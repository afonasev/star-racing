using System;
using System.Reflection;
using UnityEngine;

namespace StarRacingPrototype
{
    public sealed class RoadImpactContactTrace : MonoBehaviour
    {
        public string contacts="none";
        void OnCollisionEnter(Collision collision) { Record(collision); }
        void OnCollisionStay(Collision collision) { Record(collision); }
        void Record(Collision collision)
        {
            if(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_SUBSTEPS")!=null && Environment.GetEnvironmentVariable("STAR_RACING_ROAD_REFERENCE")!="1")contacts=string.Empty;
            contacts+=" collider="+collision.collider.name+" offset="+collision.collider.contactOffset+" impulse="+collision.impulse.ToString("G9")+" impulseMagnitude="+collision.impulse.magnitude.ToString("G9");
            for(int i=0;i<collision.contactCount;i++) {var c=collision.GetContact(i);contacts+=" point="+c.point.ToString("G9")+" normal="+c.normal.ToString("G9")+" separation="+c.separation.ToString("G9");}
        }
    }
    // Fixture-only observer. It reads native contacts without modifying a solver field.
    static class RoadNativeContactObserver
    {
        sealed class Row
        {
            public bool ccd;public int first,second,body,otherBody;
            public Vector3 position,otherPosition,velocity,otherVelocity,angular,otherAngular,point,normal,target;
            public Quaternion rotation,otherRotation;public float separation,limit,friction,staticFriction,bounce;public uint face;public ModifiableMassProperties mass;
            public override string ToString() => "PRE_SOLVER ccd="+ccd+" colliders="+first+","+second+" bodies="+body+","+otherBody+
                " pose="+position.ToString("G9")+" rotation="+rotation.ToString("G9")+" otherPose="+otherPosition.ToString("G9")+" otherRotation="+otherRotation.ToString("G9")+
                " velocity="+velocity.ToString("G9")+" otherVelocity="+otherVelocity.ToString("G9")+" angular="+angular.ToString("G9")+" otherAngular="+otherAngular.ToString("G9")+
                " point="+point.ToString("G9")+" normal="+normal.ToString("G9")+" separation="+separation.ToString("G9")+" face="+face+" maxImpulse="+limit.ToString("G9")+" target="+target.ToString("G9")+" massScales="+mass.inverseMassScale+","+mass.inverseInertiaScale+","+mass.otherInverseMassScale+","+mass.otherInverseInertiaScale+" friction="+friction+","+staticFriction+" bounce="+bounce;
        }
        static readonly System.Collections.Concurrent.ConcurrentQueue<Row> pending=new System.Collections.Concurrent.ConcurrentQueue<Row>();
        static Row[] latest=Array.Empty<Row>();static bool active;
        public static void Begin(GameObject root)
        {
            if(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_OBSERVER")!="1")return;
            active=true;foreach(var collider in root.GetComponentsInChildren<Collider>())Register(collider);
            Physics.ContactModifyEvent+=Regular;Physics.ContactModifyEventCCD+=Ccd;
        }
        public static void Register(Collider collider)
        {
            if(!active)return;collider.hasModifiableContacts=true;
            Debug.Log("CONTACT_OBSERVER_COLLIDER id="+collider.GetInstanceID()+" name="+collider.name+" type="+collider.GetType().Name);
        }
        static void Regular(PhysicsScene scene,Unity.Collections.NativeArray<ModifiableContactPair> pairs)=>Capture(pairs,false);
        static void Ccd(PhysicsScene scene,Unity.Collections.NativeArray<ModifiableContactPair> pairs)=>Capture(pairs,true);
        static void Capture(Unity.Collections.NativeArray<ModifiableContactPair> pairs,bool ccd)
        {
            foreach(var pair in pairs)for(int i=0;i<pair.contactCount;i++) {
                if(pending.Count>=4096)continue;
                pending.Enqueue(new Row {ccd=ccd,first=pair.colliderInstanceID,second=pair.otherColliderInstanceID,body=pair.bodyInstanceID,otherBody=pair.otherBodyInstanceID,
                    position=pair.position,rotation=pair.rotation,otherPosition=pair.otherPosition,otherRotation=pair.otherRotation,
                    velocity=pair.bodyVelocity,otherVelocity=pair.otherBodyVelocity,angular=pair.bodyAngularVelocity,otherAngular=pair.otherBodyAngularVelocity,
                    point=pair.GetPoint(i),normal=pair.GetNormal(i),separation=pair.GetSeparation(i),face=pair.GetFaceIndex(i),limit=pair.GetMaxImpulse(i),target=pair.GetTargetVelocity(i),mass=pair.massProperties,friction=pair.GetDynamicFriction(i),staticFriction=pair.GetStaticFriction(i),bounce=pair.GetBounciness(i)});
            }
        }
        public static void AfterStep()
        {
            var rows=new System.Collections.Generic.List<Row>();while(pending.TryDequeue(out var row))rows.Add(row);latest=rows.ToArray();
        }
        public static string For(Collider collider,System.Collections.Generic.Dictionary<int,Collider> inventory=null)
        {
            if(!active)return string.Empty;int id=collider.GetInstanceID();string result="";
            foreach(var row in latest)if(row.first==id || row.second==id) {
                result+="\n"+row;
                int other=row.first==id?row.second:row.first;
                if(inventory!=null && inventory.TryGetValue(other,out var target)) {
                    result+="\nCOLLIDER_DETAIL id="+other+" name="+target.name+" type="+target.GetType().Name;
                    if(target is MeshCollider mesh) {
                        result+=" convex="+mesh.convex+" mesh="+mesh.sharedMesh.name;
                        if(mesh.convex)foreach(var v in mesh.sharedMesh.vertices)result+=" worldVertex="+mesh.transform.TransformPoint(v).ToString("G9");
                    }
                }
            }
            return result.Length==0?"\nPRE_SOLVER no-captured-contact-points":""+result;
        }
        public static void End()
        {
            if(!active)return;Physics.ContactModifyEvent-=Regular;Physics.ContactModifyEventCCD-=Ccd;active=false;
            while(pending.TryDequeue(out _)){}latest=Array.Empty<Row>();
        }
    }
    public static class RoadVolumeChecks
    {
        static int impacts, probes;
        static TrackBuilder substepTrack;
        static float maxObservedPenetration,maxFrozenSegmentPenetration;
        static readonly System.Collections.Generic.Dictionary<int,System.Collections.Generic.Queue<string>> histories=new System.Collections.Generic.Dictionary<int,System.Collections.Generic.Queue<string>>();
        static void Simulate(float dt)
        {
            int count=int.Parse(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_SUBSTEPS")??RacePhysicsStepper.Substeps.ToString());
            var previous=new System.Collections.Generic.Dictionary<int,(Vector3 position,Quaternion rotation)>();
            foreach(var car in RacePhysicsStepper.Vehicles)if(car!=null)previous[car.GetInstanceID()]=(car.Body.position,car.Body.rotation);
            int part=0;bool reference=Environment.GetEnvironmentVariable("STAR_RACING_ROAD_REFERENCE")=="1";
            RacePhysicsStepper.SimulatePrepared(RacePhysicsStepper.Vehicles,dt,count,()=> {
                RoadNativeContactObserver.AfterStep();
                if(substepTrack!=null)foreach(var car in RacePhysicsStepper.Vehicles) {
                    if(car==null || !car.gameObject.activeInHierarchy)continue;
                    var box=car.GetComponent<BoxCollider>();float depth=SlabPenetration(box,car.Body.position,car.Body.rotation,FlatSlab(substepTrack));
                    maxObservedPenetration=Mathf.Max(maxObservedPenetration,depth);
                    if((car.Body.constraints&RigidbodyConstraints.FreezeRotation)==RigidbodyConstraints.FreezeRotation) {
                        var before=previous[car.GetInstanceID()];Check(Quaternion.Angle(before.rotation,car.Body.rotation)<.001f,"frozen segment oracle received rotating chassis");
                        float swept=FrozenSegmentPenetration(box,before.position,car.Body.position,car.Body.rotation,FlatSlab(substepTrack));
                        maxFrozenSegmentPenetration=Mathf.Max(maxFrozenSegmentPenetration,swept);Check(swept<=.04f,"frozen native segment crossed road depth="+swept+" car="+car.name);
                    }
                    previous[car.GetInstanceID()]=(car.Body.position,car.Body.rotation);
                    var trace=car.GetComponent<RoadImpactContactTrace>();
                    if(reference) {
                        int id=car.GetInstanceID();if(!histories.TryGetValue(id,out var history))histories[id]=history=new System.Collections.Generic.Queue<string>();
                        history.Enqueue("NATIVE_REFERENCE part="+part+" car="+car.name+" depth="+depth+" pose="+car.Body.position.ToString("F5")+" rotation="+car.Body.rotation.eulerAngles.ToString("F5")+" velocity="+car.Body.linearVelocity.ToString("F5")+" angular="+car.Body.angularVelocity.ToString("F5")+" contacts="+(trace==null?"none":trace.contacts)+RoadNativeContactObserver.For(box));
                        if(history.Count>12)history.Dequeue();
                        if(depth>.04f)foreach(var entry in history)Debug.Log(entry);
                        if(trace!=null)trace.contacts=string.Empty;
                    }
                    Check(box.enabled && depth<=.04f,"native substep car="+car.name+" penetration="+depth+" part="+part+" of="+count+" pose="+car.Body.position+" velocity="+car.Body.linearVelocity+" angular="+car.Body.angularVelocity);
                }
                part++;
            });
        }
        static void Check(bool valid, string message) { if (!valid) throw new Exception("Road volume: " + message); }
        public static Procedural.Definition FlatRoad()
        {
            var samples=new Procedural.Sample[61];
            for(int i=0;i<samples.Length;i++) samples[i]=new Procedural.Sample {
                index=i,distance=i*5,progress=i/60.0,halfWidth=8,
                position=new Procedural.DVec(1000,120,i*5),tangent=new Procedural.DVec(0,0,1),
                normal=new Procedural.DVec(0,1,0),right=new Procedural.DVec(-1,0,0),kind="straight" };
            return new Procedural.Definition { theme="cloud-city",guardrailMode="none",samples=samples,totalLength=300,
                branches=Array.Empty<Procedural.Branch>(),jumps=Array.Empty<Procedural.Jump>(),
                patterns=Array.Empty<Procedural.Pattern>(),checkpoints=Array.Empty<Procedural.Checkpoint>() };
        }
        static MeshCollider Shell(TrackBuilder track)
        {
            foreach(var collider in track.GetComponentsInChildren<MeshCollider>())
                if(collider.gameObject.name==RoadVolumeMesh.MeshName)return collider;
            throw new Exception("Missing road shell");
        }
        static Mesh ShellVisual(TrackBuilder track)=>Shell(track).GetComponent<MeshFilter>().sharedMesh;
        static bool BoundaryRaycast(TrackBuilder track,Ray ray,out RaycastHit result,float distance)
        {
            result=default;bool found=false;
            foreach(var hit in Physics.RaycastAll(ray,distance)) {
                var collider=hit.collider as MeshCollider;
                if(collider==null || !collider.transform.IsChildOf(track.transform) || collider.sharedMesh==null)continue;
                string name=collider.sharedMesh.name;
                if(name!=RoadVolumeMesh.BottomName && name!=RoadVolumeMesh.ExteriorName && name!=RoadVolumeMesh.SupportName)continue;
                if(!found || hit.distance<result.distance){result=hit;found=true;}
            }
            return found;
        }
        static bool RenderedShellRaycast(TrackBuilder track,Ray ray,float distance)
        {
            // Inspect the authored exterior independently of the inward faces of
            // contained convex supports. This temporary query collider never simulates.
            var go=new GameObject("authored shell query");go.transform.SetParent(track.transform,false);
            try {var collider=go.AddComponent<MeshCollider>();collider.sharedMesh=ShellVisual(track);return collider.Raycast(ray,out _,distance);}
            finally {UnityEngine.Object.DestroyImmediate(go);}
        }
        public static void ValidateFlatClosure()
        {
            foreach(var rotation in new[]{Quaternion.identity,Quaternion.Euler(31,42,17)}) {
                var definition=FlatRoad();
                foreach(var sample in definition.samples) {
                    Vector3 position=rotation*new Vector3((float)sample.position.x,(float)sample.position.y,(float)sample.position.z);
                    Vector3 normal=rotation*Vector3.up,tangent=rotation*Vector3.forward,right=rotation*Vector3.left;
                    sample.position=new Procedural.DVec(position.x,position.y,position.z);sample.normal=new Procedural.DVec(normal.x,normal.y,normal.z);
                    sample.tangent=new Procedural.DVec(tangent.x,tangent.y,tangent.z);sample.right=new Procedural.DVec(right.x,right.y,right.z);
                }
                var root=new GameObject("independent closed flat slab");
                try {
                    var track=root.AddComponent<TrackBuilder>();track.Build(new TrackRoute(definition));
                    Quaternion inverse=Quaternion.Inverse(rotation);var shell=Shell(track);var top=track.GetComponentInChildren<TrackSurface>().GetComponent<MeshCollider>();
                    float first=(inverse*track.Route.Samples[0].position).z,last=(inverse*track.Route.Samples[track.Route.Samples.Length-1].position).z;
                    var expected=new Bounds(new Vector3(1000,119.5f,(first+last)*.5f),new Vector3(16,1,last-first));
                    var vertices=ShellVisual(track).vertices;var indices=ShellVisual(track).triangles;
                    for(int i=0;i<vertices.Length;i++) {
                        Vector3 point=inverse*vertices[i];Vector3 outside=point-expected.ClosestPoint(point);
                        Check(outside.magnitude<.002f,"wall/cap escaped independently authored slab point="+point+" bounds="+expected);
                    }
                    for(int i=0;i<indices.Length;i+=3) {
                        Vector3 a=inverse*vertices[indices[i]],b=inverse*vertices[indices[i+1]],c=inverse*vertices[indices[i+2]];
                        Vector3 normal=Vector3.Cross(b-a,c-a).normalized;
                        Vector3 outward=Vector3.zero;
                        if(Mathf.Abs(a.y-119)<.002f && Mathf.Abs(b.y-119)<.002f && Mathf.Abs(c.y-119)<.002f)outward=Vector3.down;
                        else if(Mathf.Abs(a.x-1008)<.002f && Mathf.Abs(b.x-1008)<.002f && Mathf.Abs(c.x-1008)<.002f)outward=Vector3.right;
                        else if(Mathf.Abs(a.x-992)<.002f && Mathf.Abs(b.x-992)<.002f && Mathf.Abs(c.x-992)<.002f)outward=Vector3.left;
                        else if(Mathf.Abs(a.z-first)<.002f && Mathf.Abs(b.z-first)<.002f && Mathf.Abs(c.z-first)<.002f)outward=Vector3.back;
                        else if(Mathf.Abs(a.z-last)<.002f && Mathf.Abs(b.z-last)<.002f && Mathf.Abs(c.z-last)<.002f)outward=Vector3.forward;
                        Check(outward.sqrMagnitude>0 && Vector3.Dot(normal,outward)>.99f,"unexpected interior/reversed slab boundary");
                    }
                    Physics.SyncTransforms();
                    for(int face=0;face<4;face++)foreach(float height in new[]{119.001f,119.25f,119.5f,119.75f,119.999f,120.5f}) {
                        Vector3 point=face==0?new Vector3(1008,height,50):face==1?new Vector3(992,height,50):face==2?new Vector3(1000,height,first):new Vector3(1000,height,last);
                        Vector3 outward=rotation*(face==0?Vector3.right:face==1?Vector3.left:face==2?Vector3.back:Vector3.forward);
                        bool hit=BoundaryRaycast(track,new Ray(rotation*point+outward*.4f,-outward),out var found,.8f);
                        Check(hit==(height<120),"side/cap coverage at independently authored height="+height+" face="+face);
                        if(hit)Check(Vector3.Distance(found.point,rotation*point)<.002f && Vector3.Dot(found.normal,outward)>.99f,"independent boundary ray mismatch");
                    }
                    // Closed oriented triangle complex for flat slabs, independent of emitted wall coordinates.
                    var edges=new System.Collections.Generic.Dictionary<string,(int count,int orientation)>();
                    Action<Mesh> add=mesh=> {
                        var points=mesh.vertices;var triangles=mesh.triangles;
                        Func<Vector3,string> key=point=> {var local=inverse*point;return Mathf.RoundToInt(local.x*1000)+","+Mathf.RoundToInt(local.y*1000)+","+Mathf.RoundToInt(local.z*1000);};
                        for(int i=0;i<triangles.Length;i+=3)for(int side=0;side<3;side++) {
                            string a=key(points[triangles[i+side]]),b=key(points[triangles[i+(side+1)%3]]);int direction=string.CompareOrdinal(a,b)<0?1:-1;
                            string edge=direction>0?a+"/"+b:b+"/"+a;edges.TryGetValue(edge,out var value);edges[edge]=(value.count+1,value.orientation+direction);
                        }
                    };
                    add(top.sharedMesh);add(ShellVisual(track));
                    foreach(var edge in edges)Check(edge.Value.count==2 && edge.Value.orientation==0,"open/nonmanifold flat slab edge="+edge.Key+" count="+edge.Value.count);
                    Debug.Log("ROAD_VOLUME_INDEPENDENT_CLOSURE_OK rotation="+rotation.eulerAngles+" edges="+edges.Count);
                }finally {UnityEngine.Object.DestroyImmediate(root);}
            }
        }
        static void Geometry(TrackBuilder track)
        {
            var top=track.GetComponentInChildren<TrackSurface>();var shell=Shell(track);
            Check(shell.GetComponent<TrackSurface>()==null,"shell became a magnetic surface");
            var visual=ShellVisual(track);
            Check(shell.sharedMesh.name==RoadVolumeMesh.BottomName,"shell bottom collision missing");
            Check(shell.sharedMesh.triangles.Length==top.sourceTriangles.Length,"bottom triangle count changed");
            var bottomIndices=shell.sharedMesh.triangles;var visualIndices=visual.triangles;
            for(int i=0;i<bottomIndices.Length;i++)Check(bottomIndices[i]==visualIndices[i],"bottom collision disagrees with visual source");
            Check(shell.sharedMaterial==top.GetComponent<MeshCollider>().sharedMaterial,"shell contact friction changed");
            var upper=top.sourceVertices;var lower=visual.vertices;
            Check(lower.Length>upper.Length,"missing side/end faces");
            for(int i=0;i<upper.Length;i++) {
                var face=top.triangleFaces[(i/4)*2];
                var frame=track.Route.Evaluate(i%2==0?face.startDistance:face.endDistance);
                Check(Vector3.Distance(lower[i],upper[i]-frame.normal)<.002f,"incorrect slab thickness");
            }
            Physics.SyncTransforms();
            int supports=0;
            foreach(var collider in track.GetComponentsInChildren<MeshCollider>()) {
                if(collider.sharedMesh==null || collider.sharedMesh.name!=RoadVolumeMesh.SupportName)continue;
                supports++;var points=collider.sharedMesh.vertices;
                Check(collider.enabled && !collider.isTrigger && collider.convex && points.Length==4 && collider.sharedMesh.triangles.Length==12,"invalid native support hull");
                Check(collider.GetComponent<TrackSurface>()==null && collider.sharedMaterial==shell.sharedMaterial,"support magnetic/friction contract changed");
                Vector3 a=collider.transform.TransformPoint(points[0]),b=collider.transform.TransformPoint(points[1]),c=collider.transform.TransformPoint(points[2]);
                Vector3 outward=Vector3.Cross(b-a,c-a).normalized,center=(a+b+c)/3;
                Check(collider.Raycast(new Ray(center+outward*.02f,-outward),out var cooked,.04f),"native cooking lost support base "+supports);
                Check(Vector3.Distance(cooked.point,center)<.002f && Vector3.Dot(cooked.normal,outward)>.99f,"native cooking changed support base "+supports);
            }
            Check(supports>0,"production exterior supports missing");
            Debug.Log("ROAD_SUPPORT_COOKING_OK supports="+supports+" nativeBaseRays="+supports+" geometryBudget=.002m");
            var indices=visual.triangles;
            // Independent physical readback of outward faces, spread across bottom and walls.
            int stride=Mathf.Max(1,indices.Length/900/3);
            for(int triangle=0;triangle<indices.Length/3;triangle+=stride) {
                Vector3 a=lower[indices[triangle*3]],b=lower[indices[triangle*3+1]],c=lower[indices[triangle*3+2]];
                Vector3 normal=Vector3.Cross(b-a,c-a);if(normal.sqrMagnitude<1e-10f)continue;
                normal.Normalize();Vector3 center=(a+b+c)/3;
                Check(BoundaryRaycast(track,new Ray(center+normal*.05f,-normal),out var hit,.1f),"open/reversed native exterior face");
                Check(Vector3.Distance(hit.point,center)<.002f,"shell physical face disagrees with rendered face"); probes++;
            }
            // Road queries must reject underside/side hits while preserving top support.
            var flat=track.Route.Evaluate(80);
            Check(track.RaycastRoad(flat.position+flat.normal,-flat.normal,2,80,out _),"top lost suspension support");
            Check(!track.RaycastRoad(flat.position-flat.normal*2,flat.normal,1.5f,80,out _),"underside became suspension support");
        }
        // Independent finite-volume oracle: the flat authored road is an AABB;
        // the physical chassis is an OBB. Infinite side planes would incorrectly
        // reject corners that rotate above or below the one-metre slab.
        static float SlabPenetration(BoxCollider box, Vector3 position, Quaternion rotation, Bounds slab)
        {
            Vector3 center=position+rotation*box.center, half=box.size*.5f, delta=center-slab.center;
            Vector3[] boxAxes={rotation*Vector3.right,rotation*Vector3.up,rotation*Vector3.forward};
            Vector3[] slabAxes={Vector3.right,Vector3.up,Vector3.forward};
            float penetration=float.PositiveInfinity;
            for(int i=0;i<15;i++) {
                Vector3 axis=i<3?slabAxes[i]:i<6?boxAxes[i-3]:Vector3.Cross(slabAxes[(i-6)/3],boxAxes[(i-6)%3]);
                if(axis.sqrMagnitude<1e-10f)continue;axis.Normalize();
                float a=Mathf.Abs(Vector3.Dot(axis,boxAxes[0]))*half.x+Mathf.Abs(Vector3.Dot(axis,boxAxes[1]))*half.y+Mathf.Abs(Vector3.Dot(axis,boxAxes[2]))*half.z;
                float b=Mathf.Abs(axis.x)*slab.extents.x+Mathf.Abs(axis.y)*slab.extents.y+Mathf.Abs(axis.z)*slab.extents.z;
                float overlap=a+b-Mathf.Abs(Vector3.Dot(delta,axis));
                if(overlap<=0)return 0;penetration=Mathf.Min(penetration,overlap);
            }
            return penetration;
        }
        static System.Collections.Generic.List<Vector3> SatAxes(Quaternion rotation)
        {
            var result=new System.Collections.Generic.List<Vector3>();
            Vector3[] car={rotation*Vector3.right,rotation*Vector3.up,rotation*Vector3.forward},road={Vector3.right,Vector3.up,Vector3.forward};
            for(int i=0;i<15;i++) {
                Vector3 axis=i<3?road[i]:i<6?car[i-3]:Vector3.Cross(road[(i-6)/3],car[(i-6)%3]);
                if(axis.sqrMagnitude<1e-10f)continue;axis.Normalize();result.Add(axis);
            }return result;
        }
        static float ProjectionRadius(BoxCollider box,Quaternion rotation,Bounds slab,Vector3 axis)
        {
            var half=box.size*.5f;
            return Mathf.Abs(Vector3.Dot(axis,rotation*Vector3.right))*half.x+Mathf.Abs(Vector3.Dot(axis,rotation*Vector3.up))*half.y+
                Mathf.Abs(Vector3.Dot(axis,rotation*Vector3.forward))*half.z+Mathf.Abs(axis.x)*slab.extents.x+Mathf.Abs(axis.y)*slab.extents.y+Mathf.Abs(axis.z)*slab.extents.z;
        }
        // Exact maximum of the SAT MTD on the straight segment joining native poses
        // for a fixed orientation. This rejects between-return linear skips.
        static float FrozenSegmentPenetration(BoxCollider box,Vector3 from,Vector3 to,Quaternion rotation,Bounds slab)
        {
            Vector3 delta=from+rotation*box.center-slab.center,motion=to-from;
            var lines=new System.Collections.Generic.List<(double intercept,double slope)>();
            foreach(var axis in SatAxes(rotation)) {
                double radius=ProjectionRadius(box,rotation,slab,axis),offset=Vector3.Dot(delta,axis),slope=Vector3.Dot(motion,axis);
                lines.Add((radius-offset,-slope));lines.Add((radius+offset,slope));
            }
            Func<double,double> at=t=> {double depth=double.PositiveInfinity;foreach(var line in lines)depth=Math.Min(depth,line.intercept+line.slope*t);return Math.Max(0,depth);};
            double maximum=Math.Max(at(0),at(1));
            for(int i=0;i<lines.Count;i++)for(int j=i+1;j<lines.Count;j++) {
                double denominator=lines[i].slope-lines[j].slope;if(Math.Abs(denominator)<1e-12)continue;
                double t=(lines[j].intercept-lines[i].intercept)/denominator;
                if(t>0 && t<1)maximum=Math.Max(maximum,at(t));
            }return (float)maximum;
        }
        static bool FiniteEscape(BoxCollider box,Vector3 position,Quaternion rotation,Vector3 velocity,Bounds slab)
        {
            Vector3 delta=position+rotation*box.center-slab.center;
            foreach(var axis in SatAxes(rotation)) {
                float offset=Vector3.Dot(delta,axis),gap=Mathf.Abs(offset)-ProjectionRadius(box,rotation,slab,axis);
                if(gap>=.001f && Vector3.Dot(velocity,axis)*Mathf.Sign(offset)>=.001f) {
                    Debug.Log("ROAD_VOLUME_FINITE_ESCAPE gap="+gap+" axis="+(axis*Mathf.Sign(offset)).ToString("G9")+" awaySpeed="+(Vector3.Dot(velocity,axis)*Mathf.Sign(offset)));return true;
                }
            }return false;
        }
        static void ValidateSlabOracle()
        {
            var obj=new GameObject("finite-volume oracle");
            try {
                var box=obj.AddComponent<BoxCollider>();box.size=new Vector3(2,1,4);box.center=Vector3.zero;
                var slab=new Bounds(Vector3.zero,new Vector3(16,1,20));
                Check(SlabPenetration(box,new Vector3(0,1,0),Quaternion.identity,slab)==0,"SAT touching oracle");
                Check(Mathf.Abs(SlabPenetration(box,new Vector3(0,.9f,0),Quaternion.identity,slab)-.1f)<1e-5f,"SAT penetration oracle");
                Check(SlabPenetration(box,new Vector3(8.1f,1.8f,0),Quaternion.Euler(0,0,90),slab)==0,"SAT finite edge oracle");
                Check(Mathf.Abs(SlabPenetration(box,Vector3.zero,Quaternion.identity,slab)-1)<1e-5f,"SAT containment oracle");
                Check(Mathf.Abs(FrozenSegmentPenetration(box,new Vector3(0,2,0),new Vector3(0,-2,0),Quaternion.identity,slab)-1)<1e-5f,"swept SAT skipped full crossing");
                Check(Mathf.Abs(FrozenSegmentPenetration(box,new Vector3(8.99f,2,0),new Vector3(8.99f,-2,0),Quaternion.identity,slab)-.01f)<1e-5f,"swept SAT finite-edge grazing");
                Check(FrozenSegmentPenetration(box,new Vector3(9.1f,2,0),new Vector3(9.1f,-2,0),Quaternion.identity,slab)==0,"swept SAT finite miss");
                Check(Mathf.Abs(FrozenSegmentPenetration(box,new Vector3(8.961f,2,0),new Vector3(8.961f,-2,0),Quaternion.identity,slab)-.039f)<1e-5f,"swept SAT below gate");
                Check(Mathf.Abs(FrozenSegmentPenetration(box,new Vector3(8.959f,2,0),new Vector3(8.959f,-2,0),Quaternion.identity,slab)-.041f)<1e-5f,"swept SAT above gate");
                Check(FrozenSegmentPenetration(box,new Vector3(9,2,0),new Vector3(9,-2,0),Quaternion.identity,slab)==0,"swept SAT touching");
                Check(Mathf.Abs(FrozenSegmentPenetration(box,Vector3.zero,Vector3.zero,Quaternion.identity,slab)-1)<1e-5f,"swept SAT zero translation");
            } finally {UnityEngine.Object.DestroyImmediate(obj);}
        }
        static Bounds FlatSlab(TrackBuilder track)
        {
            var bounds=track.GetComponentInChildren<TrackSurface>().GetComponent<MeshFilter>().sharedMesh.bounds;
            bounds.SetMinMax(bounds.min-Vector3.up*RoadVolumeMesh.Thickness,bounds.max);return bounds;
        }
        static void ConfigureCcdSlices(MagneticVehicle car)
        {
            if(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_SOLID_BOX")=="1")car.Body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
            if(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_SUBSTEPS")!=null)car.Body.collisionDetectionMode=(CollisionDetectionMode)Enum.Parse(typeof(CollisionDetectionMode),Environment.GetEnvironmentVariable("STAR_RACING_ROAD_CCD_MODE")??"ContinuousDynamic");
            if(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_CCD_SLICES")!="1")return;
            var body=car.Body;var chassis=car.GetComponent<BoxCollider>();
            float mass=body.mass;Vector3 com=body.centerOfMass,inertia=body.inertiaTensor;Quaternion tensorRotation=body.inertiaTensorRotation;
            int count=Mathf.CeilToInt(chassis.size.y/.1f);float height=chassis.size.y/count;
            for(int i=0;i<count;i++) {
                var obj=new GameObject("fixture CCD slice "+i);obj.transform.SetParent(car.transform,false);obj.layer=30;
                var box=obj.AddComponent<BoxCollider>();var size=chassis.size;size.y=height;box.size=size;
                var center=chassis.center;center.y+=-chassis.size.y*.5f+height*(i+.5f);box.center=center;
                box.sharedMaterial=chassis.sharedMaterial;
            }
            body.mass=mass;body.centerOfMass=com;body.inertiaTensor=inertia;body.inertiaTensorRotation=tensorRotation;
            body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            Check(chassis.enabled && body.mass==mass && body.centerOfMass==com && body.inertiaTensor==inertia && body.inertiaTensorRotation==tensorRotation,"CCD slices changed chassis mass properties");
        }
        static void DiagnosticSolid(GameObject root,Bounds bounds,PhysicsMaterial material,System.Collections.Generic.List<Mesh> meshes)
        {
            var obj=new GameObject("diagnostic closed volume");obj.transform.SetParent(root.transform);
            if(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_SOLID_MESH")!="1") {
                var box=obj.AddComponent<BoxCollider>();box.center=bounds.center;box.size=bounds.size;box.sharedMaterial=material;return;
            }
            var mesh=new Mesh {name="exact convex diagnostic volume"};meshes.Add(mesh);
            Vector3 a=bounds.min,b=bounds.max;
            mesh.vertices=new[]{new Vector3(a.x,a.y,a.z),new Vector3(b.x,a.y,a.z),new Vector3(a.x,b.y,a.z),new Vector3(b.x,b.y,a.z),
                new Vector3(a.x,a.y,b.z),new Vector3(b.x,a.y,b.z),new Vector3(a.x,b.y,b.z),new Vector3(b.x,b.y,b.z)};
            mesh.triangles=new[]{0,2,1,1,2,3,4,5,6,5,7,6,0,4,2,2,4,6,1,3,5,3,7,5,0,1,4,1,5,4,2,6,3,3,6,7};mesh.RecalculateBounds();
            var collider=obj.AddComponent<MeshCollider>();collider.sharedMesh=mesh;collider.convex=true;collider.sharedMaterial=material;
        }
        static void ThinSupportControl(TrackBuilder track,System.Collections.Generic.List<Mesh> meshes)
        {
        var slab=FlatSlab(track);int supports=0;
        foreach(var collider in track.GetComponentsInChildren<MeshCollider>()) {
            if(collider.sharedMesh==null || collider.sharedMesh.name!=RoadVolumeMesh.SupportName)continue;
            var points=collider.sharedMesh.vertices;Vector3 n=Vector3.Cross(points[1]-points[0],points[2]-points[0]).normalized;
            points[3]=(points[1]+points[2])*.5f-n*.02f;
            foreach(var p in points)Check(slab.Contains(collider.transform.TransformPoint(p)),"thin control escaped exact authored slab");
            Check(Vector3.Dot(n,points[3]-points[0])<0,"thin control apex not inward");
            var mesh=new Mesh {name=RoadVolumeMesh.SupportName};meshes.Add(mesh);mesh.vertices=points;mesh.triangles=collider.sharedMesh.triangles;mesh.RecalculateBounds();
            Physics.BakeMesh(mesh.GetEntityId(),true,collider.cookingOptions);collider.sharedMesh=mesh;supports++;
        }
        Debug.Log("ROAD_THIN_SUPPORT_CONTROL_READY supports="+supports+" depth=.02 containment=exact-AABB verticesUnchanged=true");
        }
        public static void DiagnoseCcdSlices()
        {
            Check(Application.isPlaying,"CCD slices require actual Play Mode");
            ValidateFlatClosure();ValidateSlabOracle();maxObservedPenetration=0;maxFrozenSegmentPenetration=0;histories.Clear();var mode=Physics.simulationMode;float dt=Time.fixedDeltaTime;Physics.simulationMode=SimulationMode.Script;Time.fixedDeltaTime=.01f;
            var root=new GameObject("CCD slice control");var meshes=new System.Collections.Generic.List<Mesh>();
            try {
                var track=root.AddComponent<TrackBuilder>();track.Build(new TrackRoute(FlatRoad()));
                if(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_THIN_SUPPORT_CONTROL")=="1")ThinSupportControl(track,meshes);
                if(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_SOLID_BOX")=="1") {
                    foreach(var mesh in track.GetComponentsInChildren<MeshCollider>())mesh.enabled=false;
                    var bounds=FlatSlab(track);
                    if(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_SOLID_SEAM")=="1") {
                        var first=bounds;first.SetMinMax(bounds.min,new Vector3(bounds.max.x,bounds.max.y,track.Route.Evaluate(80).position.z));
                        var second=bounds;second.SetMinMax(new Vector3(bounds.min.x,bounds.min.y,track.Route.Evaluate(80).position.z),bounds.max);
                        DiagnosticSolid(root,first,Shell(track).sharedMaterial,meshes);DiagnosticSolid(root,second,Shell(track).sharedMaterial,meshes);
                    } else DiagnosticSolid(root,bounds,Shell(track).sharedMaterial,meshes);
                }
                if(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_SUBSTEPS")!=null || Environment.GetEnvironmentVariable("STAR_RACING_ROAD_SOLID_BOX")=="1")substepTrack=track;
                RoadNativeContactObserver.Begin(root);NativeImpactMatrix(track);
            }finally {RoadNativeContactObserver.End();substepTrack=null;UnityEngine.Object.DestroyImmediate(root);foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);Physics.simulationMode=mode;Time.fixedDeltaTime=dt;}
        }
        static void NativeImpactMatrix(TrackBuilder track)
        {
            if(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_SHIFT_DIAGNOSTIC")=="1") {
                foreach(float shift in new[]{-.251f,-.249f,-.017f,.017f,.249f,.251f})AngularImpulseMatrix(track,shift,shift);return;
            }
            if(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_VELOCITY_DIAGNOSTIC")=="1") {
                var frame=track.Route.Evaluate(80);Impact(track,frame.position-frame.right*8-frame.normal*.5f,-frame.right,frame.Rotation*Quaternion.Euler(10,30,20),300,.61f,"tilted left");return;
            }
            if(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_SENTINELS")=="1") {AngularImpulseMatrix(track);return;}
            int failed=0,passed=0;
                var f=track.Route.Evaluate(80);var start=track.Route.Evaluate(0);
                Vector3[] points={f.position,f.position-f.normal,f.position+f.right*8-f.normal*.5f,f.position-f.right*8-f.normal*.5f,start.position-start.normal*.5f};
                Vector3[] normals={f.normal,-f.normal,f.right,-f.right,-start.tangent};
                string[] labels={"top","bottom","right","left","start cap"};
                foreach(float speed in new[]{5f,42f,139f,300f,1000f})foreach(float phase in new[]{.13f,.61f,.97f})for(int side=0;side<5;side++) {
                    try {Impact(track,points[side],normals[side],f.Rotation,speed,phase,labels[side]);passed++;}
                    catch(Exception e) {failed++;Debug.Log("CCD_CONTROL_FAILED "+e.Message);}
                }
                for(int side=0;side<5;side++)foreach(string kind in new[]{"falling ","tilted ","rotating ","grazing "}) {
                    try {Impact(track,points[side],normals[side],kind=="tilted "?f.Rotation*Quaternion.Euler(10,30,20):f.Rotation,
                        kind=="grazing "?42:300,.61f,kind+labels[side],kind=="falling ",kind=="rotating "||kind=="grazing ",false,null,
                        kind=="grazing "?(side==4?f.right*139:f.tangent*139):Vector3.zero);passed++;}
                    catch(Exception e) {failed++;Debug.Log("CCD_CONTROL_FAILED "+e.Message);}
                }
                try {AngularAndImpulse(track);passed++;}catch(Exception e) {failed++;Debug.Log("CCD_CONTROL_FAILED "+e.Message);}
                if(failed==0 && Environment.GetEnvironmentVariable("STAR_RACING_ROAD_EXPANDED")=="1")AngularImpulseMatrix(track);
                Debug.Log("CCD_CONTROL_SUMMARY passed="+passed+" failed="+failed+" maxPenetration="+maxObservedPenetration+" maxFrozenSegmentPenetration="+maxFrozenSegmentPenetration);
                Check(failed==0,"native CCD experiment failed "+failed+" cases");
        }
        static void CheckProductionChassis(MagneticVehicle car)
        {
            var chassis=car.GetComponent<BoxCollider>();var boxes=car.GetComponentsInChildren<BoxCollider>();
            Check(boxes.Length==5 && chassis.size.Equals(VehicleGeometry.ChassisSize) && chassis.center.Equals(VehicleGeometry.ChassisCenter),"production chassis envelope changed");
            Check(LayerMask.NameToLayer("Vehicle")==MagneticVehicle.VehicleCollisionLayer && LayerMask.NameToLayer("DrivableRoad")==TrackBuilder.RoadCollisionLayer && car.gameObject.layer==MagneticVehicle.VehicleCollisionLayer,"vehicle collision layer mismatch root="+car.gameObject.layer+" named="+LayerMask.NameToLayer("Vehicle")+" expected="+MagneticVehicle.VehicleCollisionLayer);
            var bounds=new Bounds(chassis.center,chassis.size);
            foreach(var box in boxes)if(box!=chassis) {
                Vector3 low=box.center-box.size*.5f,high=box.center+box.size*.5f;
                for(int axis=0;axis<3;axis++)Check(low[axis]>=bounds.min[axis] && high[axis]<=bounds.max[axis],"CCD guard extends outside original chassis");
                Check(box.enabled && !box.isTrigger && box.attachedRigidbody==car.Body && box.sharedMaterial==chassis.sharedMaterial && box.excludeLayers.value==((1<<MagneticVehicle.VehicleCollisionLayer)|(1<<TrackBuilder.BarrierCollisionLayer)|(box.size.z==.02f?1<<TrackBuilder.RoadCollisionLayer:0)),"CCD guard/car pair contract");
            }
            Check(car.Body.mass==1000f && car.Body.centerOfMass.Equals(new Vector3(0,-.18f,0)) && car.Body.inertiaTensor.Equals(VehicleGeometry.BaselineInertia) && car.Body.inertiaTensorRotation.Equals(VehicleGeometry.BaselineInertiaRotation),"compound chassis changed mass/COM/inertia");
        }
        static void ConfigureDiagnosticSolver(MagneticVehicle car)
        {
            CheckProductionChassis(car);
            ConfigureCcdSlices(car);RoadNativeContactObserver.Register(car.GetComponent<BoxCollider>());
            if(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_SUBSTEPS")!=null)Check(car.Body.collisionDetectionMode==(CollisionDetectionMode)Enum.Parse(typeof(CollisionDetectionMode),Environment.GetEnvironmentVariable("STAR_RACING_ROAD_CCD_MODE")??"ContinuousDynamic"),"fixture fixed CCD selection diverged");
        }
        static void Impact(TrackBuilder track, Vector3 point, Vector3 outward, Quaternion rotation, float speed, float phase, string label, bool falling = false, bool rotating = false, bool boxFixture = false, CollisionDetectionMode? detection = null, Vector3 drift = default)
        {
            var obj=new GameObject(label+" speed="+speed+" phase="+phase);obj.transform.SetParent(track.transform.parent);
            GameObject exactSlab=null;Mesh exactBottom=null;var roadColliders=track.GetComponentsInChildren<MeshCollider>();
            var roadEnabled=new bool[roadColliders.Length];for(int i=0;i<roadColliders.Length;i++)roadEnabled[i]=roadColliders[i].enabled;
            try {
                var car=obj.AddComponent<MagneticVehicle>();
                car.Initialize(track,0,Color.cyan,ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text),false);
                ConfigureDiagnosticSolver(car);car.enabled=false; var body=car.Body;var box=obj.GetComponent<BoxCollider>();
                if(detection.HasValue)body.collisionDetectionMode=detection.Value;
                if(falling) {
                    car.SetRaceContext(true,1,2,10);
                    var f=track.Route.Evaluate(80);
                    body.position=f.position+f.right*25;obj.transform.position=body.position;Physics.SyncTransforms();
                    car.PrepareProjection();
                    typeof(MagneticVehicle).GetMethod("FixedUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(car,null);
                    Check(car.IsFalling && box.enabled,"fall transition disabled road collision");
                }
                var trace=obj.AddComponent<RoadImpactContactTrace>();
                if(boxFixture) {
                    foreach(var collider in roadColliders)collider.enabled=false;
                    var bounds=FlatSlab(track);
                    if(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_THIN_WALL")=="1") {
                        float depth=float.Parse(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_WALL_DEPTH")??"0.02",System.Globalization.CultureInfo.InvariantCulture);
                        var center=bounds.center;center.x=point.x-outward.x*depth*.5f;var size=bounds.size;size.x=depth;
                        bounds=new Bounds(center,size);
                        foreach(var collider in roadColliders)if(collider.GetComponent<TrackSurface>()!=null)collider.enabled=true;
                    }
                    exactSlab=new GameObject("exact diagnostic slab");
                    var slab=exactSlab.AddComponent<BoxCollider>();slab.center=bounds.center;slab.size=bounds.size;
                    slab.sharedMaterial=roadColliders[0].sharedMaterial;
                    if(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_SEGMENT_WALL")=="1") {
                        slab.enabled=false;
                        for(float z=bounds.min.z;z<bounds.max.z;z+=.5f) {
                            var piece=exactSlab.AddComponent<BoxCollider>();var center=bounds.center;center.z=z+.25f;
                            var size=bounds.size;size.z=.5f;piece.center=center;piece.size=size;piece.sharedMaterial=slab.sharedMaterial;
                        }
                    }
                    if(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_THIN_WALL")=="1") {
                        var shell=Shell(track).sharedMesh;var top=track.GetComponentInChildren<TrackSurface>().GetComponent<MeshCollider>().sharedMesh;
                        exactBottom=new Mesh {name="thin control bottom",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};
                        exactBottom.vertices=shell.vertices;exactBottom.SetTriangles(shell.triangles,0,top.triangles.Length,0);exactBottom.RecalculateBounds();
                        var bottom=exactSlab.AddComponent<MeshCollider>();bottom.sharedMesh=exactBottom;bottom.sharedMaterial=slab.sharedMaterial;
                        Physics.SyncTransforms();
                        Check(bottom.Raycast(new Ray(FlatSlab(track).center-Vector3.up*2,Vector3.up),out var underside,3) && underside.normal.y<-.9f,"diagnostic bottom missing/reversed");
                    }
                }
                body.constraints=rotating?RigidbodyConstraints.None:RigidbodyConstraints.FreezeRotation;body.linearDamping=0;body.rotation=rotation;
                Vector3 local=Quaternion.Inverse(rotation)*outward;
                float radius=Vector3.Dot(new Vector3(Mathf.Abs(local.x),Mathf.Abs(local.y),Mathf.Abs(local.z)),box.size)*.5f;
                body.position=point+outward*(radius+.06f+speed*.01f*phase)-rotation*box.center;
                body.linearVelocity=Vector3.zero;obj.transform.SetPositionAndRotation(body.position,rotation);Physics.SyncTransforms();
                // Register the newly spawned body at rest before measuring a CCD sweep.
                Simulate(.01f);body.linearVelocity=-outward*speed+drift;
                if(rotating)body.angularVelocity=rotation*Vector3.forward*6;
                int revision=car.PositionRevision;float minimum=float.PositiveInfinity;
                for(int step=0;step<16;step++) {
                    trace.contacts=string.Empty;Simulate(.01f);
                    Vector3 axis=Quaternion.Inverse(body.rotation)*outward;
                    float actualRadius=Vector3.Dot(new Vector3(Mathf.Abs(axis.x),Mathf.Abs(axis.y),Mathf.Abs(axis.z)),box.size)*.5f;
                    float clearance=Vector3.Dot(body.position+body.rotation*box.center-point,outward)-actualRadius;
                    minimum=Mathf.Min(minimum,clearance);
                    bool finiteEdge=rotating || label.StartsWith("tilted ");
                    float penetration=finiteEdge?SlabPenetration(box,body.position,body.rotation,FlatSlab(track)):Mathf.Max(0,-clearance);
                    if(boxFixture)Debug.Log("VOLUME_SOLID_STEP label="+label+" step="+step+" depth="+penetration+" pose="+body.position+" rotation="+body.rotation.eulerAngles+" velocity="+body.linearVelocity+" angular="+body.angularVelocity+" contacts="+trace.contacts);
                    Check(penetration<=.04f,label+" penetrated actual road speed="+speed+" phase="+phase+" depth="+penetration+" plane="+clearance+" step="+step+" pose="+body.position+" rotation="+body.rotation.eulerAngles+" velocity="+body.linearVelocity+" angular="+body.angularVelocity+" contacts="+trace.contacts);
                    Check(finiteEdge || Vector3.Dot(body.position+body.rotation*box.center-point,outward)>=0,
                        label+" crossed to opposite side of impact face");
                }
                Check(minimum<.1f,label+" never reached physical surface min="+minimum+" point="+point.ToString("G9")+" outward="+outward.ToString("G9")+" pose="+body.position.ToString("G9")+" velocity="+body.linearVelocity.ToString("G9")+" contacts="+trace.contacts);
                if(!rotating && Vector3.Dot(body.linearVelocity,outward)<=-.2f && histories.TryGetValue(car.GetInstanceID(),out var finalHistory))foreach(var entry in finalHistory)Debug.Log(entry);
                Check(rotating || Vector3.Dot(body.linearVelocity,outward)>-.2f || (label.StartsWith("tilted ") && FiniteEscape(box,body.position,body.rotation,body.linearVelocity,FlatSlab(track))),label+" continued moving through contact speed="+speed+" phase="+phase+" min="+minimum+" finalSat="+SlabPenetration(box,body.position,body.rotation,FlatSlab(track))+" pose="+body.position.ToString("G9")+" rotation="+body.rotation.ToString("G9")+" boxCenter="+(body.position+body.rotation*box.center).ToString("G9")+" velocity="+body.linearVelocity+" contacts="+trace.contacts);
                Check(car.PositionRevision==revision && box.enabled,"respawn/disabled collider masked impact");impacts++;
            } finally {
                UnityEngine.Object.DestroyImmediate(obj);if(exactSlab!=null)UnityEngine.Object.DestroyImmediate(exactSlab);
                if(exactBottom!=null)UnityEngine.Object.DestroyImmediate(exactBottom);
                for(int i=0;i<roadColliders.Length;i++)roadColliders[i].enabled=roadEnabled[i];
            }
        }
        static void FlatImpacts(TrackBuilder track)
        {
            var f=track.Route.Evaluate(80);var start=track.Route.Evaluate(0);
            Vector3[] points={f.position,f.position-f.normal,f.position+f.right*8-f.normal*.5f,
                f.position-f.right*8-f.normal*.5f,start.position-start.normal*.5f};
            Vector3[] normals={f.normal,-f.normal,f.right,-f.right,-start.tangent};
            string[] labels={"top","bottom","right","left","start cap"};
            foreach(float speed in new[]{5f,42f,500f/3.6f,300f,1000f})
                foreach(float phase in new[]{.13f,.61f,.97f})
                    for(int side=0;side<points.Length;side++)
                        Impact(track,points[side],normals[side],f.Rotation,speed,phase,labels[side]);
            for(int side=0;side<points.Length;side++) {
                Impact(track,points[side],normals[side],f.Rotation,300,.61f,"falling "+labels[side],true);
                Impact(track,points[side],normals[side],f.Rotation*Quaternion.Euler(10,30,20),300,.61f,"tilted "+labels[side]);
                Impact(track,points[side],normals[side],f.Rotation,300,.61f,"rotating "+labels[side],false,true);
            }
            for(int side=0;side<points.Length;side++)
                foreach(float closing in new[]{5f,42f,139f})
                    Impact(track,points[side],normals[side],f.Rotation,closing,.61f,"grazing "+labels[side],false,true,false,null,
                        side==4?f.right*139:f.tangent*139);
            FallPairIsolation(track);
            // Cross-section/subdivision seams cannot contain internal walls.
            Check(!RenderedShellRaycast(track,new Ray(f.position-f.normal*.5f,f.tangent),40),"internal transverse wall");
        }
        static void FallPairIsolation(TrackBuilder track)
        {
            var first=new GameObject("fall pair owner");var second=new GameObject("fall pair peer");
            first.transform.SetParent(track.transform.parent);second.transform.SetParent(track.transform.parent);
            try {
                var balance=ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);
                var a=first.AddComponent<MagneticVehicle>();a.Initialize(track,0,Color.cyan,balance,false);
                var b=second.AddComponent<MagneticVehicle>();b.Initialize(track,1,Color.green,balance,false);
                var f=track.Route.Evaluate(80);a.Body.position=f.position+f.right*25;first.transform.position=a.Body.position;
                a.SetRaceContext(true,1,2,10);Physics.SyncTransforms();a.PrepareProjection();
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                typeof(MagneticVehicle).GetMethod("FixedUpdate",flags).Invoke(a,null);
                var ca=first.GetComponent<Collider>();var cb=second.GetComponent<Collider>();
                Check(a.IsFalling && ca.enabled && Physics.GetIgnoreCollision(ca,cb),"fall did not preserve road/ignore car pairs");
                b.Recovery.Observe(.01f,true,10,false,false,true,0,0,0);b.Recovery.Respawn();
                typeof(MagneticVehicle).GetMethod("BeginGhosting",flags).Invoke(b,null);
                b.Recovery.StepGhost(2);
                typeof(MagneticVehicle).GetMethod("RestoreCarCollisions",flags).Invoke(b,null);
                Check(Physics.GetIgnoreCollision(ca,cb),"peer ghost expiry removed fall pair protection");
                a.Recovery.Reset();typeof(MagneticVehicle).GetMethod("RestoreCarCollisions",flags).Invoke(a,null);
                Check(!Physics.GetIgnoreCollision(ca,cb),"pair protection leaked after fall ended");
            } finally {UnityEngine.Object.DestroyImmediate(first);UnityEngine.Object.DestroyImmediate(second);}
        }
        static void BranchAndJumpImpacts(TrackBuilder track)
        {
            var rails=new System.Collections.Generic.List<MeshCollider>();
            foreach(var collider in track.GetComponentsInChildren<MeshCollider>())
                if(collider.enabled && collider.sharedMesh!=null && collider.sharedMesh.name=="Procedural guard rails") {rails.Add(collider);collider.enabled=false;}
            // Test the slab itself: the taller chassis can otherwise hit a rail
            // 15 cm before its side, leaving the volume collision unexercised.
            try {BranchAndJumpVolumeImpacts(track);}
            finally {foreach(var collider in rails)collider.enabled=true;}
        }
        static void BranchAndJumpVolumeImpacts(TrackBuilder track)
        {
            var route=track.Route;
            foreach(var branch in route.Definition.branches) {
                float d=30+(branch.startIndex+branch.endIndex)*2.5f;
                var spans=Procedural.Layout.Paved(route.Definition,route.SourceAt(d));
                if(spans.Length<2)continue;var f=route.Evaluate(d);
                float edge=(float)spans[0].Right;
                Vector3 point=f.position-f.right*edge-f.normal*.5f;
                Impact(track,point,-f.right,f.Rotation,300,.61f,"inner fork side");
                float gap=((float)spans[0].Right+(float)spans[1].Left)*.5f;
                Vector3 empty=f.position-f.right*gap;
                Check(!Physics.Raycast(empty+f.normal*.2f,-f.normal,2),"fork void filled by shell");
                break;
            }
            foreach(var jump in route.Definition.jumps) {
                if(jump.kind!="mandatory" || route.Definition.guardrailMode=="full")continue;
                float d=30+jump.gapEndIndex*5;var f=route.Evaluate(d);
                Impact(track,f.position-f.normal*.5f,-f.tangent,f.Rotation,300,.61f,"landing cap");break;
            }
        }
        static void AngularAndImpulse(TrackBuilder track)
        {
            var f=track.Route.Evaluate(80);var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var first=new GameObject("road impulse receiver");var second=new GameObject("road impulse sender");
            try {
                var balance=ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);
                var a=first.AddComponent<MagneticVehicle>();a.Initialize(track,0,Color.cyan,balance,false);a.enabled=false;
                var b=second.AddComponent<MagneticVehicle>();b.Initialize(track,1,Color.green,balance,false);b.enabled=false;
                foreach(var car in new[]{a,b}) {ConfigureDiagnosticSolver(car);}
                foreach(var car in new[]{a,b})typeof(MagneticVehicle).GetMethod("RestoreCarCollisions",flags).Invoke(car,null);
                a.Body.position=f.position+f.normal*VehicleGeometry.AdheredBodyHeight;a.Body.rotation=f.Rotation;
                b.Body.position=a.Body.position+f.normal*(VehicleGeometry.ChassisSize.y+139*.01f*.61f);b.Body.rotation=f.Rotation;
                first.transform.SetPositionAndRotation(a.Body.position,a.Body.rotation);second.transform.SetPositionAndRotation(b.Body.position,b.Body.rotation);
                a.Body.constraints=b.Body.constraints=RigidbodyConstraints.FreezeRotation;
                Physics.SyncTransforms();Simulate(.01f);b.Body.linearVelocity=-f.normal*139;
                for(int i=0;i<16;i++) {
                    Simulate(.01f);
                    float clearance=Vector3.Dot(a.Body.position-f.position,f.normal)+VehicleGeometry.ChassisMinY;
                    Check(clearance>=-.04f,"car impulse pushed receiver into road clearance="+clearance);
                }
                Debug.Log("ROAD_VOLUME_IMPULSE_OK");
            } finally {UnityEngine.Object.DestroyImmediate(first);UnityEngine.Object.DestroyImmediate(second);}
            first=new GameObject("angular road contact");
            try {
                var car=first.AddComponent<MagneticVehicle>();car.Initialize(track,0,Color.cyan,ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text),false);car.enabled=false;
                ConfigureDiagnosticSolver(car);car.Body.position=f.position+f.normal*VehicleGeometry.AdheredBodyHeight;car.Body.rotation=f.Rotation;
                first.transform.SetPositionAndRotation(car.Body.position,car.Body.rotation);Physics.SyncTransforms();Simulate(.01f);
                car.Body.linearVelocity=Vector3.zero;car.Body.angularVelocity=f.tangent*6;
                var box=first.GetComponent<BoxCollider>();
                for(int i=0;i<16;i++) {
                    Simulate(.01f);Vector3 axis=Quaternion.Inverse(car.Body.rotation)*f.normal;
                    float radius=Vector3.Dot(new Vector3(Mathf.Abs(axis.x),Mathf.Abs(axis.y),Mathf.Abs(axis.z)),box.size)*.5f;
                    float gap=Vector3.Dot(car.Body.position+car.Body.rotation*box.center-f.position,f.normal)-radius;
                    Check(gap>=-.04f,"angular-only road penetration="+gap);
                }
                Debug.Log("ROAD_VOLUME_ANGULAR_ONLY_OK");
            } finally {UnityEngine.Object.DestroyImmediate(first);}
        }
        static void AngularImpulseMatrix(TrackBuilder track,float alongShift=0,float capLateral=0)
        {
            var f=track.Route.Evaluate(80+alongShift);var begin=track.Route.Evaluate(0);var end=track.Route.Samples[track.Route.Samples.Length-1];
            Vector3[] points={f.position,f.position-f.normal,f.position+f.right*8-f.normal*.5f,f.position-f.right*8-f.normal*.5f,begin.position-begin.normal*.5f+begin.right*capLateral,end.position-end.normal*.5f+end.right*capLateral};
            Vector3[] normals={f.normal,-f.normal,f.right,-f.right,-begin.tangent,end.tangent};
            var balance=ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);int cases=0,failed=0;
            bool sentinels=Environment.GetEnvironmentVariable("STAR_RACING_ROAD_SENTINELS")=="1";
            for(int face=0;face<points.Length;face++)foreach(var rotation in new[]{f.Rotation,f.Rotation*Quaternion.Euler(10,30,20),f.Rotation*Quaternion.Euler(-25,-17,31)}) {
                foreach(var axis in new[]{Vector3.right,Vector3.up,Vector3.forward}) {
                    if(sentinels && !(face==2 && axis==Vector3.up && Quaternion.Angle(rotation,f.Rotation*Quaternion.Euler(-25,-17,31))<.01f))continue;
                    var obj=new GameObject("angular face="+face+" axis="+axis+" orientation="+rotation.eulerAngles);obj.transform.SetParent(track.transform.parent);
                    try {
                        var car=obj.AddComponent<MagneticVehicle>();car.Initialize(track,0,Color.cyan,balance,false);car.enabled=false;ConfigureDiagnosticSolver(car);
                        if(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_REFERENCE")=="1")obj.AddComponent<RoadImpactContactTrace>();
                        var box=obj.GetComponent<BoxCollider>();Vector3 outward=normals[face],local=Quaternion.Inverse(rotation)*outward;
                        float radius=Vector3.Dot(new Vector3(Mathf.Abs(local.x),Mathf.Abs(local.y),Mathf.Abs(local.z)),box.size)*.5f;
                        car.Body.position=points[face]+outward*(radius+.015f)-rotation*box.center;car.Body.rotation=rotation;
                        obj.transform.SetPositionAndRotation(car.Body.position,rotation);Physics.SyncTransforms();
                        Check(SlabPenetration(box,car.Body.position,rotation,FlatSlab(track))==0 && car.Body.linearVelocity.sqrMagnitude<1e-10f,"angular fixture initial overlap/motion");Simulate(.01f);
                        car.Body.angularVelocity=rotation*axis*6;int revision=car.PositionRevision;
                        for(int step=0;step<24;step++)Simulate(.01f);
                        Check(box.enabled && car.PositionRevision==revision,"angular matrix was masked by recovery");cases++;
                    }catch(Exception e){failed++;Debug.Log("ROAD_VOLUME_EXTENDED_FAILED "+e.Message);if(sentinels)throw;}finally {UnityEngine.Object.DestroyImmediate(obj);}
                }
                foreach(float speed in new[]{42f,139f,300f}) {
                    if(sentinels && !((face==2 && speed==139 && Quaternion.Angle(rotation,f.Rotation*Quaternion.Euler(-25,-17,31))<.01f) || (face==4 && speed==300 && Quaternion.Angle(rotation,f.Rotation*Quaternion.Euler(10,30,20))<.01f)))continue;
                    var receiver=new GameObject("impulse face="+face+" speed="+speed+" orientation="+rotation.eulerAngles);var donor=new GameObject("impulse donor");receiver.transform.SetParent(track.transform.parent);donor.transform.SetParent(track.transform.parent);
                    try {
                        var a=receiver.AddComponent<MagneticVehicle>();a.Initialize(track,0,Color.cyan,balance,false);a.enabled=false;ConfigureDiagnosticSolver(a);
                        var b=donor.AddComponent<MagneticVehicle>();b.Initialize(track,1,Color.green,balance,false);b.enabled=false;ConfigureDiagnosticSolver(b);
                        if(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_REFERENCE")=="1"){receiver.AddComponent<RoadImpactContactTrace>();donor.AddComponent<RoadImpactContactTrace>();}
                        var box=a.GetComponent<BoxCollider>();Vector3 outward=normals[face],local=Quaternion.Inverse(rotation)*outward;
                        float radius=Vector3.Dot(new Vector3(Mathf.Abs(local.x),Mathf.Abs(local.y),Mathf.Abs(local.z)),box.size)*.5f;
                        a.Body.position=points[face]+outward*(radius+.015f)-rotation*box.center;a.Body.rotation=rotation;
                        b.Body.position=a.Body.position+outward*(radius*2+.06f+speed*.01f*.61f);b.Body.rotation=rotation;
                        receiver.transform.SetPositionAndRotation(a.Body.position,rotation);donor.transform.SetPositionAndRotation(b.Body.position,rotation);
                        Physics.IgnoreCollision(box,donor.GetComponent<BoxCollider>(),false);Physics.SyncTransforms();
                        Check(SlabPenetration(box,a.Body.position,rotation,FlatSlab(track))==0 && SlabPenetration(donor.GetComponent<BoxCollider>(),b.Body.position,rotation,FlatSlab(track))==0,"impulse fixture initial overlap");Simulate(.01f);
                        b.Body.linearVelocity=-outward*speed;int revision=a.PositionRevision;
                        for(int step=0;step<24;step++)Simulate(.01f);
                        Check(box.enabled && a.PositionRevision==revision,"impulse matrix was masked by recovery");cases++;
                    }catch(Exception e){failed++;Debug.Log("ROAD_VOLUME_EXTENDED_FAILED "+e.Message);if(sentinels)throw;}finally {UnityEngine.Object.DestroyImmediate(receiver);UnityEngine.Object.DestroyImmediate(donor);}
                }
            }
            Debug.Log("ROAD_VOLUME_ANGULAR_IMPULSE_MATRIX_RESULT passed="+cases+" failed="+failed+" faces=6 orientations=3 alongShift="+alongShift+" capLateral="+capLateral+" maxPenetration="+maxObservedPenetration);
            Check(failed==0,"angular/impulse matrix failed cases="+failed);
        }
        public static void PlayModeFixture(TrackBuilder track)
        {
            Check(Application.isPlaying,"not actually in Play Mode");
            var mode=Physics.simulationMode;
            try { Physics.simulationMode=SimulationMode.Script;Geometry(track);BranchAndJumpImpacts(track); }
            finally { Physics.simulationMode=mode; }
            Debug.Log("ROAD_VOLUME_PLAYMODE_OK");
        }
        public static void Profile64()
        {
            foreach(var detection in new[]{CollisionDetectionMode.ContinuousDynamic,CollisionDetectionMode.ContinuousSpeculative})
                Profile64Case(detection,FlatRoad(),"flat");
        }
        static Procedural.Definition CostRoad()
        {
            var definition=FlatRoad();var samples=new Procedural.Sample[201];
            for(int i=0;i<samples.Length;i++)samples[i]=new Procedural.Sample {index=i,distance=i*5,progress=i/200.0,halfWidth=8,position=new Procedural.DVec(1000,120,i*5),tangent=new Procedural.DVec(0,0,1),normal=new Procedural.DVec(0,1,0),right=new Procedural.DVec(-1,0,0),kind="straight"};
            definition.samples=samples;definition.totalLength=1000;return definition;
        }
        public static void ProfileCandidate64()=>Profile64Case(CollisionDetectionMode.ContinuousDynamic,CostRoad(),"candidate-flat-steady64");
        public static void ProfileProduction64()
        {
            Profile64Case(CollisionDetectionMode.ContinuousDynamic,CostRoad(),"flat-steady64");
            foreach(string theme in new[]{"cloud-city","space-station"})
                Profile64Case(CollisionDetectionMode.ContinuousDynamic,Procedural.Generator.Generate(77,"full",theme,true,true),theme+"/full");
        }
        static void Profile64Case(CollisionDetectionMode detection,Procedural.Definition definition,string label)
        {
            var mode=Physics.simulationMode;float dt=Time.fixedDeltaTime;
            var root=new GameObject("64 car CCD cost fixture");var meshes=new System.Collections.Generic.List<Mesh>();
            try {
                Physics.simulationMode=SimulationMode.Script;Time.fixedDeltaTime=.01f;
                var trackObject=new GameObject("CCD cost track");trackObject.transform.SetParent(root.transform);
                var track=trackObject.AddComponent<TrackBuilder>();track.Build(new TrackRoute(definition));
                if(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_THIN_SUPPORT_CONTROL")=="1")ThinSupportControl(track,meshes);
                var balance=ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);
                var cars=new MagneticVehicle[64];var revisions=new int[64];
                var tick=typeof(MagneticVehicle).GetMethod("FixedUpdate",BindingFlags.Instance|BindingFlags.NonPublic);
                for(int i=0;i<cars.Length;i++) {
                    var obj=new GameObject("CCD cost car "+i);obj.transform.SetParent(root.transform);
                    cars[i]=obj.AddComponent<MagneticVehicle>();cars[i].Initialize(track,i,Color.cyan,balance,false);cars[i].enabled=false;cars[i].Body.collisionDetectionMode=detection;
                    cars[i].ResetAt(30+(i/2)*6,i%2==0?-3:3);cars[i].Body.linearVelocity=cars[i].Frame.tangent*42;
                    cars[i].SetInput(new DrivingInput{throttle=1,nitro=true});revisions[i]=cars[i].PositionRevision;
                }
                bool generated=!label.Contains("flat");var world=new AiWorldSnapshot(64);var drivers=new AiDriver[64];
                var timers=new float[64];var lastDecision=new double[64];var commands=new DrivingInput[64];
                for(int i=0;i<64;i++){if(generated)drivers[i]=new AiDriver(i,DriverProfile.Racer,(uint)(77+i*31),balance);timers[i]=.1f*i/64;lastDecision[i]=9.9;}
                Physics.SyncTransforms();var total=new double[180];var physics=new double[180];
                for(int step=0;step<200;step++) {
                    var watch=System.Diagnostics.Stopwatch.StartNew();double elapsed=10+step*.01;
                    foreach(var car in cars)car.PrepareProjection();
                    if(generated) {
                        world.Capture(cars,track.Route,elapsed);
                        for(int i=0;i<64;i++){timers[i]+=.01f;if(timers[i]>=.1f){commands[i]=drivers[i].Step(world,track.Route,(float)(elapsed-lastDecision[i]));timers[i]=0;lastDecision[i]=elapsed;}cars[i].SetInput(commands[i]);}
                    }
                    foreach(var car in cars) {car.SetRaceContext(true,1,64,elapsed);tick.Invoke(car,null);}
                    double force=watch.Elapsed.TotalMilliseconds;Simulate(.01f);
                    if(step>=20) {total[step-20]=watch.Elapsed.TotalMilliseconds;physics[step-20]=total[step-20]-force;}
                }
                foreach(var car in cars)if(car.IsFalling || car.PositionRevision!=revisions[car.Seat])Debug.Log("ROAD_VOLUME_CCD64_FAILED_CAR seat="+car.Seat+" fall="+car.IsFalling+" revision="+car.PositionRevision+" expected="+revisions[car.Seat]+" distance="+car.Distance+" lateral="+Vector3.Dot(car.Body.position-car.Frame.position,car.Frame.right)+" height="+Vector3.Dot(car.Body.position-car.Frame.position,car.Frame.normal)+" velocity="+car.Body.linearVelocity.ToString("G9"));
                Array.Sort(total);Array.Sort(physics);
                double sum=0;foreach(double value in total)sum+=value;
                Debug.Log("ROAD_VOLUME_CCD64_METRICS fixture="+label+" mode="+detection+" substeps="+(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_SUBSTEPS")??RacePhysicsStepper.Substeps.ToString())+" samples=180 ai10Hz="+generated+" physicsP50Ms="+physics[90].ToString("F3")+" physicsP99Ms="+physics[178].ToString("F3")+" physicsP95Ms="+physics[170].ToString("F3")+" totalP95Ms="+total[170].ToString("F3")+" totalMeanMs="+(sum/total.Length).ToString("F3")+" physicsMaxMs="+physics[179].ToString("F3")+" noResets="+Array.TrueForAll(cars,car=>!car.IsFalling && car.PositionRevision==revisions[car.Seat])+" editorOnly=true");
                foreach(var car in cars)Check(!car.IsFalling && car.PositionRevision==revisions[car.Seat],"64 car CCD fixture fell/reset");
                Check(sum<total.Length*10,"64-car production tick accumulated backlog fixture="+label);
            } finally {UnityEngine.Object.DestroyImmediate(root);foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);Physics.simulationMode=mode;Time.fixedDeltaTime=dt;}
        }
        // Diagnostic only: preserve each actual exterior triangle while testing
        // native convex contact manifolds, without changing production geometry.
        public static void DiagnoseFacetWalls()
        {
            ValidateFlatClosure();ValidateSlabOracle();maxObservedPenetration=0;maxFrozenSegmentPenetration=0;histories.Clear();
            var mode=Physics.simulationMode;Physics.simulationMode=SimulationMode.Script;
            var root=new GameObject("convex facet control");var meshes=new System.Collections.Generic.List<Mesh>();
            try {
                var track=root.AddComponent<TrackBuilder>();track.Build(new TrackRoute(FlatRoad()));
                if(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_SUBSTEPS")!=null)substepTrack=track;
                var shell=Shell(track);var visual=ShellVisual(track);var points=visual.vertices;var indices=visual.triangles;
                int bottomCount=track.GetComponentInChildren<TrackSurface>().GetComponent<MeshCollider>().sharedMesh.triangles.Length;
                var bottom=new Mesh {name=RoadVolumeMesh.MeshName,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};
                meshes.Add(bottom);bottom.vertices=points;bottom.SetTriangles(indices,0,bottomCount,0);bottom.RecalculateBounds();shell.sharedMesh=bottom;
                if(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_PLANAR_BOX")=="1") {
                    var bounds=FlatSlab(track);
                    foreach(bool alongX in new[]{true,false})foreach(bool positive in new[]{true,false}) {
                        var obj=new GameObject("planar convex control wall");obj.transform.SetParent(root.transform);
                        var box=obj.AddComponent<BoxCollider>();var center=bounds.center;var size=bounds.size;
                        float depth=float.Parse(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_WALL_DEPTH")??"0.02",System.Globalization.CultureInfo.InvariantCulture);
                        if(alongX) {center.x=(positive?bounds.max.x:bounds.min.x)+(positive?-depth*.5f:depth*.5f);size.x=depth;}
                        else {center.z=(positive?bounds.max.z:bounds.min.z)+(positive?-depth*.5f:depth*.5f);size.z=depth;}
                        box.center=center;box.size=size;box.sharedMaterial=shell.sharedMaterial;
                        Check(bounds.Contains(center-size*.5f) && bounds.Contains(center+size*.5f),"planar band escaped authored slab");
                        Check(box.sharedMaterial==shell.sharedMaterial && box.enabled && !box.isTrigger,"planar band collider contract");
                    }
                } else
                for(int i=bottomCount;i<indices.Length;i+=3) {
                    Vector3 a=points[indices[i]],b=points[indices[i+1]],c=points[indices[i+2]];
                    Vector3 normal=Vector3.Cross(b-a,c-a).normalized;
                    Vector3 inset=-Vector3.ProjectOnPlane(normal,Vector3.up).normalized*.02f;
                    var mesh=new Mesh {name="convex control facet"};meshes.Add(mesh);
                    if(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_WALL_CONE")=="1") {
                        var min=Vector3.Min(a,Vector3.Min(b,c));var max=Vector3.Max(a,Vector3.Max(b,c));
                        Vector3 apex=(min+max)*.5f;apex.y=FlatSlab(track).center.y;
                        apex-=Vector3.ProjectOnPlane(normal,Vector3.up).normalized*(Mathf.Abs(normal.x)>.5f?.5f:.25f);
                        var occupied=FlatSlab(track);
                        Check(occupied.Contains(apex) && occupied.Contains(a) && occupied.Contains(b) && occupied.Contains(c),"flat wedge escaped exact slab a="+a.ToString("F6")+" b="+b.ToString("F6")+" c="+c.ToString("F6")+" apex="+apex.ToString("F6")+" bounds="+occupied+" normal="+normal);
                        Check(Vector3.Dot(normal,apex-a)<-1e-6f,"flat wedge apex not inward");
                        mesh.vertices=new[]{a,b,c,apex};mesh.triangles=new[]{0,1,2,0,3,1,1,3,2,2,3,0};
                    } else {
                        mesh.vertices=new[]{a,b,c,a+inset,b+inset,c+inset};
                        mesh.triangles=new[]{0,1,2,3,5,4,0,3,1,1,3,4,1,4,2,2,4,5,2,5,0,0,5,3};
                    }
                    mesh.RecalculateBounds();
                    var obj=new GameObject("convex control facet");obj.transform.SetParent(root.transform);
                    var collider=obj.AddComponent<MeshCollider>();collider.sharedMesh=mesh;collider.convex=true;collider.sharedMaterial=shell.sharedMaterial;
                }
                Physics.SyncTransforms();NativeImpactMatrix(track);
                Debug.Log("ROAD_VOLUME_CONVEX_CONTROL_OK representation="+(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_PLANAR_BOX")=="1"?"merged-box":"facets")+" colliderCount="+root.GetComponentsInChildren<Collider>().Length);
            } finally {substepTrack=null;UnityEngine.Object.DestroyImmediate(root);foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);Physics.simulationMode=mode;}
        }
        public static void DiagnoseBox()
        {
            var mode=Physics.simulationMode;Physics.simulationMode=SimulationMode.Script;
            var root=new GameObject("box comparison road");
            try {var track=root.AddComponent<TrackBuilder>();track.Build(new TrackRoute(FlatRoad()));var f=track.Route.Evaluate(80);
                foreach(bool solid in new[]{false,true}) {
                    foreach(bool tilt in new[]{false,true}) {
                        Vector3 outward=tilt?-f.right:f.right,point=f.position+outward*8-f.normal*.5f;
                        try {Impact(track,point,outward,tilt?f.Rotation*Quaternion.Euler(10,30,20):f.Rotation,300,.61f,
                                tilt?"tilted left":"rotating right",false,!tilt,solid,CollisionDetectionMode.ContinuousSpeculative);
                            Debug.Log("VOLUME_COMPARISON_OK solid="+solid+" tilt="+tilt);
                        } catch(Exception e) {Debug.Log("VOLUME_COMPARISON_FAILED solid="+solid+" tilt="+tilt+" detail="+e.Message);}
                    }
                }
            } finally {UnityEngine.Object.DestroyImmediate(root);Physics.simulationMode=mode;}
        }
        public static void Run()
        {
            ValidateFlatClosure();ValidateSlabOracle();impacts=probes=0;var mode=Physics.simulationMode;float dt=Time.fixedDeltaTime;
            try {
                Physics.simulationMode=SimulationMode.Script;Time.fixedDeltaTime=.01f;
                var root=new GameObject("flat road volume fixture");
                try { var track=root.AddComponent<TrackBuilder>();track.Build(new TrackRoute(FlatRoad()));Geometry(track);substepTrack=track;FlatImpacts(track);AngularAndImpulse(track);AngularImpulseMatrix(track); }
                finally { substepTrack=null;UnityEngine.Object.DestroyImmediate(root); }
                RunGenerated();
            } finally { Physics.simulationMode=mode;Time.fixedDeltaTime=dt; }
            Debug.Log("ROAD_VOLUME_CHECKS_OK impacts="+impacts+" probes="+probes+" maxImpactSpeed=1000m/s noRespawnMasking=true");
        }
        public static void RunGenerated()
        {
            var mode=Physics.simulationMode;Physics.simulationMode=SimulationMode.Script;
            try {
                string filter=Environment.GetEnvironmentVariable("STAR_RACING_ROAD_GENERATED_FIXTURE");
                foreach(string theme in new[]{"cloud-city","space-station"})
                    foreach(string modeName in new[]{"none","normal","full"}) {
                        if(!string.IsNullOrEmpty(filter) && filter!=theme+"/"+modeName)continue;
                        var root=new GameObject("generated road volume fixture");
                        try {
                            var track=root.AddComponent<TrackBuilder>();
                            var watch=System.Diagnostics.Stopwatch.StartNew();
                            track.Build(new TrackRoute(Procedural.Generator.Generate(77,modeName,theme,true,true)));Geometry(track);BranchAndJumpImpacts(track);
                            Debug.Log("ROAD_VOLUME_GEOMETRY_OK theme="+theme+" mode="+modeName+" triangles="+Shell(track).sharedMesh.triangles.Length/3+" seconds="+watch.Elapsed.TotalSeconds);
                        } finally { UnityEngine.Object.DestroyImmediate(root); }
                    }
            } finally { Physics.simulationMode=mode; }
            Debug.Log("ROAD_VOLUME_GENERATED_CHECKS_OK");
        }
    }
}

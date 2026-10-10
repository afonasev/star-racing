using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;

namespace StarRacingPrototype
{
    public static class RoadSmoothnessChecks
    {
        static readonly MethodInfo Tick = typeof(MagneticVehicle).GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
        static void Check(bool valid, string message) { if (!valid) throw new Exception("Road smoothness: " + message); }
        static void CheckGeometry(TrackBuilder track)
        {
            var route = track.Route;
            foreach (var node in route.Samples) {
                if (node.distance <= 0 || node.distance >= route.Length) continue;
                var before = route.Evaluate(node.distance - .001f);
                var after = route.Evaluate(node.distance + .001f);
                Check(Vector3.Distance(before.position, after.position) < .003f, "position seam");
                Check(Vector3.Angle(before.tangent, after.tangent) < .05f, "tangent seam");
                Check(Vector3.Angle(before.normal, after.normal) < .05f, "normal seam");
            }
            float peakError = 0, peakNormalStep = 0;
            for (float d = route.LoopStart; d < route.LoopEnd - 5; d += .25f) {
                var frame = route.Evaluate(d);
                Check(track.RaycastRoad(frame.position + frame.normal, -frame.normal, 2, d, out var hit), "loop missing collider");
                peakError = Mathf.Max(peakError, Vector3.Distance(hit.point, frame.position));
                var next = route.Evaluate(d + .01f);
                Check(track.RaycastRoad(next.position + next.normal, -next.normal, 2, d + .01f, out var nextHit), "seam missing collider");
                peakNormalStep = Mathf.Max(peakNormalStep, Vector3.Angle(hit.normal, nextHit.normal));
                var projected = route.Project(frame.position, d);
                Check(Mathf.Abs(projected.distance - d) < .01f, "projection disagrees with curve");
            }
            // Check physical normals off-center, including roll transitions. A shared
            // centerline normal would pass the center test but fail this surface derivative.
            float edgeError = 0;
            for (int i=16;i<route.Samples.Length-81;i+=3) {
                float d=route.Samples[i].distance+.25f;
                if(route.IsJumpRegion(d))continue;
                var frame=route.Evaluate(d);var before=route.Evaluate(d-.1f);var after=route.Evaluate(d+.1f);
                foreach(float lateral in new[]{-4f,4f}) {
                    Vector3 point=frame.position-frame.right*lateral;
                    if(!track.RaycastRoad(point+frame.normal,-frame.normal,2,d,out var hit))continue;
                    Vector3 along=(after.position-after.right*lateral)-(before.position-before.right*lateral);
                    Vector3 expected=Vector3.Cross(along,frame.right).normalized;
                    edgeError=Mathf.Max(edgeError,Vector3.Angle(expected,hit.normal));
                }
            }
            Check(edgeError<1f,"off-center surface normal " + edgeError);
            Check(peakError < .005f, "collider sagitta " + peakError);
            Check(peakNormalStep < .1f, "normal step " + peakNormalStep);
            foreach (var surface in track.GetComponentsInChildren<TrackSurface>()) {
                Check(surface.GetComponent<MeshFilter>().sharedMesh == surface.GetComponent<MeshCollider>().sharedMesh,
                    "render/collider mismatch");
                foreach (var face in surface.triangleFaces)
                    Check(face.endDistance - face.startDistance <= .501f, "coarse physical panel");
            }
            Debug.Log($"ROAD_GEOMETRY_OK theme={route.Definition.theme} error={peakError:F5} normalStep={peakNormalStep:F4} edgeNormalError={edgeError:F4}");
        }
        static Procedural.Definition IsolatedRoad(bool loop, bool crest)
        {
            var samples = new Procedural.Sample[101];
            // A convex full circle puts its exit straight through the starting car.
            // Use a half-circle descent with a correctly oriented exit instead.
            double radius = 420 / (Math.PI * (crest ? 1 : 2)), sign = crest ? -1 : 1;
            for (int i = 0; i < samples.Length; i++) {
                double d = i * 5, angle = Math.Min(d, 420) / radius;
                double y = loop ? 120 + sign * radius * (1 - Math.Cos(angle)) : 120;
                double z = loop ? radius * Math.Sin(angle) + Math.Cos(angle) * Math.Max(0, d - 420) : d;
                var tangent = loop ? new Procedural.DVec(0, sign * Math.Sin(angle), Math.Cos(angle)) : new Procedural.DVec(0,0,1);
                var normal = loop ? new Procedural.DVec(0, Math.Cos(angle), -sign * Math.Sin(angle)) : new Procedural.DVec(0,1,0);
                samples[i] = new Procedural.Sample { index=i, distance=d, progress=d/500, halfWidth=8,
                    position=new Procedural.DVec(loop ? 0 : 1000,y,z), tangent=tangent,normal=normal,
                    right=new Procedural.DVec(-1,0,0),kind=loop && d<420 ? "loop" : "straight" };
            }
            return new Procedural.Definition { theme="cloud-city",guardrailMode="none",samples=samples,totalLength=500,
                branches=Array.Empty<Procedural.Branch>(),jumps=Array.Empty<Procedural.Jump>(),
                patterns=Array.Empty<Procedural.Pattern>(),checkpoints=Array.Empty<Procedural.Checkpoint>() };
        }
        static void CheckCoast(bool powered, bool crest = false, bool boost = false)
        {
            var roots = new GameObject[2]; var cars = new MagneticVehicle[2]; var revisions = new int[2];
            var balance = powered ? ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text) : null;
            try {
                for (int i=0;i<2;i++) {
                    roots[i]=new GameObject("isolated road coast");
                    var trackObject=new GameObject("track");trackObject.transform.SetParent(roots[i].transform);
                    var track=trackObject.AddComponent<TrackBuilder>();track.Build(new TrackRoute(IsolatedRoad(i==1,crest)));
                    var carObject=new GameObject("coast car");carObject.transform.SetParent(roots[i].transform);
                    cars[i]=carObject.AddComponent<MagneticVehicle>();cars[i].Initialize(track,0,Color.cyan,balance,powered);
                    cars[i].ResetAt(40);cars[i].Body.linearVelocity=cars[i].Frame.tangent*(powered?(boost?cars[i].Drive.NitroSpeed:cars[i].Drive.BaseSpeed):42);
                    cars[i].SetInput(new DrivingInput{throttle=powered?1:0,nitro=boost}); revisions[i]=cars[i].PositionRevision;
                }
                Physics.SyncTransforms();
                for(int i=0;i<2;i++)
                    Check(CheckpointAdmission.CheckPose(roots[i].GetComponentInChildren<TrackBuilder>(),cars[i].Body.position,
                        cars[i].Body.rotation,cars[i].Distance,VehicleGeometry.AdheredBodyHeight).accepted,"invalid/overlapping isolated fixture");
                float worstLoss=0,peakHeight=0,minHeight=100,maxPitchError=0,minClearance=100,poseGap=0;int unsupported=0;float firstLoss=-1,firstLossDistance=-1;
                int ticks=0;
                for (;ticks<2000 && cars[1].Distance<450;ticks++) {
                    foreach(var car in cars){car.SetRaceContext(powered,1,2,ticks*.01);car.PrepareProjection();Tick.Invoke(car,null);}
                    RacePhysicsStepper.Simulate(.01f);
                    float straight=Vector3.ProjectOnPlane(cars[0].Body.linearVelocity,cars[0].Frame.normal).magnitude;
                    float loop=Vector3.ProjectOnPlane(cars[1].Body.linearVelocity,cars[1].Frame.normal).magnitude;
                    worstLoss=Mathf.Max(worstLoss,Mathf.Abs(straight-loop)/straight);
                    peakHeight=Mathf.Max(peakHeight,Vector3.Dot(cars[1].Body.position-cars[1].Frame.position,cars[1].Frame.normal));
                    minHeight=Mathf.Min(minHeight,Vector3.Dot(cars[1].Body.position-cars[1].Frame.position,cars[1].Frame.normal));
                    poseGap=Mathf.Max(poseGap,Vector3.Distance(cars[1].Body.position,cars[1].transform.position));
                    maxPitchError=Mathf.Max(maxPitchError,Vector3.Angle(cars[1].Body.rotation*Vector3.up,cars[1].Frame.normal));
                    var road=roots[1].GetComponentInChildren<TrackBuilder>();
                    for(int corner=-1;corner<=1;corner+=2) {
                        Vector3 bottom=cars[1].Body.position+cars[1].Body.rotation*new Vector3(0,VehicleGeometry.ChassisMinY,corner*VehicleGeometry.Length*.5f);
                        if(road.RaycastRoad(bottom+cars[1].Frame.normal*2,-cars[1].Frame.normal,4,cars[1].Distance,out var hit))
                            minClearance=Mathf.Min(minClearance,Vector3.Dot(bottom-hit.point,cars[1].Frame.normal));
                    }
                    if(cars[1].HandlingSupportCount==0){unsupported++;if(firstLoss<0){firstLoss=ticks*.01f;firstLossDistance=cars[1].Distance;}}
                }
                Debug.Log($"ROAD_COAST_METRICS powered={powered} crest={crest} boost={boost} straight={cars[0].Body.linearVelocity.magnitude:F3} loop={cars[1].Body.linearVelocity.magnitude:F3} worstLoss={worstLoss:F4} height={minHeight:F3}..{peakHeight:F3} pitchError={maxPitchError:F2} poseGap={poseGap:F4} clearance={minClearance:F3} unsupported={unsupported} firstLoss={firstLoss:F3}@{firstLossDistance:F2} distance={cars[1].Distance:F2} seconds={ticks*.01f:F2}");
                Check(cars[1].Distance>=450,"isolated loop did not complete");
                Check(!cars[1].IsFalling && cars[1].PositionRevision==revisions[1],"isolated loop recovered/reset");
                Check(worstLoss<=.03f,"bend-only speed loss " + worstLoss);
                Check(unsupported==0 && peakHeight<.65f,"isolated loop bounced/detached");

            }
            finally { foreach(var root in roots)if(root!=null) {
                var meshes=new System.Collections.Generic.HashSet<Mesh>();
                foreach(var collider in root.GetComponentsInChildren<MeshCollider>())if(collider.sharedMesh!=null)meshes.Add(collider.sharedMesh);
                UnityEngine.Object.DestroyImmediate(root);foreach(var mesh in meshes)if(mesh!=null)UnityEngine.Object.DestroyImmediate(mesh);
            } }
        }
        static void CheckMaterialLifecycle(TrackBuilder track)
        {
            var material=track.GetComponentInChildren<TrackSurface>().GetComponent<MeshCollider>().sharedMaterial;
            foreach(var surface in track.GetComponentsInChildren<TrackSurface>())
                Check(surface.GetComponent<MeshCollider>().sharedMaterial==material && material.dynamicFriction==0 && material.staticFriction==0,
                    "road contact resistance");
                foreach(var collider in track.GetComponentsInChildren<MeshCollider>())
                if(collider.GetComponent<TrackSurface>()==null) {
                    bool volume=collider.sharedMesh!=null && (collider.sharedMesh.name==RoadVolumeMesh.BottomName || collider.sharedMesh.name==RoadVolumeMesh.ExteriorName || collider.sharedMesh.name==RoadVolumeMesh.SupportName);
                    Check(volume?collider.sharedMaterial==material:collider.sharedMaterial!=material,volume?"volume contact friction changed":"road material applied to guard rails");
                }
            track.Build(track.Route);
            Check(material==null,"road contact material leaked across rebuild");
            Check(track.GetComponentInChildren<TrackSurface>().GetComponent<MeshCollider>().sharedMaterial!=null,"rebuilt material missing");
        }
        static void CheckBraking()
        {
            var root=new GameObject("road braking checks");
            try {
                var track=root.AddComponent<TrackBuilder>();track.Build(new TrackRoute(IsolatedRoad(false,false)));
                var carObject=new GameObject("braking car");carObject.transform.SetParent(root.transform);
                var balance=ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);
                var car=carObject.AddComponent<MagneticVehicle>();car.Initialize(track,0,Color.cyan,balance,true);
                car.ResetAt(40);car.Body.linearVelocity=car.Frame.tangent*car.Drive.BaseSpeed;
                car.SetInput(new DrivingInput{throttle=1,brake=1});Physics.SyncTransforms();
                for(int i=0;i<100;i++){car.SetRaceContext(true,1,2,i*.01);car.PrepareProjection();Tick.Invoke(car,null);RacePhysicsStepper.Simulate(.01f);}
                float speed=Vector3.Dot(car.Body.linearVelocity,car.Frame.tangent);
                Check(speed<car.Drive.BaseSpeed-20 && !car.IsFalling,"controlled braking lost authority");
                Debug.Log($"ROAD_BRAKING_OK initial={car.Drive.BaseSpeed:F3} final={speed:F3}");
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        static void CheckWidthTransitions()
        {
            var root=new GameObject("road width checks");
            try {
                var definition=IsolatedRoad(false,false);
                foreach(var sample in definition.samples)sample.halfWidth=6+2*Math.Sin(sample.distance*Math.PI/125);
                var track=root.AddComponent<TrackBuilder>();track.Build(new TrackRoute(definition));
                var carObject=new GameObject("width transition car");carObject.transform.SetParent(root.transform);
                var car=carObject.AddComponent<MagneticVehicle>();
                car.Initialize(track,0,Color.cyan,ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text),true);
                car.ResetAt(40,1.5f);car.Body.linearVelocity=car.Frame.tangent*car.Drive.BaseSpeed;
                car.SetInput(new DrivingInput{throttle=1});Physics.SyncTransforms();int revision=car.PositionRevision;
                float minSpeed=car.Drive.BaseSpeed;int unsupported=0,ticks=0;
                for(;ticks<600 && car.Distance<450;ticks++) {
                    car.SetRaceContext(true,1,2,ticks*.01);car.PrepareProjection();Tick.Invoke(car,null);RacePhysicsStepper.Simulate(.01f);
                    minSpeed=Mathf.Min(minSpeed,Vector3.Dot(car.Body.linearVelocity,car.Frame.tangent));
                    if(car.HandlingSupportCount==0)unsupported++;
                }
                Check(car.Distance>=450 && car.PositionRevision==revision && !car.IsFalling,"width transition failed");
                Check(minSpeed>=car.Drive.BaseSpeed*.97f && unsupported==0,"width transition snag/detach");
                Debug.Log($"ROAD_WIDTH_TRANSITIONS_OK speed={minSpeed:F3} unsupported={unsupported}");
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        public static void Run()
        {
            Check(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STAR_RACING_COAST_CONTROL")),"diagnostic coast control cannot certify production handling");
            var mode = Physics.simulationMode; float dt = Time.fixedDeltaTime;
            try
            {
                Physics.simulationMode = SimulationMode.Script; Time.fixedDeltaTime = .01f;
                CheckCoast(true,false,true); CheckCoast(true,true,true);
                CheckCoast(false); CheckCoast(true); CheckCoast(false,true); CheckCoast(true,true);
                CheckBraking();
                CheckWidthTransitions();
                var balance = ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);
                foreach (string theme in new[] { "cloud-city", "space-station" })
                {
                    CheckShippingLoop(theme,balance);
                }
            }
            finally { Physics.simulationMode = mode; Time.fixedDeltaTime = dt; }
        }
        static void CheckShippingLoop(string theme,ReleaseBalance balance,int substeps=RacePhysicsStepper.Substeps,bool originalCar=false,bool surfaceOnly=false)
        {
                    var root = new GameObject("road smoothness checks");
                    try
                    {
                        var trackObject = new GameObject("track"); trackObject.transform.SetParent(root.transform);
                        var track = trackObject.AddComponent<TrackBuilder>();
                        track.Build(new TrackRoute(Procedural.Generator.Generate(77, "normal", theme, false, true)));
                        // Material lifetime is an EditMode gate; PlayMode contact diagnostics run within one frame.
                        if(!(Application.isPlaying && Environment.GetEnvironmentVariable("STAR_RACING_SHIPPING_CONTACT_TRACE")=="1"))CheckMaterialLifecycle(track); Physics.SyncTransforms(); CheckGeometry(track);
                        if(Environment.GetEnvironmentVariable("STAR_RACING_SHIPPING_SLOW_MIDPHASE")=="1")foreach(var surface in track.GetComponentsInChildren<TrackSurface>()) {
                            var collider=surface.GetComponent<MeshCollider>();var mesh=collider.sharedMesh;collider.sharedMesh=null;
                            collider.cookingOptions&=~MeshColliderCookingOptions.UseFastMidphase;collider.sharedMesh=mesh;
                            Debug.Log("TOP_MIDPHASE_CONTROL "+collider.cookingOptions);
                        }
                        var carObject = new GameObject("car"); carObject.transform.SetParent(root.transform);
                        var car = carObject.AddComponent<MagneticVehicle>(); car.Initialize(track, 0, Color.cyan, balance, false);
                        car.ResetAt(track.Route.LoopStart - 20); car.Body.linearVelocity = car.Frame.tangent * car.Drive.BaseSpeed;
                        Physics.SyncTransforms();
                        if(originalCar)foreach(var collider in car.GetComponentsInChildren<BoxCollider>())if(collider.gameObject!=car.gameObject)collider.enabled=false;
                        if(surfaceOnly)foreach(var collider in track.GetComponentsInChildren<MeshCollider>())if(collider.sharedMesh!=null && (collider.sharedMesh.name==RoadVolumeMesh.BottomName || collider.sharedMesh.name==RoadVolumeMesh.ExteriorName || collider.sharedMesh.name==RoadVolumeMesh.SupportName))collider.enabled=false;
                        var driver = new AiDriver(0, DriverProfile.Racer, 77, balance);
                        var world = new AiWorldSnapshot(1); var cars = new[] { car };
                        float minSpeed = float.PositiveInfinity, maxSpeed = 0, minHeight = float.PositiveInfinity, maxHeight = 0;
                        int unsupported = 0, ticks = 0, revision = car.PositionRevision;
                        bool trace=Environment.GetEnvironmentVariable("STAR_RACING_SHIPPING_CONTACT_TRACE")=="1";
                        var contacts=new List<string>();var states=new List<string>();int nativePart=0;
                        if(trace && Environment.GetEnvironmentVariable("STAR_RACING_ROAD_OBSERVER")=="1"){
                            RoadNativeContactObserver.Begin(carObject);
                            foreach(var surface in track.GetComponentsInChildren<TrackSurface>())RoadNativeContactObserver.Register(surface.GetComponent<Collider>());
                        }
                        if(trace)car.ContactObserved+=collision=>{
                            if(ticks>352)return;
                            for(int c=0;c<collision.contactCount;c++) {
                                var contact=collision.GetContact(c);
                                contacts.Add($"LOOP_CONTACT tick={ticks} part={nativePart} self={contact.thisCollider.name}:{contact.thisCollider.GetInstanceID()} other={contact.otherCollider.name}:{contact.otherCollider.GetInstanceID()} point={contact.point.ToString("F6")} local={car.transform.InverseTransformPoint(contact.point).ToString("F6")} normal={contact.normal.ToString("F6")} impulse={collision.impulse.ToString("F6")} separation={contact.separation:G9}");
                            }
                        };
                        for (; ticks < 3000 && car.Distance < track.Route.LoopEnd; ticks++)
                        {
                            car.PrepareProjection(); world.Capture(cars, track.Route, ticks * .01);
                            var input = driver.Step(world, track.Route, .01f, false);
                            input.throttle = 1; input.brake = 0; input.nitro = false; input.drift = false;
                            car.SetInput(input); car.SetRaceContext(true, 1, 2, ticks * .01);
                            Tick.Invoke(car, null);nativePart=0;var beforeVelocity=car.Body.linearVelocity;
                            RacePhysicsStepper.SimulatePrepared(RacePhysicsStepper.Vehicles,.01f,substeps,()=>{
                                if(trace && ticks>=345 && ticks<=352) {
                                    RoadNativeContactObserver.AfterStep();
                                    contacts.Add($"LOOP_NATIVE tick={ticks} part={nativePart} before={beforeVelocity.ToString("G9")} after={car.Body.linearVelocity.ToString("G9")} position={car.Body.position.ToString("G9")} angular={car.Body.angularVelocity.ToString("G9")}");
                                    foreach(var collider in car.GetComponentsInChildren<BoxCollider>())contacts.Add($"LOOP_RAW tick={ticks} part={nativePart} self={collider.name}"+RoadNativeContactObserver.For(collider));
                                }
                                beforeVelocity=car.Body.linearVelocity;nativePart++;
                            });
                            if (car.Distance > track.Route.LoopStart + 10)
                            {
                                float speed = Vector3.ProjectOnPlane(car.Body.linearVelocity, car.Frame.normal).magnitude;
                                float height = Vector3.Dot(car.Body.position - car.Frame.position, car.Frame.normal);
                                minSpeed = Mathf.Min(minSpeed, speed); maxSpeed = Mathf.Max(maxSpeed, speed);
                                minHeight = Mathf.Min(minHeight, height); maxHeight = Mathf.Max(maxHeight, height);
                                if (car.HandlingSupportCount == 0) unsupported++;
                            }
                            if(trace)states.Add($"LOOP_STATE tick={ticks} distance={car.Distance:F6} speed={Vector3.ProjectOnPlane(car.Body.linearVelocity,car.Frame.normal).magnitude:F6} position={car.Body.position.ToString("F6")} lateral={Vector3.Dot(car.Body.position-car.Frame.position,car.Frame.right):F6} steer={input.steer:F6} height={Vector3.Dot(car.Body.position-car.Frame.position,car.Frame.normal):F6} pitch={Vector3.SignedAngle(car.Frame.tangent,car.transform.forward,car.Frame.right):F6} support={car.HandlingSupportCount} velocity={car.Body.linearVelocity.ToString("F6")} angular={car.Body.angularVelocity.ToString("F6")}");
                            if (car.IsFalling || car.PositionRevision != revision) break;
                        }
                        if(trace) {foreach(var line in contacts)Debug.Log(line);foreach(var line in states)Debug.Log(line);}
                        Debug.Log($"ROAD_LOOP_METRICS theme={theme} completed={car.Distance >= track.Route.LoopEnd} seconds={ticks * .01f:F2} speed={minSpeed:F3}..{maxSpeed:F3} height={minHeight:F3}..{maxHeight:F3} unsupported={unsupported} base={car.Drive.BaseSpeed:F3}");
                        Check(car.Distance>=track.Route.LoopEnd && !car.IsFalling && car.PositionRevision==revision,"shipping loop did not complete " + theme);
                        Check(minSpeed>=car.Drive.BaseSpeed*.97f,"shipping loop speed loss " + theme + ": " + minSpeed);
                        Check(unsupported==0 && maxHeight<.65f,"shipping loop bounced/detached " + theme);

                    }
                    finally { RoadNativeContactObserver.End();UnityEngine.Object.DestroyImmediate(root); }
        }
        public static void DiagnoseShippingLoop()
        {
            var mode=Physics.simulationMode;float dt=Time.fixedDeltaTime;
            try {
                Physics.simulationMode=SimulationMode.Script;Time.fixedDeltaTime=.01f;
                var control=Environment.GetEnvironmentVariable("STAR_RACING_SHIPPING_CONTROL")??"production";
                var balance=ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);
                CheckShippingLoop(Environment.GetEnvironmentVariable("STAR_RACING_SHIPPING_THEME")??"cloud-city",balance,control.EndsWith("1")?1:RacePhysicsStepper.Substeps,control.Contains("original"),control.Contains("surface"));
            }finally {Physics.simulationMode=mode;Time.fixedDeltaTime=dt;}
        }
        public static void DiagnoseShippingMesh()
        {
            var root=new GameObject("shipping mesh witness");
            try {
                var track=root.AddComponent<TrackBuilder>();track.Build(new TrackRoute(Procedural.Generator.Generate(77,"normal","space-station",false,true)));
                var surface=track.GetComponentInChildren<TrackSurface>();var collider=surface.GetComponent<MeshCollider>();
                var mesh=collider.sharedMesh;var vertices=mesh.vertices;var triangles=mesh.triangles;
                var output=Environment.GetEnvironmentVariable("STAR_RACING_ROAD_QA_DIR");System.IO.Directory.CreateDirectory(output);
                using(var writer=new System.IO.StreamWriter(System.IO.Path.Combine(output,"top.obj"))) {
                    foreach(var v in vertices)writer.WriteLine(FormattableString.Invariant($"v {v.x:G9} {v.y:G9} {v.z:G9}"));
                    for(int i=0;i<triangles.Length;i+=3)writer.WriteLine($"f {triangles[i]+1} {triangles[i+1]+1} {triangles[i+2]+1}");
                }
                int face=70687;var a=vertices[triangles[face*3]];var b=vertices[triangles[face*3+1]];var c=vertices[triangles[face*3+2]];
                var normal=Vector3.Cross(b-a,c-a).normalized;var centre=(a+b+c)/3;
                Physics.SyncTransforms();bool found=collider.Raycast(new Ray(centre+normal*.1f,-normal),out var hit,.2f);
                Debug.Log($"TOP_FACE_WITNESS cooking={collider.cookingOptions} face={face} a={a.ToString("G9")} b={b.ToString("G9")} c={c.ToString("G9")} normal={normal.ToString("G9")} rayFound={found} nativeRayFace={hit.triangleIndex} hit={hit.point.ToString("G9")}");
            }finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        public static void DiagnoseShippingLoops()
        {
            var mode=Physics.simulationMode;float dt=Time.fixedDeltaTime;
            try {
                Physics.simulationMode=SimulationMode.Script;Time.fixedDeltaTime=.01f;
                var balance=ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);
                foreach(var theme in new[]{"cloud-city","space-station"})CheckShippingLoop(theme,balance);
            }finally{Physics.simulationMode=mode;Time.fixedDeltaTime=dt;}
        }
        public static void DiagnoseBoostCoast()
        {
            var mode=Physics.simulationMode;float step=Time.fixedDeltaTime;
            try {Physics.simulationMode=SimulationMode.Script;Time.fixedDeltaTime=.01f;CheckCoast(true,false,true);}
            finally {Physics.simulationMode=mode;Time.fixedDeltaTime=step;}
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using StarRacingPrototype.Procedural;

namespace StarRacingPrototype {
    // Compiles six unique physical surfaces. The four junction guides never emit ribbons.
    public static class IndependentTrackMesh {
        static TrackFrame Frame(GraphFrame f) => new TrackFrame { distance=(float)f.distance,position=f.position.Float,
            tangent=f.tangent.Float,normal=f.normal.Float,right=-f.right.Float,halfWidth=(float)f.halfWidth };
        public static TrackFrame Evaluate(IndependentGraph graph,RoadFace face,float distance) {
            if(face.geometryId==3 || face.geometryId==4) {
                var node=graph.junctions.Single(j=>j.id==face.geometryId);
                return new TrackFrame{distance=distance,position=node.Point(0,distance).Float,tangent=node.tangent.Float,
                    normal=node.normal.Float,right=-node.right.Float,halfWidth=(float)IndependentGraph.HalfWidth};
            }
            return Frame(graph.Road(face.geometryId).Evaluate(distance));
        }
        // Support observers receive compiler-local triangles and their explicit world transform.
        public static GraphRoadSurface Build(Transform parent,IndependentGraph graph,int buildRevision,Material roadMaterial,Material railMaterial,
            Action<Transform,RoadVolumeMesh.SupportTriangle> observer=null) => BuildCore(parent,graph,buildRevision,roadMaterial,railMaterial,observer,false);
        // Explicit experimental path. Only common longitudinal elevation is admitted.
        public static GraphRoadSurface BuildCommonVertical(Transform parent,IndependentGraph graph,int buildRevision,Material roadMaterial,Material railMaterial,
            Action<Transform,RoadVolumeMesh.SupportTriangle> observer=null) => BuildCore(parent,graph,buildRevision,roadMaterial,railMaterial,observer,true);
        // Catalog path additionally preserves rolled and inverted common frames. Branches,
        // junctions, jump contracts and the planar junction collars retain their guards.
        public static GraphRoadSurface BuildCommonCatalog(Transform parent,IndependentGraph graph,int buildRevision,Material roadMaterial,Material railMaterial,
            Action<Transform,RoadVolumeMesh.SupportTriangle> observer=null) => BuildCore(parent,graph,buildRevision,roadMaterial,railMaterial,observer,true,true);
        static GraphRoadSurface BuildCore(Transform parent,IndependentGraph graph,int buildRevision,Material roadMaterial,Material railMaterial,
            Action<Transform,RoadVolumeMesh.SupportTriangle> observer,bool commonVertical,bool commonCatalog=false) {
            GraphRoadSurface result=null;LoadingWork.Drain(BuildSteps(parent,graph,buildRevision,roadMaterial,railMaterial,observer,commonVertical,commonCatalog,value=>result=value));return result;
        }
        internal static IEnumerator BuildSteps(Transform parent,IndependentGraph graph,int buildRevision,Material roadMaterial,Material railMaterial,Action<Transform,RoadVolumeMesh.SupportTriangle> observer,bool commonVertical,bool commonCatalog,Action<GraphRoadSurface> completed) {
            var budget=new LoadingWork.Budget();
            if(!parent.position.Equals(Vector3.zero) || !parent.rotation.Equals(Quaternion.identity) || !parent.lossyScale.Equals(Vector3.one))
                throw new ArgumentException("Graph coordinates require an identity parent transform");
            if(parent.childCount!=0 || parent.GetComponents<Component>().Length!=1)throw new ArgumentException("Graph compiler requires a dedicated empty root");
            if(!commonVertical && graph.jumps.Count>0)throw new ArgumentException("Jumps require the explicit common physical compiler");
            if(graph.legacy || graph.Validate().Length!=0)throw new ArgumentException("Expected validated independent graph");
            // Keep the planar entry exact before float conversion. The explicit
            // common path retains elevation and protects the planar node collars.
            var carrier=graph.junctions.Single(j=>j.id==3);
            foreach(var edge in graph.roads)foreach(var sample in edge.samples)
                if(sample.halfWidth!=IndependentGraph.HalfWidth || sample.railLeft!=(graph.guardrailMode!="none") || sample.railRight!=(graph.guardrailMode!="none"))
                    throw new ArgumentException("Graph compiler requires canonical width and rail metadata");
                else if(!commonVertical || edge.id!=IndependentGraph.Approach && edge.id!=IndependentGraph.Exit) {
                    if(Math.Abs(DVec.Dot(sample.position-carrier.origin,carrier.normal))>1e-8 || (sample.normal-carrier.normal).Squared>1e-16)
                        throw new ArgumentException("Planar graph compiler cannot flatten nonplanar geometry");
                } else if(!commonCatalog && ((sample.right-carrier.right).Squared>1e-16 || DVec.Dot(sample.tangent,carrier.tangent)<.7 || DVec.Dot(sample.normal,carrier.normal)<.7 ||
                    Math.Abs(DVec.Dot(sample.position-carrier.origin,carrier.right))>1e-8))
                    throw new ArgumentException("Common vertical compiler requires longitudinal elevation without banking");
            foreach(var node in graph.junctions)
                if(Math.Abs(DVec.Dot(node.origin-carrier.origin,carrier.normal))>1e-8 || (node.normal-carrier.normal).Squared>1e-16)
                    throw new ArgumentException("Common vertical compiler requires coplanar junctions");
            if(commonVertical)foreach(int id in new[]{IndependentGraph.Approach,IndependentGraph.Exit}) {
                var edge=graph.Road(id);
                const double collar=50; // Existing turn reserve, also clear of the junction window.
                if(edge.Length<collar)throw new ArgumentException("Common road needs a planar junction collar");
                bool InCollar(double d)=>id==IndependentGraph.Approach?d>=edge.Length-collar:d<=collar;
                var boundary=edge.Evaluate(id==IndependentGraph.Approach?edge.Length-collar:collar);
                foreach(var f in edge.samples.Where(f=>InCollar(f.distance)).Concat(new[]{boundary}))
                    if(Math.Abs(DVec.Dot(f.position-carrier.origin,carrier.normal))>1e-8 ||
                        (f.normal-carrier.normal).Squared>1e-16 || (f.tangent-carrier.tangent).Squared>1e-16)
                        throw new ArgumentException("Common elevation must remain outside the 50m planar junction collar");
            }
            // Frames already certified coplanar must stay bit-exact y=0 in the
            // compiler frame. Tiny double basis residues otherwise become real
            // triangle tilts and can trigger a CCD edge bounce at rejoin.
            bool Flat(GraphFrame f)=>Math.Abs(DVec.Dot(f.position-carrier.origin,carrier.normal))<=1e-8 && (f.normal-carrier.normal).Squared<=1e-16;
            Vector3 Local(DVec point,bool planar=false) {var delta=point-carrier.origin;return new Vector3((float)-DVec.Dot(delta,carrier.right),commonVertical&&!planar?(float)DVec.Dot(delta,carrier.normal):0,(float)DVec.Dot(delta,carrier.tangent));}
            Vector3 Direction(DVec value,bool planar=false)=>new Vector3((float)-DVec.Dot(value,carrier.right),commonVertical&&!planar?(float)DVec.Dot(value,carrier.normal):0,(float)DVec.Dot(value,carrier.tangent));
            Vector3 Up(GraphFrame f)=>!commonVertical||Flat(f)?Vector3.up:Direction(f.normal);
            var geometryRoot=new GameObject("Independent geometry frame");geometryRoot.transform.SetParent(parent,false);
            geometryRoot.transform.localPosition=carrier.origin.Float;
            geometryRoot.transform.localRotation=Quaternion.LookRotation(carrier.tangent.Float,carrier.normal.Float);
            bool complete=false;
            try {
            TrackFrame LocalFrame(RoadFace face,float distance) {
                if(face.geometryId==3 || face.geometryId==4) {
                    var node=graph.junctions.Single(j=>j.id==face.geometryId);
                    return new TrackFrame{distance=distance,position=Local(node.Point(0,distance),true),tangent=Direction(node.tangent,true),normal=Vector3.up,right=-Direction(node.right,true),halfWidth=8.5f};
                }
                var f=graph.Road(face.geometryId).Evaluate(distance);
                return new TrackFrame{distance=distance,position=Local(f.position,Flat(f)),tangent=Direction(f.tangent,Flat(f)),normal=Up(f),right=-Direction(f.right,!commonCatalog||Flat(f)),halfWidth=(float)f.halfWidth};
            }
            var road=new ProceduralTrackMesh.Geometry();var rails=new ProceduralTrackMesh.Geometry();
            var portVertices=new Dictionary<int,Vector3[]>();
            foreach(var port in graph.ports) {
                var cuts=new Vector3[35];
                for(int i=0;i<cuts.Length;i++)cuts[i]=Local(port.frame.position+port.frame.right*(-8.5+i*.5),Flat(port.frame));
                portVertices.Add(port.id,cuts);
            }
            Vector3 PortPoint(int port,double q) {
                int cut=(int)Math.Round((q+8.5)*2);
                if(cut<0 || cut>=35 || Math.Abs(q-(-8.5+cut*.5))>1e-8)throw new InvalidOperationException("Unregistered port cut");
                return portVertices[port][cut];
            }
            void Quad(int geometry,int arm,int sample,GraphFrame a,GraphFrame b,double left0,double right0,double left1,double right1,
                int startPort,int endPort,double startOffset,double endOffset,int leftTag=0,int rightTag=0,GraphJump ramp=null) {
                var face=new RoadFace(sample,road.quadCount++,(float)a.distance,(float)b.distance,(float)left0,(float)right0,(float)left1,(float)right1,ramp!=null) {
                    geometryId=geometry,arm=arm,startPort=startPort,endPort=endPort,startSeamOffset=(float)startOffset,endSeamOffset=(float)endOffset,
                    leftBoundaryTag=leftTag,rightBoundaryTag=rightTag,canonicalFlatPorts=true };
                Vector3 Point(GraphFrame f,double lateral,int port,double offset)=>port!=0?PortPoint(port,lateral+offset):Local(f.position+f.right*lateral,Flat(f))+Direction(f.normal)*(float)(ramp?.Height(f.distance,graph.guardrailMode)??0);
                Vector3 RampUp(GraphFrame f,double lane)=>ramp==null?(commonCatalog && (geometry==0||geometry==5) && !Flat(f)?Direction(graph.Road(geometry).SurfaceNormal(f.distance,lane)):Up(f)):Direction(DVec.Cross(f.right,(f.tangent+f.normal*ramp.Slope(f.distance,graph.guardrailMode)).Unit()).Unit());
                road.Quad(Point(a,left0,startPort,startOffset),Point(b,left1,endPort,endOffset),Point(a,right0,startPort,startOffset),Point(b,right1,endPort,endOffset),
                    sample,RampUp(a,left0),face,uvStart:(float)a.distance/4,uvEnd:(float)b.distance/4,endUp:RampUp(b,left1),
                    rightUp:RampUp(a,right0),rightEndUp:RampUp(b,right1),uvSideStart:(float)left0/4,uvSideEnd:(float)left1/4);
            }
            void Rail(Vector3 a,Vector3 b,Vector3 side0,Vector3 side1,Vector3 up,bool joinedCenter=false,Vector3 endUp=default) {
                if(endUp.sqrMagnitude==0)endUp=up;
                Vector3 outward=side0.sqrMagnitude>0?side0:side1;
                rails.Quad(a,b,a+up*1.3f,b+endUp*1.3f,0,-outward);
                if(!joinedCenter)rails.Quad(a+side0,b+side1,a+side0+up*1.3f,b+side1+endUp*1.3f,0,outward);
                rails.Quad(a+up*1.3f,b+endUp*1.3f,a+side0+up*1.3f,b+side1+endUp*1.3f,0,up);
            }
            foreach(int id in new[]{IndependentGraph.Approach,IndependentGraph.Technical,IndependentGraph.Speed,IndependentGraph.Exit}) {
                var edge=graph.Road(id);
                // Preserve canonical polyline corners on elevated common roads; planar
                // compilation retains its previous station sequence exactly.
                var stations=new List<double>{0};
                if(commonVertical && (id==IndependentGraph.Approach || id==IndependentGraph.Exit)) {
                    for(int sample=0;sample+1<edge.samples.Count;sample++) {
                        double from=edge.samples[sample].distance,to=edge.samples[sample+1].distance;
                        int pieces=(int)Math.Ceiling((to-from)/.5);
                        for(int piece=1;piece<=pieces;piece++)stations.Add(piece==pieces?to:from+(to-from)*piece/pieces);
                    }
                } else {
                    int pieces=(int)Math.Ceiling(edge.Length/.5);
                    for(int piece=1;piece<=pieces;piece++)stations.Add(edge.Length*piece/pieces);
                }
                int steps=stations.Count-1;
                for(int i=0;i<steps;i++) {
                    if(budget.Due){yield return null;budget.Restart();}
                    var a=edge.Evaluate(stations[i]);var b=edge.Evaluate(stations[i+1]);
                    int start=i==0?edge.fromPort:0,end=i==steps-1?edge.toPort:0;
                    // Uniform .5m cross-port cuts include q=0 and match both node halves.
                    var jump=graph.jumps.FirstOrDefault(j=>j.roadId==id && a.distance>=j.rampStart && a.distance<j.gapEnd);
                    var cuts=new List<double>();for(int lane=0;lane<=34;lane++)cuts.Add(-IndependentGraph.HalfWidth+lane*.5);
                    if(jump!=null){cuts.Add(jump.lateralCenter-jump.lateralHalfWidth);cuts.Add(jump.lateralCenter+jump.lateralHalfWidth);}
                    cuts=cuts.Distinct().OrderBy(q=>q).ToList();
                    for(int lane=0;lane+1<cuts.Count;lane++) {
                        double left=cuts[lane],right=cuts[lane+1],middle=(left+right)*.5;
                        if(jump!=null && jump.Gap(a.distance,middle,graph.guardrailMode))continue;
                        // A native ramp is one authored transverse strip per .5m
                        // station, as in the legacy physical compiler. Tiling it into
                        // .5m squares introduces unnecessary internal CCD edges.
                        if(jump!=null&&jump.ContainsLane(middle)&&jump.Ramp(a.distance,b.distance,graph.guardrailMode))continue;
                        Quad(id,edge.branch,i,a,b,left,right,left,right,start,end,0,0);
                    }
                    if(jump!=null&&jump.Ramp(a.distance,b.distance,graph.guardrailMode)) {
                        double left=jump.lateralCenter-jump.lateralHalfWidth,right=jump.lateralCenter+jump.lateralHalfWidth;
                        Quad(id,edge.branch,i,a,b,left,right,left,right,start,end,0,0,ramp:jump);
                    }
                    if(graph.guardrailMode!="none")foreach(int side in new[]{-1,1}) {
                        Vector3 pa=start!=0?PortPoint(start,side*IndependentGraph.HalfWidth):Local(a.position+a.right*(side*IndependentGraph.HalfWidth),Flat(a));
                        Vector3 pb=end!=0?PortPoint(end,side*IndependentGraph.HalfWidth):Local(b.position+b.right*(side*IndependentGraph.HalfWidth),Flat(b));
                        if(jump!=null && graph.guardrailMode!="full" && a.distance>=jump.launch && a.distance<jump.gapEnd && jump.ContainsLane(side*IndependentGraph.HalfWidth))continue;
                        var railRamp=jump!=null && jump.ContainsLane(side*IndependentGraph.HalfWidth) && jump.Ramp(a.distance,b.distance,graph.guardrailMode)?jump:null;
                        if(railRamp!=null){pa+=Direction(a.normal)*(float)railRamp.Height(a.distance,graph.guardrailMode);pb+=Direction(b.normal)*(float)railRamp.Height(b.distance,graph.guardrailMode);}
                        Rail(pa,pb,Direction(a.right,!commonCatalog||Flat(a))*(side*.3f),Direction(b.right,!commonCatalog||Flat(b))*(side*.3f),Up(a),endUp:Up(b));
                    }
                }
            }
            foreach(var node in graph.junctions) {
                for(int i=0;i+1<node.stations.Count;i++) {
                    var s=node.stations[i];var t=node.stations[i+1];
                    GraphFrame Carrier(JunctionStation row)=>new GraphFrame(row.z,node.Point(0,row.z),node.tangent,node.normal,node.right,IndependentGraph.HalfWidth);
                    var a=Carrier(s);var b=Carrier(t);
                    foreach(int arm in new[]{IndependentGraph.Technical,IndependentGraph.Speed}) {
                        bool left=arm==IndependentGraph.Technical;
                        double l0=left?-s.outer:s.gapHalf,r0=left?-s.gapHalf:s.outer;
                        double l1=left?-t.outer:t.gapHalf,r1=left?-t.gapHalf:t.outer;
                        if(Math.Max(r0-l0,r1-l1)>17+1e-8)throw new InvalidOperationException("Junction band exceeds canonical width");
                        int lanes=(int)Math.Ceiling(Math.Min(17,Math.Max(r0-l0,r1-l1))/.5);
                        // Explicit port cut counts; interior growth cannot invent a different seam grid.
                        if(i==0)lanes=node.merging?34:17;
                        if(i==node.stations.Count-2)lanes=node.merging?17:34;
                        int start=i==0?(node.merging?(left?21:22):10):0;
                        int end=i==node.stations.Count-2?(node.merging?30:(left?11:12)):0;
                        double startOffset=node.merging?(left?30:-30):0,endOffset=node.merging?0:(left?30:-30);
                        int tag=s.gapHalf==0 && t.gapHalf==0?0:arm;
                        for(int lane=0;lane<lanes;lane++) {
                            double lA=l0+(r0-l0)*lane/lanes,rA=l0+(r0-l0)*(lane+1)/lanes;
                            double lB=l1+(r1-l1)*lane/lanes,rB=l1+(r1-l1)*(lane+1)/lanes;
                            Quad(node.id,arm,i,a,b,lA,rA,lB,rB,start,end,startOffset,endOffset,
                                !left&&lane==0?tag:0,left&&lane==lanes-1?tag:0);
                        }
                        if(graph.guardrailMode=="none")continue;
                        double sign=left?-1:1;
                        Rail(start!=0?PortPoint(start,sign*8.5):Local(node.Point(sign*s.outer,s.z),true),end!=0?PortPoint(end,sign*8.5):Local(node.Point(sign*t.outer,t.z),true),Direction(node.right,true)*(float)(sign*.3),Direction(node.right,true)*(float)(sign*.3),Vector3.up);
                        if(s.gapHalf>0 || t.gapHalf>0) {
                            // Each half occupies only its own half of the actual gap; a single tapered nose.
                            Rail(Local(node.Point(sign*s.gapHalf,s.z),true),Local(node.Point(sign*t.gapHalf,t.z),true),
                                Direction(node.right,true)*(float)(-sign*Math.Min(.3,s.gapHalf)),Direction(node.right,true)*(float)(-sign*Math.Min(.3,t.gapHalf)),Vector3.up,s.gapHalf<=.3 && t.gapHalf<=.3);
                        }
                    }
                }
            }
            for(int i=rails.triangles.Count-3;i>=0;i-=3) {
                var a=rails.vertices[rails.triangles[i]];var b=rails.vertices[rails.triangles[i+1]];var c=rails.vertices[rails.triangles[i+2]];
                if(Vector3.Cross(b-a,c-a).sqrMagnitude<=1e-12f)rails.triangles.RemoveRange(i,3);
            }
            if(!commonVertical)road.Create(geometryRoot.transform,"Independent road",roadMaterial,true);
            else {
                // Keep the certified planar junction domain out of nonplanar
                // mesh cooking. Each authored quad still occurs exactly once;
                // shell/support seam cancellation continues over the whole top.
                var pieces=new List<(ProceduralTrackMesh.Geometry geometry,Vector3 offset)>();Vector3 chunkOffset=Vector3.zero;
                int previousGeometry=-1;bool previousPlanar=false;ProceduralTrackMesh.Geometry piece=null;
                for(int q=0;q<road.vertices.Count/4;q++) {
                    if(budget.Due){yield return null;budget.Restart();}
                    int at=q*4;var face=road.faces[q*2];
                    bool planar=road.vertices[at].y==0 && road.vertices[at+1].y==0 && road.vertices[at+2].y==0 && road.vertices[at+3].y==0;
                    if(piece==null || face.geometryId!=previousGeometry || planar!=previousPlanar) {
                        piece=new ProceduralTrackMesh.Geometry();
                        // Raised common strips cook close to their own origin. Canonical
                        // float vertices minus/add this translation retain the same exact top.
                        // Catalog strips retain the common carrier transform at planar
                        // joins; independent translated frames can open float cracks
                        // on a rotated hill entry. Native ramp origins remain unchanged.
                        // The PhysX ramp triangles avoid kilometre-scale longitudinal coordinates.
                        chunkOffset=graph.jumps.Count>0&&!planar&&(!commonCatalog||face.ramp)?new Vector3(0,0,road.vertices[at].z):Vector3.zero;
                        pieces.Add((piece,chunkOffset));previousGeometry=face.geometryId;previousPlanar=planar;
                    }
                    face.quad=piece.quadCount++;
                    piece.Quad(road.vertices[at]-chunkOffset,road.vertices[at+1]-chunkOffset,road.vertices[at+2]-chunkOffset,road.vertices[at+3]-chunkOffset,face.sample,road.smoothNormals[at],face,
                        uvStart:road.uv[at].x,uvEnd:road.uv[at+1].x,endUp:road.smoothNormals[at+1],rightUp:road.smoothNormals[at+2],rightEndUp:road.smoothNormals[at+3],
                        uvSideStart:road.uv[at].y,uvSideEnd:road.uv[at+1].y);
                }
                foreach(var chunk in pieces) {
                    var context=geometryRoot.transform;
                    if(chunk.offset!=Vector3.zero){var pivot=new GameObject("Common strip local origin");pivot.transform.SetParent(context,false);pivot.transform.localPosition=chunk.offset;context=pivot.transform;}
                    chunk.geometry.Create(context,"Independent road",roadMaterial,true);
                    yield return null;budget.Restart();
                }
            }
            yield return RoadVolumeMesh.BuildGraphSteps(geometryRoot.transform,LocalFrame,road.vertices,road.faces,road.triangles,roadMaterial,observer==null?null:tri=>observer(geometryRoot.transform,tri),port=>Up(graph.ports.Single(p=>p.id==port).frame));
            if(rails.triangles.Count>0)rails.Create(geometryRoot.transform,"Independent guard rails",railMaterial,false);
            var surface=parent.gameObject.AddComponent<GraphRoadSurface>();surface.Initialize(graph,buildRevision);
            completed(surface);complete=true;
            } finally {
                if(!complete){
                var meshes=new HashSet<Mesh>();
                foreach(var filter in geometryRoot.GetComponentsInChildren<MeshFilter>())if(filter.sharedMesh!=null)meshes.Add(filter.sharedMesh);
                foreach(var collider in geometryRoot.GetComponentsInChildren<MeshCollider>())if(collider.sharedMesh!=null)meshes.Add(collider.sharedMesh);
                // Detach immediately so a retry can reuse the dedicated empty root,
                // even while runtime Destroy is deferred until the end of the frame.
                geometryRoot.transform.SetParent(null,false);
                if(Application.isPlaying)UnityEngine.Object.Destroy(geometryRoot);else UnityEngine.Object.DestroyImmediate(geometryRoot);
                foreach(var mesh in meshes)if(Application.isPlaying)UnityEngine.Object.Destroy(mesh);else UnityEngine.Object.DestroyImmediate(mesh);
                }
            }
        }
    }
}

using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using StarRacingPrototype.Procedural;
namespace StarRacingPrototype {
    public static class GraphVehicleRouteChecks {
        static int assertions;
        static void Check(bool value,string message) { assertions++; if(!value)throw new Exception("Graph vehicle: "+message); }
        static Pose PoseAt(IndependentGraph graph,int road,double distance,float lateral=0) {
            var f=graph.Road(road).Evaluate(distance);
            return new Pose(f.position.Float-f.right.Float*lateral+f.normal.Float*VehicleGeometry.AdheredBodyHeight,Quaternion.LookRotation(f.tangent.Float,f.normal.Float));
        }
        static GraphVehicleRoute Spawn(GraphRoadSurface surface,int road,double distance,int branch=0,float lateral=0) {
            var cursor=surface.Graph.Cursor(road,distance,branch);var pose=PoseAt(surface.Graph,road,distance,lateral);
            Check(GraphVehicleRoute.TryCreate(surface,cursor,pose,1,0,out var route),"spawn "+road+" at "+distance);
            return route;
        }
        static void Walk(GraphVehicleRoute route,IndependentGraph graph,int road,double from,double to,ref long tick,float lateral=0) {
            int steps=Math.Max(1,(int)Math.Ceiling(Math.Abs(to-from)/2));
            for(int i=1;i<=steps;i++) {
                double at=from+(to-from)*i/steps;
                Check(route.Observe(PoseAt(graph,road,at,lateral),route.PositionRevision,++tick),"walk road="+road+" at="+at+" cursor="+route.Cursor.roadId+"/"+route.Cursor.localPhysicalDistance);
            }
        }
        static void Traverse(GraphRoadSurface surface,int branch) {
            var graph=surface.Graph;var route=Spawn(surface,0,10);long tick=0;
            int[] roads={0,branch==1?10:11,branch,branch==1?12:13,5};
            foreach(int road in roads) {
                double start=road==0?10:0,end=graph.Road(road).Length-(road==5?10:0);
                Walk(route,graph,road,start,end,ref tick);
                if(road==branch) {
                    Check(route.CommittedBranch==branch,"branch committed by split support");
                    var pose=PoseAt(graph,road,end-10);
                    Walk(route,graph,road,end,end-10,ref tick);
                    Check(route.TryCaptureSafePose(pose,out var safe),"safe pose on branch "+branch);
                    Walk(route,graph,road,end-10,end,ref tick);
                    Check(route.TryRestore(safe,2,++tick,out var restored),"restore branch "+branch);
                    Check(restored.position==pose.position && route.Cursor.roadId==branch && route.CommittedBranch==branch && !route.LastObservationContinuous,"restore full state, no credit");
                    Walk(route,graph,road,end-10,end,ref tick);
                }
            }
            Check(route.Cursor.roadId==5 && route.CommittedBranch==branch,"exit retains incoming history");
            // Reverse through merge must preserve the road that was actually traversed.
            Walk(route,graph,5,graph.Road(5).Length-10,0,ref tick);
            int merge=branch==1?12:13;
            Walk(route,graph,merge,graph.Road(merge).Length,0,ref tick);
            Walk(route,graph,branch,graph.Road(branch).Length,graph.Road(branch).Length-15,ref tick);
            Check(route.Cursor.roadId==branch && route.CommittedBranch==branch,"reverse merge history");
        }
        static void SplitChoice(GraphRoadSurface surface) {
            var graph=surface.Graph;var node=graph.junctions.Single(j=>!j.merging);
            var route=Spawn(surface,0,150);long tick=0;
            Walk(route,graph,0,150,160,ref tick);
            Walk(route,graph,10,0,35,ref tick);
            Check(route.CommittedBranch==0,"guide remains provisional on shared split");
            var f=graph.Road(11).Evaluate(35);
            Check(route.Observe(new Pose((f.position+f.normal*VehicleGeometry.AdheredBodyHeight).Float,Quaternion.LookRotation(f.tangent.Float,f.normal.Float)),1,++tick),"switch side on shared split");
            Check(route.Cursor.roadId==11 && route.CommittedBranch==0,"provisional sibling chosen");
            Check(route.TryCaptureSafePose(new Pose((f.position+f.normal*VehicleGeometry.AdheredBodyHeight).Float,Quaternion.LookRotation(f.tangent.Float,f.normal.Float)),out var provisional),"safe provisional pose");
            Walk(route,graph,11,35,150,ref tick);
            Check(route.CommittedBranch==2,"support beyond divider commits speed");
            var before=route.Cursor;
            Check(!route.Observe(PoseAt(graph,10,150),1,++tick),"foreign arm cannot switch committed branch");
            Check(route.Cursor.roadId==before.roadId && route.Cursor.localPhysicalDistance==before.localPhysicalDistance,"rejected attempt preserves cursor");
            Check(route.TryRestore(provisional,2,++tick,out _),"restore provisional state");
            Check(route.CommittedBranch==0 && route.Cursor.roadId==11,"recovery preserves provisional choice");
            Check(route.Observe(PoseAt(graph,11,35),2,++tick) && !route.LastObservationContinuous,"recovery reanchors without credit");
            Walk(route,graph,11,35,150,ref tick);
            Walk(route,graph,11,150,35,ref tick);
            Check(route.CommittedBranch==0,"reverse back into common split reopens choice");
            Check(route.Observe(PoseAt(graph,10,35),2,++tick),"reverse provisional can switch again");
            var broken=Spawn(surface,11,80);long b=0;
            Check(broken.CommittedBranch==0,"before split divider");
            Check(!broken.Observe(PoseAt(graph,11,100),1,b+=2) && broken.CommittedBranch==0,"missing tick cannot commit branch on same guide");
            Check(!broken.Observe(PoseAt(graph,11,100),1,++b) && broken.CommittedBranch==0,"rejected observation cannot commit next tick");
            var reverseBroken=Spawn(surface,11,100);
            Check(reverseBroken.CommittedBranch==2,"beyond split divider");
            Check(!reverseBroken.Observe(PoseAt(graph,11,80),1,2) && reverseBroken.CommittedBranch==2,"missing tick cannot release branch in reverse");
            var regain=Spawn(surface,10,35);var raised=PoseAt(graph,10,35);raised.position+=raised.rotation*Vector3.up*.8f;
            Check(!regain.Observe(raised,1,1) && !regain.RequiresRecovery,"temporary wheel loss suspends admission without respawn");
            Check(regain.Observe(PoseAt(graph,11,35),1,2) && regain.CommittedBranch==0 && !regain.LastObservationContinuous && !regain.RequiresRecovery,"shared split sibling reanchor without credit");
            long regainTick=2;Walk(regain,graph,11,35,150,ref regainTick);
            Check(regain.CommittedBranch==2,"reanchored choice continues normally");
            // A short chord across the tiny opening still crosses the exact divider.
            double tip=node.stations.Last(s=>s.gapHalf==0).z;
            var left=node.Point(-2,tip+.01)+node.normal*VehicleGeometry.AdheredBodyHeight;
            var right=node.Point(2,tip+.01)+node.normal*VehicleGeometry.AdheredBodyHeight;
            var near=Spawn(surface,10,graph.Road(10).samples.OrderBy(s=>(s.position-left).Squared).First().distance-5);
            long n=0;
            Check(near.Observe(new Pose(left.Float,Quaternion.LookRotation(node.tangent.Float,node.normal.Float)),1,++n),"supported point beside tiny divider");
            Check(!near.Observe(new Pose(right.Float,Quaternion.LookRotation(node.tangent.Float,node.normal.Float)),1,++n),"sub-sample divider chord rejected");
        }
        static void RecoveryBoundary(GraphRoadSurface surface) {
            var graph=surface.Graph;var route=Spawn(surface,0,159.9);
            Check(route.TryCaptureSafePose(PoseAt(graph,0,159.9),out var safe),"safe footprint immediately before port");
            Check(route.TryRestore(safe,2,1,out _),"restore port anchor");
            Check(route.Observe(PoseAt(graph,10,.35),2,2) && route.Cursor.roadId==10 && !route.RequiresRecovery &&
                !route.LastObservationContinuous && route.LastPhysicalDelta==0,"first nonzero recovery step crosses port without credit or loop");
            var split=graph.junctions.Single(j=>!j.merging);double tip=split.stations.Last(s=>s.gapHalf==0).z;
            double at=graph.Road(11).samples.Single(s=>Math.Abs(DVec.Dot(s.position-split.origin,split.tangent)-tip)<1e-7).distance;
            var divider=Spawn(surface,11,at-.1);
            Check(divider.CommittedBranch==0 && divider.TryCaptureSafePose(PoseAt(graph,11,at-.1),out safe),"safe pose immediately before divider");
            Check(divider.TryRestore(safe,2,1,out _),"restore divider anchor");
            Check(divider.Observe(PoseAt(graph,11,at+.35),2,2) && divider.CommittedBranch==2 && !divider.RequiresRecovery &&
                !divider.LastObservationContinuous && divider.LastPhysicalDelta==0,"first nonzero recovery step commits divider without credit or loop");
        }
        static void InvalidObservations(GraphRoadSurface surface) {
            var graph=surface.Graph;var route=Spawn(surface,1,100);long tick=0;
            var lost=Spawn(surface,0,159);
            Check(lost.TryCaptureSafePose(PoseAt(graph,0,159),out var prePort),"safe pre-port snapshot");
            Check(!lost.Observe(PoseAt(graph,10,2),1,2) && lost.RequiresRecovery,"missing sample at port signals recovery");
            Check(!lost.Observe(PoseAt(graph,10,3),1,3) && lost.Cursor.roadId==0,"no retroactive port admission");
            Check(lost.TryRestore(prePort,2,4,out _) && !lost.RequiresRecovery && !lost.LastObservationContinuous,"lost traversal restores proven safe pose without credit");
            var floating=PoseAt(graph,1,100);floating.position+=floating.rotation*Vector3.up*1.1f;
            Check(!route.Observe(floating,1,++tick),"nearby centre road is not wheel support");
            Check(!route.TryCaptureSafePose(floating,out _),"unsupported pose cannot become safe");
            var before=route.Cursor;
            Check(!route.Observe(PoseAt(graph,2,100),1,++tick),"cross-road shortcut rejected");
            Check(!route.Observe(PoseAt(graph,12,100),1,++tick),"merge shortcut rejected");
            Check(!route.Observe(PoseAt(graph,1,101),2,++tick),"position revision mismatch rejected");
            Check(route.Cursor.localPhysicalDistance==before.localPhysicalDistance,"invalid observations cannot advance cursor");
            Check(route.Observe(PoseAt(graph,1,101),1,++tick) && !route.LastObservationContinuous,"reacquire same road without credit");
            Check(!route.Observe(PoseAt(graph,1,102),1,tick),"duplicate tick rejected");
            Check(!route.Observe(new Pose(new Vector3(float.NaN,0,0),Quaternion.identity),1,++tick),"nonfinite observation rejected");
            Check(route.Observe(PoseAt(graph,1,102),1,tick+=2) && !route.LastObservationContinuous,"missing tick breaks credit continuity");
            Check(route.Observe(PoseAt(graph,1,103),1,++tick) && route.LastObservationContinuous,"fresh consecutive sample resumes");
            var pose=PoseAt(graph,1,103);
            Check(route.TryCaptureSafePose(pose,out var safe),"capture valid state");
            var other=Spawn(surface,1,103);
            Check(!other.TryRestore(safe,2,1,out _),"foreign vehicle safe token rejected");
            Check(!route.TryRestore(safe,1,++tick,out _),"recovery requires new revision");
            var saved=route.Cursor;
            route.ProjectPresentation(PoseAt(graph,2,100).position);route.LookAhead(40);
            Check(route.Cursor.roadId==saved.roadId && route.Cursor.localPhysicalDistance==saved.localPhysicalDistance,"presentation/lookahead read only");
            var obstacle=GameObject.CreatePrimitive(PrimitiveType.Cube);
            try {
                obstacle.transform.SetParent(surface.transform);obstacle.transform.position=pose.position;Physics.SyncTransforms();
                Check(!route.TryRestore(safe,2,++tick,out _),"new barrier blocks recovery");
            } finally {UnityEngine.Object.DestroyImmediate(obstacle);}
            var collider=surface.GetComponentInChildren<TrackSurface>().GetComponent<MeshCollider>();
            collider.enabled=false;
            Check(!route.TryRestore(safe,2,++tick,out _),"disabled road cannot restore");collider.enabled=true;
            surface.enabled=false;
            Check(!route.TryRestore(safe,2,++tick,out _),"inactive owner cannot restore");surface.enabled=true;
            typeof(GraphRoadSurface).GetMethod("Initialize",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(surface,new object[]{graph,surface.BuildRevision+1});
            Check(!route.TryRestore(safe,2,++tick,out _) && !route.Observe(pose,1,++tick),"same graph new build invalidates route/safe pose");
        }
        static void Case(IndependentGraph graph,string label) {
            var root=new GameObject("Graph vehicle QA "+label);
            try {
                var surface=IndependentTrackMesh.Build(root.transform,graph,71,null,null,(_,__)=>{});
                Physics.SyncTransforms();
                Traverse(surface,1);Traverse(surface,2);SplitChoice(surface);RecoveryBoundary(surface);InvalidObservations(surface);
                Debug.Log("GRAPH_VEHICLE_CASE_OK "+label+" assertions="+assertions);
            } finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        public static void Run() {
            string output=Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_VEHICLE_QA_DIR");
            if(string.IsNullOrEmpty(output))throw new Exception("Explicit evidence directory required");
            Directory.CreateDirectory(output);assertions=0;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var t=new DVec(0,0,1);var n=new DVec(0,1,0);var r=new DVec(-1,0,0);
            Case(IndependentGraph.Create(new DVec(),t,n,r),"normal");
            var axis=new DVec(.3,.7,.2).Unit();
            Case(IndependentGraph.Create(new DVec(123,45,-67),t.Rotate(axis,.81),n.Rotate(axis,.81),r.Rotate(axis,.81),"full"),"rotated");
            CheckpointAdmissionChecks.Run();TrackProjectionParityChecks.Run();VehiclePresentationChecks.Run();
            File.WriteAllText(Path.Combine(output,"result.txt"),"GRAPH_VEHICLE_ROUTE_OK assertions="+assertions+" cases=2 support=actual-top-colliders traversal=synthetic-observations\n");
            Debug.Log("GRAPH_VEHICLE_ROUTE_OK assertions="+assertions);
        }
    }
}

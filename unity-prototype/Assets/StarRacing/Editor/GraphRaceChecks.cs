using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor.SceneManagement;
using StarRacingPrototype.Procedural;
namespace StarRacingPrototype {
    public static class GraphRaceChecks {
        static int assertions;
        static void Check(bool ok,string why){assertions++;if(!ok)throw new Exception("Graph race: "+why);}
        static Pose PoseAt(IndependentGraph graph,int road,double distance,float lateral=0) {
            var f=graph.Road(road).Evaluate(distance);
            return new Pose((f.position+f.right*lateral+f.normal*VehicleGeometry.AdheredBodyHeight).Float,Quaternion.LookRotation(f.tangent.Float,f.normal.Float));
        }
        static GraphVehicleRoute Spawn(GraphRoadSurface surface,int road=0,double at=10,int history=0) {
            Check(GraphVehicleRoute.TryCreate(surface,surface.Graph.Cursor(road,at,history),PoseAt(surface.Graph,road,at),1,0,out var route),"spawn");return route;
        }
        static RaceObservation Observe(GraphVehicleRoute route,int road,double at,ref long tick,float lateral=0) {
            Check(route.Observe(PoseAt(route.Surface.Graph,road,at,lateral),route.PositionRevision,++tick),"physical observation road="+road+" at="+at);
            return new RaceObservation(route.RaceObservation);
        }
        static RaceSession Begin(GraphRoadSurface surface,GraphVehicleRoute[] routes,float finish) {
            var session=new RaceSession(surface,0,finish,routes.Length,routes.Length);
            session.Reset(routes.Select(r=>new RaceObservation(r.RaceObservation)).ToArray());session.Begin();session.Tick(3,routes.Select(r=>new RaceObservation(r.RaceObservation)).ToArray());return session;
        }
        static void Pair(GraphRoadSurface surface,bool offCentre=false) {
            var graph=surface.Graph;var routes=new[]{Spawn(surface),Spawn(surface)};long[] ticks={0,0};
            var seen=new[]{Observe(routes[0],0,11,ref ticks[0]),Observe(routes[1],0,11,ref ticks[1])};
            float finish=(float)(graph.CommonLength-10);
            var session=Begin(surface,routes,finish);int[][] paths={new[]{0,10,1,12,5},new[]{0,11,2,13,5}};
            for(int stage=0;stage<5;stage++) {
                double[] from={stage==0?11:0,stage==0?11:0};
                double[] to=paths.Select(path=>stage==4?graph.Road(5).Length-11:graph.Road(path[stage]).Length).ToArray();
                int steps=(int)Math.Ceiling(Math.Max(to[0]-from[0],to[1]-from[1])/2);
                for(int i=1;i<=steps;i++) {
                    for(int seat=0;seat<2;seat++)seen[seat]=Observe(routes[seat],paths[seat][stage],from[seat]+(to[seat]-from[seat])*i/steps,ref ticks[seat],offCentre && (stage==1||stage==3)?(seat==0?-3:3):0);
                    session.Tick(.01,seen);
                    Check(session.Racers.All(r=>!r.Finished),"no premature finish");
                    if(!offCentre)Check(Math.Abs(session.Racers[0].Progress-session.Racers[1].Progress)<.02,"equal common progress stage="+stage+" step="+i+" progress="+session.Racers[0].Progress+"/"+session.Racers[1].Progress+" gates="+session.Racers[0].ValidatedGateCount+"/"+session.Racers[1].ValidatedGateCount+" common="+routes[0].CommonProgress+"/"+routes[1].CommonProgress+" cursors="+routes[0].Cursor.roadId+"/"+routes[1].Cursor.roadId+" continuity="+routes[0].LastObservationContinuous+"/"+routes[1].LastObservationContinuous+" deltas="+routes[0].LastPhysicalDelta+"/"+routes[1].LastPhysicalDelta);
                }
                Check(session.Racers[0].ValidatedGateCount==session.Racers[1].ValidatedGateCount,"same frontier at seam");
            }
            double end=graph.Road(5).Length-10;
            seen[0]=Observe(routes[0],5,end+1,ref ticks[0]);seen[1]=Observe(routes[1],5,end+3,ref ticks[1]);
            double before=session.Elapsed;session.Tick(1,seen);
            Check(session.Racers[1].Place==1&&session.Racers[0].Place==2,"physical finish opposite roster order");
            Check(Math.Abs(session.Racers[0].FinishTime-before-.5)<.0002 && Math.Abs(session.Racers[1].FinishTime-before-.25)<.0002,"physical finish fractions");
            Check(session.Phase==RacePhase.FinishWindow,"last human starts grace");
            session.Paused=true;session.Tick(9,seen);Check(session.Elapsed==before+1,"pause freezes clock");session.Paused=false;
            session.Tick(9,seen);Check(session.Phase==RacePhase.Results&&Math.Abs(session.Elapsed-session.Racers[0].FinishTime-3)<1e-8,"exact results deadline");
            session.Reset(seen);Check(session.Racers.All(r=>r.ValidatedGateCount==0&&!r.Finished),"reset mid-track cannot mint gates");
        }
        static void Recovery(GraphRoadSurface surface) {
            var route=Spawn(surface,0,35);long tick=0;Observe(route,0,36,ref tick);
            var session=Begin(surface,new[]{route},(float)(surface.Graph.CommonLength-10));
            var seen=new[]{Observe(route,0,39,ref tick)};session.Tick(.1,seen);
            Check(route.TryCaptureSafePose(PoseAt(surface.Graph,0,39),out var safe),"safe before gate");
            Check(route.TryRestore(safe,2,++tick,out _),"restore");seen[0]=new RaceObservation(route.RaceObservation);session.Tick(.1,seen);
            seen[0]=Observe(route,0,41,ref tick);session.Tick(.1,seen);
            Check(session.Racers[0].ValidatedGateCount==0,"first restored crossing has no credit");
            seen[0]=Observe(route,0,42,ref tick);session.Tick(.1,seen);Check(session.Racers[0].ValidatedGateCount==0,"no legacy catch-up beyond gate");
            seen[0]=Observe(route,0,39,ref tick);session.Tick(.1,seen);seen[0]=Observe(route,0,41,ref tick);session.Tick(.1,seen);
            Check(session.Racers[0].ValidatedGateCount==1,"physical recross credits owed gate");
            session.Tick(.1,seen);Check(session.Racers[0].ValidatedGateCount==1,"duplicate snapshot no credit");
            var foreign=Spawn(surface,0,98);long otherTick=0;Observe(foreign,0,99,ref otherTick);seen[0]=Observe(foreign,0,101,ref otherTick);session.Tick(.1,seen);
            Check(session.Racers[0].ValidatedGateCount==1,"foreign binding cannot replace seat");
            var pose=PoseAt(surface.Graph,0,100);Check(!route.Observe(pose,2,tick+=2),"teleport rejected");seen[0]=new RaceObservation(route.RaceObservation);session.Tick(.1,seen);
            Check(session.Racers[0].ValidatedGateCount==1,"invalid cannot credit");
            var snapshot=foreign.RaceObservation;
            typeof(GraphRoadSurface).GetMethod("Initialize",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(surface,new object[]{surface.Graph,surface.BuildRevision+1});
            Check(!snapshot.Valid,"old accepted snapshot invalidated by build");
            session.Tick(.1,new[]{new RaceObservation(snapshot)});Check(session.Racers[0].ValidatedGateCount==1&&!session.Racers[0].Finished,"stale build cannot credit");
        }
        static void Walk(RaceSession session,GraphVehicleRoute route,int road,double from,double to,ref long tick) {
            var seen=new RaceObservation[1];int steps=Math.Max(1,(int)Math.Ceiling(Math.Abs(to-from)/2));
            for(int i=1;i<=steps;i++){seen[0]=Observe(route,road,from+(to-from)*i/steps,ref tick);session.Tick(.01,seen);}
        }
        static void ChangeBranch(GraphRoadSurface surface) {
            var graph=surface.Graph;var route=Spawn(surface);long tick=0;Observe(route,0,11,ref tick);
            var session=Begin(surface,new[]{route},(float)(graph.CommonLength-10));
            Walk(session,route,0,11,160,ref tick);Walk(session,route,11,0,80,ref tick);
            Check(route.CommittedBranch==0,"undivided split");Check(route.TryCaptureSafePose(PoseAt(graph,11,80),out var safe),"saved undivided split anchor");
            Walk(session,route,11,80,35,ref tick);
            session.Tick(.01,new[]{Observe(route,10,35,ref tick)});
            Walk(session,route,10,35,graph.Road(10).Length,ref tick);Walk(session,route,1,0,120,ref tick);
            var old=route.RaceObservation;int credited=session.Racers[0].ValidatedGateCount;
            Check(credited>session.GraphCourse.FirstBranchGate,"technical branch gained real gates");
            Check(route.TryRestore(safe,2,++tick,out _),"restore to shared anchor");
            Check(!old.Valid,"cached accepted snapshot invalid after recovery revision");
            session.Tick(.01,new[]{new RaceObservation(old)});Check(session.Racers[0].ValidatedGateCount==credited,"cached snapshot cannot mint credit");
            session.Tick(.01,new[]{new RaceObservation(route.RaceObservation)});
            Check(session.Racers[0].ValidatedGateCount==session.GraphCourse.FirstBranchGate,"recovery shared anchor discards branch-specific frontier");
            var seen=new[]{Observe(route,11,100,ref tick)};session.Tick(.01,seen);
            Check(route.CommittedBranch==2&&!seen[0].graph.Continuous,"first suppressed crossing selects physical speed arm without credit");
            Check(session.Racers[0].ValidatedGateCount==session.GraphCourse.FirstBranchGate,"suppressed divider crossing adds no gates");
            Walk(session,route,11,100,80,ref tick);
            Walk(session,route,11,80,graph.Road(11).Length,ref tick);Walk(session,route,2,0,graph.Road(2).Length,ref tick);
            Walk(session,route,13,0,graph.Road(13).Length,ref tick);Walk(session,route,5,0,graph.Road(5).Length-9,ref tick);
            Check(session.Racers[0].Finished,"new branch traverses its own gates and finishes after recovery gates="+session.Racers[0].ValidatedGateCount+"/"+session.GraphCourse.Gates.Count+" progress="+session.Racers[0].Progress);
        }
        static void ProvisionalCommit(GraphRoadSurface surface) {
            var graph=surface.Graph;int admitted=0,crossed=0;
            for(double from=62;from<=78;from+=4) {
                var route=Spawn(surface);long tick=0;Observe(route,0,11,ref tick);var session=Begin(surface,new[]{route},(float)(graph.CommonLength-10));
                Walk(session,route,0,11,160,ref tick);Walk(session,route,10,0,from,ref tick);
                var before=route.RaceObservation;int frontier=session.Racers[0].ValidatedGateCount;
                if(!route.Observe(PoseAt(graph,11,from+22),1,++tick) || route.CommittedBranch==0)continue;
                var after=route.RaceObservation;Check(before.Cursor.roadId==10&&after.Cursor.roadId==11&&after.Continuous,"certified sibling choice plus commitment");admitted++;
                double gate=session.GraphCourse.Gates[frontier].CommonProgress;
                bool crossing=before.CommonProgress<gate && after.CommonProgress>gate;
                session.Tick(.01,new[]{new RaceObservation(after)});
                if(crossing){crossed++;Check(session.Racers[0].ValidatedGateCount>frontier,"same-tick choice does not lose physical gate");}
            }
            Check(admitted>0&&crossed>0,"actual same-tick sibling commitment and gate exercised");
        }
        static void Deadline(GraphRoadSurface surface) {
            var graph=surface.Graph;var routes=new[]{Spawn(surface),Spawn(surface),Spawn(surface)};long[] ticks={0,0,0};var seen=new RaceObservation[3];
            for(int i=0;i<3;i++)seen[i]=Observe(routes[i],0,11,ref ticks[i]);
            float finish=(float)(graph.Road(5).progressStart+50);var session=new RaceSession(surface,0,finish,3,1);session.Reset(seen);session.Begin();session.Tick(3,seen);
            foreach(int road in new[]{0,10,1,12,5}) {
                double from=road==0?11:0,to=road==5?49:graph.Road(road).Length;int steps=(int)Math.Ceiling((to-from)/2);
                for(int step=1;step<=steps;step++) {for(int i=0;i<3;i++)seen[i]=Observe(routes[i],road,from+(to-from)*step/steps,ref ticks[i]);session.Tick(.01,seen);}
            }
            int frontier=session.Racers[2].ValidatedGateCount;double elapsed=session.Elapsed;
            for(int i=0;i<3;i++)seen[i]=Observe(routes[i],5,i==0?52:i==1?51:50.25,ref ticks[i]);
            session.Tick(10,seen);
            Check(session.Phase==RacePhase.Results,"coarse graph tick reaches exact deadline");
            Check(session.Racers[0].Finished&&session.Racers[1].Finished&&!session.Racers[2].Finished,"graph AI finish inside/after last-human deadline");
            Check(session.Racers[2].ValidatedGateCount==frontier,"late graph finish cannot mutate frontier");
            Check(Math.Abs(session.Racers[0].FinishTime-elapsed-10.0/3)<.001,"physical coarse finish interpolation");
            Check(Math.Abs(session.Elapsed-session.Racers[0].FinishTime-3)<1e-8,"graph deadline clock");
        }
        static void Case(IndependentGraph graph,string label) {
            var root=new GameObject("Graph race contracts "+label);
            try{var surface=IndependentTrackMesh.Build(root.transform,graph,71,null,null,(_,__)=>{});Physics.SyncTransforms();Pair(surface);Pair(surface,true);ChangeBranch(surface);ProvisionalCommit(surface);Deadline(surface);Recovery(surface);}
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        // Reviewed subset: all physical course/frontier/recovery contracts, without
        // unrelated vehicle/composition aggregators. Existing assertions stay intact.
        public static void RunCourseContracts() {
            string output=Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_RACE_QA_DIR");if(string.IsNullOrEmpty(output))throw new Exception("Evidence required");Directory.CreateDirectory(output);assertions=0;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);RaceSessionChecks.Run();
            Case(IndependentGraph.Create(new DVec(),new DVec(0,0,1),new DVec(0,1,0),new DVec(-1,0,0)),"normal");
            var axis=new DVec(.3,.7,.2).Unit();Case(IndependentGraph.Create(new DVec(123,45,-67),new DVec(0,0,1).Rotate(axis,.81),new DVec(0,1,0).Rotate(axis,.81),new DVec(-1,0,0).Rotate(axis,.81)),"rotated");
            File.WriteAllText(Path.Combine(output,"result.txt"),"GRAPH_RACE_COURSE_OK assertions="+assertions+" physical gates/recovery/foreign/rebuild/reverse\n");Debug.Log("GRAPH_RACE_COURSE_OK assertions="+assertions);
        }
        public static void Run() {
            string output=Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_RACE_QA_DIR");if(string.IsNullOrEmpty(output))throw new Exception("Evidence required");Directory.CreateDirectory(output);assertions=0;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            RaceSessionChecks.Run();
            Case(IndependentGraph.Create(new DVec(),new DVec(0,0,1),new DVec(0,1,0),new DVec(-1,0,0)),"normal");
            var axis=new DVec(.3,.7,.2).Unit();Case(IndependentGraph.Create(new DVec(123,45,-67),new DVec(0,0,1).Rotate(axis,.81),new DVec(0,1,0).Rotate(axis,.81),new DVec(-1,0,0).Rotate(axis,.81)),"rotated");
            Environment.SetEnvironmentVariable("STAR_RACING_GRAPH_VEHICLE_QA_DIR",Path.Combine(output,"vehicle"));GraphVehicleRouteChecks.Run();Environment.SetEnvironmentVariable("STAR_RACING_COMPOSITION_QA_DIR",Path.Combine(output,"composition"));TrackCompositionChecks.Run();
            File.WriteAllText(Path.Combine(output,"result.txt"),"GRAPH_RACE_OK assertions="+assertions+" cases=normal/rotated physicalFinish=opposite-roster recovery=recross legacy=unchanged\n");Debug.Log("GRAPH_RACE_OK assertions="+assertions);
        }
    }
}

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using StarRacingPrototype.Procedural;
namespace StarRacingPrototype {
    [InitializeOnLoad] public static class GraphRacePlayChecks {
        const string Key="StarRacing.GraphRacePlay";static int startup;static double began;
        public static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_RACE_PLAY_QA_DIR");
        static GraphRacePlayChecks(){EditorApplication.playModeStateChanged+=Changed;}
        public static void Run() {
            if(string.IsNullOrEmpty(Output))throw new Exception("Explicit evidence directory required");
            Directory.CreateDirectory(Output);SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",false);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);startup=0;EditorApplication.update+=Enter;
        }
        static void Enter(){if(++startup<15)return;EditorApplication.update-=Enter;EditorApplication.isPlaying=true;}
        static void Guard(){if(EditorApplication.timeSinceStartup-began>900)Fail(new Exception("Graph vehicle PlayerLoop timeout"));}
        static void Changed(PlayModeStateChange state) {
            if(!SessionState.GetBool(Key,false))return;
            if(state==PlayModeStateChange.EnteredPlayMode) {
                began=EditorApplication.timeSinceStartup;EditorApplication.update+=Guard;
                AudioListener.volume=0;Time.fixedDeltaTime=.01f;Time.maximumDeltaTime=.1f;Time.captureDeltaTime=.05f;
                new GameObject("Graph vehicle driving fixture").AddComponent<GraphRacePlayDriver>();
            }
            if(state==PlayModeStateChange.EnteredEditMode) {
                EditorApplication.update-=Guard;SessionState.SetBool(Key,false);
                EditorApplication.Exit(SessionState.GetBool(Key+"Failed",false)?1:0);
            }
        }
        public static void Fail(Exception e) {
            Debug.LogException(e);File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());
            SessionState.SetBool(Key+"Failed",true);EditorApplication.isPlaying=false;
        }
    }
    [DefaultExecutionOrder(-100)] public sealed class GraphRacePlayDriver:MonoBehaviour {
        TrackBuilder track;IndependentGraph graph;MagneticVehicle car;ReleaseBalance balance;RaceSession session;readonly RaceObservation[] seen=new RaceObservation[1];
        int branch=1,tick,revision;bool recovered,captured;Camera camera;StreamWriter trace;
        readonly HashSet<int> visited=new HashSet<int>();
        void Check(bool ok,string why){if(!ok)throw new Exception("Graph driving: "+why);}
        void Start(){try{
            gameObject.AddComponent<RacePhysicsStepper>();gameObject.AddComponent<GraphRacePlayObserver>().driver=this;
            var root=new GameObject("Physical independent track");track=root.AddComponent<TrackBuilder>();
            graph=IndependentGraph.Create(new DVec(),new DVec(0,0,1),new DVec(0,1,0),new DVec(-1,0,0));
            track.BuildIndependent(graph);Physics.SyncTransforms();
            int hulls=track.GetComponentsInChildren<MeshCollider>().Count(c=>c.sharedMesh!=null&&c.sharedMesh.name==RoadVolumeMesh.SupportName);
            Check(hulls==26876,"full production support inventory "+hulls);
            balance=ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);
            var light=new GameObject("QA light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.5f;light.transform.rotation=Quaternion.Euler(55,25,0);
            camera=new GameObject("QA camera").AddComponent<Camera>();camera.farClipPlane=3000;camera.backgroundColor=new Color(.04f,.06f,.12f);camera.clearFlags=CameraClearFlags.SolidColor;
            trace=new StreamWriter(Path.Combine(GraphRacePlayChecks.Output,"observations.csv"));
            trace.WriteLine("branch,tick,road,localDistance,commonProgress,committed,revision,valid,recovery,contacts,speed,x,y,z");
            Setup();
        }catch(Exception e){GraphRacePlayChecks.Fail(e);}}
        void Setup() {
            var obj=new GameObject("Actual graph vehicle "+branch);car=obj.AddComponent<MagneticVehicle>();
            car.InitializeIndependent(track,graph.Cursor(0,10),0,Color.cyan,balance,true);
            Physics.SyncTransforms();tick=0;revision=car.PositionRevision;recovered=captured=false;visited.Clear();
            Check(car.HasProvenCheckpoint,"initial graph checkpoint admitted");
            session=RaceDirector.CreateSessionForTrack(track,0,(float)(graph.Road(5).progressStart+40),1,1);
            seen[0]=RaceDirector.ObserveVehicle(car);session.Reset(seen);session.Begin();car.Hold(true);
        }
        GraphFrame Target(double ahead) {
            int choice=branch==2 && (car.GraphRoute.Cursor.roadId==0 ||
                (car.GraphRoute.Cursor.roadId==10 || car.GraphRoute.Cursor.roadId==11) && car.GraphRoute.Cursor.localPhysicalDistance<35)?1:branch;
            int[] path={0,choice==1?10:11,choice,choice==1?12:13,5};
            int road=car.GraphRoute.Cursor.roadId,index=Array.IndexOf(path,road);
            if(index<0)index=road==10||road==11?1:3;
            double at=car.GraphRoute.Cursor.localPhysicalDistance+ahead;
            while(index+1<path.Length && at>graph.Road(path[index]).Length){at-=graph.Road(path[index]).Length;index++;}
            return graph.Road(path[index]).Evaluate(at);
        }
        void FixedUpdate(){try {
            if(car==null)return;
            tick++;Check(tick<=20000,"route did not finish within 200 simulated seconds");
            if(tick!=500)car.PrepareProjection();
            var repeated=car.GraphRoute.Cursor;bool valid=car.GraphRoute.LastObservationContinuous;
            if(tick!=500)car.PrepareProjection();
            if(tick==501)Check(!car.GraphRoute.LastObservationContinuous && !car.GraphRoute.RequiresRecovery,"skipped physical tick suppresses credit without false recovery");
            Check(car.GraphRoute.Cursor.roadId==repeated.roadId && car.GraphRoute.Cursor.localPhysicalDistance==repeated.localPhysicalDistance &&
                car.GraphRoute.LastObservationContinuous==valid,"duplicate PrepareProjection cannot create a new physics observation");
            seen[0]=RaceDirector.ObserveVehicle(car);session.Tick(.01,seen);
            if(tick==500)Check(!seen[0].valid,"skipped projection cannot reuse cached credit");
            if(session.Racers[0].Finished){FinishCase();return;}
            if(!session.CanDrive(0)){car.Hold(true);car.SetInput(default);return;}
            car.SetRaceContext(true,session.Racers[0].Place,1,session.Elapsed);
            var cursor=car.GraphRoute.Cursor;visited.Add(cursor.roadId);
            float speed=Vector3.Dot(car.Body.linearVelocity,car.Body.rotation*Vector3.forward);
            float look=Mathf.Clamp(Mathf.Abs(speed)*.55f,12,35);var target=Target(look);
            Vector3 offset=Vector3.ProjectOnPlane(target.position.Float-car.Body.position,car.Frame.normal);
            float angle=Vector3.SignedAngle(car.Body.rotation*Vector3.forward,offset.normalized,car.Frame.normal)*Mathf.Deg2Rad;
            float pursuit=2*Mathf.Max(6,Mathf.Abs(speed))*Mathf.Sin(angle)/Mathf.Max(5,offset.magnitude);
            float desiredYaw=Mathf.Lerp(angle*6,pursuit,Mathf.SmoothStep(0,1,Mathf.InverseLerp(10,25,Mathf.Abs(speed))));
            float ratio=Mathf.Clamp01(Mathf.Abs(speed)/car.Drive.BaseSpeed),retention=1-(1-balance["highSpeedSteeringRetention"])*Mathf.Clamp01((ratio-.45f)/.55f);
            float authority=(1.18f+ratio*1.62f)*retention,yaw=Vector3.Dot(car.Body.angularVelocity,car.Frame.normal);
            float steer=Mathf.Clamp((desiredYaw+.18f*(desiredYaw-yaw))/authority,-1,1);
            float desired=branch==1?22:42;
            car.SetInput(new DrivingInput{throttle=speed<desired?1:0,brake=Mathf.Clamp01((speed-desired)/5),steer=steer});car.Hold(false);
            if(tick%10==0) {var p=car.Body.position;trace.WriteLine($"{branch},{tick},{cursor.roadId},{cursor.localPhysicalDistance:R},{car.GraphRoute.CommonProgress:R},{car.GraphRoute.CommittedBranch},{car.PositionRevision},{car.GraphRoute.LastObservationContinuous},{car.GraphRoute.RequiresRecovery},{car.HandlingSupportCount},{speed:R},{p.x:R},{p.y:R},{p.z:R}");trace.Flush();}
            Check(car.PositionRevision==revision && !car.IsFalling,"unexpected recovery branch="+branch+" tick="+tick+" road="+cursor.roadId+" distance="+cursor.localPhysicalDistance+" invalid="+car.GraphRoute.RequiresRecovery);
            if(cursor.roadId==branch && cursor.localPhysicalDistance>graph.Road(branch).Length*.45 && !recovered && session.GraphCourse.Gates[session.Racers[0].ValidatedGateCount].CommonProgress-car.GraphRoute.CommonProgress>5) {
                Check(car.HasProvenCheckpoint,"branch safe pose proven");
                int gates=session.Racers[0].ValidatedGateCount;car.ResetToCheckpoint();revision++;
                Check(session.Racers[0].ValidatedGateCount==gates,"recovery preserves frontier");
                Check(car.PositionRevision==revision && car.GraphRoute.Cursor.roadId==branch && car.GraphRoute.CommittedBranch==branch,"recovery restores current road and increments revision");
                Check(!car.GraphRoute.LastObservationContinuous,"recovery does not grant movement credit");recovered=true;
            }
            if(cursor.roadId==branch && cursor.localPhysicalDistance>graph.Road(branch).Length*.6 && !captured){Capture("branch-"+branch);captured=true;}
            if(session.Racers[0].Finished)FinishCase();
        }catch(Exception e){GraphRacePlayChecks.Fail(e);}}
        public void ObservePrepared(){if(car==null)return;try {
            Check(car.Body.collisionDetectionMode==CollisionDetectionMode.ContinuousDynamic,"vehicle CCD unchanged");
            Check(!car.IsFalling,"lost traversal enters explicit recovery");
        }catch(Exception e){GraphRacePlayChecks.Fail(e);}}
        void Capture(string name) {
            camera.transform.position=car.Body.position+car.Frame.normal*18-car.Frame.tangent*24;
            camera.transform.LookAt(car.Body.position+car.Frame.tangent*8,car.Frame.normal);
            var rt=new RenderTexture(1280,720,24);var prior=RenderTexture.active;Texture2D image=null;
            try {camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image=new Texture2D(1280,720,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(Path.Combine(GraphRacePlayChecks.Output,name+".png"),image.EncodeToPNG());
            }finally{camera.targetTexture=null;RenderTexture.active=prior;if(image!=null)Destroy(image);rt.Release();Destroy(rt);}
        }
        void FinishCase() {
            int[] expected={0,branch==1?10:11,branch,branch==1?12:13,5};
            Check(expected.All(id=>visited.Contains(id)) && !visited.Contains(branch==1?2:1),"both complete route history and no foreign road");
            if(branch==2)Check(visited.Contains(10) && visited.Contains(11),"actual controls switch provisional side before divider");
            Check(recovered,"branch recovery exercised");
            Check(session.Racers[0].ValidatedGateCount==session.GraphCourse.Gates.Count && session.Phase==RacePhase.FinishWindow,"all physical gates and finish");
            double finish=session.Racers[0].FinishTime;session.Paused=true;double elapsed=session.Elapsed;session.Tick(10,seen);
            Check(session.Elapsed==elapsed,"pause freezes grace");session.Paused=false;session.Tick(10,seen);
            Check(session.Phase==RacePhase.Results && Math.Abs(session.Elapsed-finish-3)<1e-8,"exact physical finish grace deadline");Capture("exit-"+branch);
            File.WriteAllText(Path.Combine(GraphRacePlayChecks.Output,"branch-"+branch+".txt"),$"GRAPH_RACE_DRIVE_OK branch={branch} simulatedSeconds={tick*.01:R} finishTime={session.Racers[0].FinishTime:R} gates={session.Racers[0].ValidatedGateCount} recovery=1 revision={revision} roadHistory={string.Join(",",visited.OrderBy(id=>id))} supportHulls=26876 input=ordinary-throttle-brake-steer\n");
            Debug.Log("GRAPH_RACE_DRIVE_OK branch="+branch+" ticks="+tick);
            DestroyImmediate(car.gameObject);car=null;
            if(branch==1){branch=2;Setup();}else{trace.Dispose();trace=null;File.WriteAllText(Path.Combine(GraphRacePlayChecks.Output,"result.txt"),"GRAPH_RACE_PLAY_OK branches=2 recoveries=2 supportHulls=26876 controls=ordinary CCD=8-substeps generator=inactive\n");EditorApplication.isPlaying=false;}
        }
        void OnDestroy(){trace?.Dispose();}
    }
    [DefaultExecutionOrder(999)] public sealed class GraphRacePlayObserver:MonoBehaviour {
        public GraphRacePlayDriver driver;void FixedUpdate(){driver.ObservePrepared();}
    }
}

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using StarRacingPrototype.Procedural;
namespace StarRacingPrototype {
 [InitializeOnLoad] public static class GraphCatalogPlayChecks {
  const string Key="StarRacing.CatalogPlay";static int startup;static double began;
  public static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_CATALOG_PLAY_QA_DIR");
  static GraphCatalogPlayChecks(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run(){if(string.IsNullOrEmpty(Output))throw new Exception("Evidence directory required");Directory.CreateDirectory(Output);SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",false);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);startup=0;EditorApplication.update+=Enter;}
  static void Enter(){if(++startup<15)return;EditorApplication.update-=Enter;EditorApplication.isPlaying=true;}
  static void Guard(){if(EditorApplication.timeSinceStartup-began>900)Fail(new Exception("Catalog PlayerLoop timeout"));}
  static void Changed(PlayModeStateChange state){if(!SessionState.GetBool(Key,false))return;if(state==PlayModeStateChange.EnteredPlayMode){began=EditorApplication.timeSinceStartup;EditorApplication.update+=Guard;AudioListener.volume=0;Time.fixedDeltaTime=.01f;Time.maximumDeltaTime=.1f;Time.captureDeltaTime=.05f;new GameObject("Combined catalog physical fixture").AddComponent<GraphCatalogPlayDriver>();}if(state==PlayModeStateChange.EnteredEditMode){EditorApplication.update-=Guard;SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",false)?1:0);}}
  public static void Fail(Exception e){Debug.LogException(e);File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());SessionState.SetBool(Key+"Failed",true);EditorApplication.isPlaying=false;}
 }
 [DefaultExecutionOrder(-100)] public sealed class GraphCatalogPlayDriver:MonoBehaviour {
  TrackBuilder track;IndependentGraph graph;ReleaseBalance balance;MagneticVehicle car;AiDriver driver;GraphAiWorldSnapshot world;RaceSession session;RaceObservation[] seen;DrivingInput command;int scenario=1,tick,revision,supportHulls,flightTicks,landings;bool manual,wasFlying,challenged;GraphVehicleRoute[] shadows;bool captured;StreamWriter trace;Camera camera;float peakHeight;int branch;bool inverted,banked;HashSet<string> captures=new HashSet<string>();HashSet<string> flights=new HashSet<string>();
  void Check(bool ok,string why){if(!ok)throw new Exception("Combined catalog physical: "+why);}
  void Start(){try {
   gameObject.AddComponent<RacePhysicsStepper>();track=new GameObject("Nonplanar production support").AddComponent<TrackBuilder>();graph=GraphCommonCatalogChecks.Geometry();Check(GraphCourseSafety.Validate(graph).Length==0,"geometric whole-course safety");track.BuildIndependentCommonCatalog(graph);Physics.SyncTransforms();
   supportHulls=track.GetComponentsInChildren<MeshCollider>().Count(c=>c.sharedMesh!=null&&c.sharedMesh.name==RoadVolumeMesh.SupportName);Check(supportHulls>40000,"full production support hulls include elevations");
   balance=ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);
   var light=new GameObject("QA light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.5f;light.transform.rotation=Quaternion.Euler(55,25,0);
   camera=new GameObject("QA camera").AddComponent<Camera>();camera.farClipPlane=3000;camera.backgroundColor=new Color(.04f,.06f,.12f);camera.clearFlags=CameraClearFlags.SolidColor;
   trace=new StreamWriter(Path.Combine(GraphCatalogPlayChecks.Output,"observations.csv"));trace.WriteLine("case,tick,road,local,progress,speed,desired,steer,throttle,brake,nitro,drift,valid,continuous,revision,gates,x,y,z,flight,jump,grounded,vy");Setup();
  }catch(Exception e){car=null;GraphCatalogPlayChecks.Fail(e);}}
  void Setup() {
   if(scenario==3){graph=GraphCommonCatalogChecks.Geometry("full");track.BuildIndependentCommonCatalog(graph);Physics.SyncTransforms();supportHulls=track.GetComponentsInChildren<MeshCollider>().Count(c=>c.sharedMesh!=null&&c.sharedMesh.name==RoadVolumeMesh.SupportName);}
   int road=0;double at=10;
   car=new GameObject("Actual common jump car "+scenario).AddComponent<MagneticVehicle>();car.InitializeIndependent(track,graph.Cursor(road,at,road==5?1:0),0,Color.cyan,balance,true);

   revision=car.PositionRevision;driver=new AiDriver(0,DriverProfile.Racer,77,balance);world=new GraphAiWorldSnapshot(track.GraphSurface,1,balance);seen=new[]{RaceDirector.ObserveVehicle(car)};
   session=RaceDirector.CreateSessionForTrack(track,0,(float)(graph.Road(5).progressEnd-5),1,1);session.Reset(seen);session.Begin();tick=0;flightTicks=landings=0;branch=0;inverted=banked=false;captures.Clear();flights.Clear();wasFlying=false;captured=challenged=false;peakHeight=0;command=default;manual=false;Physics.SyncTransforms();shadows=new GraphVehicleRoute[5];
   for(int i=0;i<shadows.Length;i++)Check(GraphVehicleRoute.TryCreate(track.GraphSurface,car.GraphRoute.Cursor,new Pose(car.Body.position,car.Body.rotation),car.PositionRevision,(long)Math.Round(Time.fixedTimeAsDouble/Time.fixedDeltaTime),out shadows[i]),"shadow has actual supported anchor");
  }
  void FixedUpdate(){try {
   if(car==null)return;tick++;Check(tick<=36000,"did not finish case="+scenario+" within360 simulated seconds");car.PrepareProjection();Challenge();seen[0]=RaceDirector.ObserveVehicle(car);session.Tick(.01,seen);world.Capture(new[]{car},new[]{driver},session.Elapsed);
   if(session.Phase==RacePhase.Countdown){car.Hold(true);car.SetInput(default);return;}
   car.SetRaceContext(true,1,1,session.Elapsed);float speed=Vector3.Dot(car.Body.linearVelocity,car.transform.forward);
   if(tick%10==0||!world[0].Valid){command=driver.StepGraph(world,.1f);manual=false;}
   var before=car.GraphRoute.Cursor;
   if(scenario==2) {
    // Ordinary target steering deliberately chooses the speed arm and optional
    // partial ramps. It never writes body motion or traversal authority.
    GraphJump partial=graph.jumps.FirstOrDefault(j=>!j.mandatory&&j.roadId==before.roadId&&before.localPhysicalDistance<j.ballisticLanding);
    bool chooseSpeed=before.roadId==0&&before.localPhysicalDistance>graph.Road(0).Length-100 || before.roadId==10||before.roadId==11;
    if(partial!=null || chooseSpeed) {manual=true;
     var target=chooseSpeed?world.Navigation.Frame(before,2,20).position.Float:car.Frame.position+car.Frame.right*(float)-partial.lateralCenter+car.Frame.tangent*20;
     float angle=Vector3.SignedAngle(car.transform.forward,Vector3.ProjectOnPlane(target-car.Body.position,car.Frame.normal).normalized,car.Frame.normal);
     command=new DrivingInput{throttle=1,brake=Mathf.Clamp01((speed-42)/5),steer=Mathf.Clamp(angle/25,-1,1)};
    }
   }
   car.SetInput(manual?command:driver.ApplyGraphControl(car,command,Time.fixedDeltaTime));car.Hold(false);
   var cursor=car.GraphRoute.Cursor;if(car.GraphRoute.IsFlying){flightTicks++;Check(world[0].Valid,"flight retains fresh current observation");if(!wasFlying){Check(car.GraphRoute.FlightJump.roadId==cursor.roadId,"launch bound to exact common road");flights.Add(car.GraphRoute.FlightJump.id);}}else if(wasFlying)landings++;wasFlying=car.GraphRoute.IsFlying;peakHeight=Mathf.Max(peakHeight,car.Body.position.y);
   if(cursor.roadId==1||cursor.roadId==2)branch=cursor.roadId;
   if(cursor.roadId==0&&Vector3.Dot(car.Frame.normal,Vector3.up)<-.98f){inverted=true;if(captures.Add("loop"))Capture("loop-case-"+scenario);}
   if(cursor.roadId==5&&Mathf.Abs(Vector3.Dot(car.Frame.right,Vector3.up))>.98f){banked=true;if(captures.Add("bank"))Capture("bank-case-"+scenario);}
   Check(car.PositionRevision==revision&&!car.IsFalling,"unexpected recovery case="+scenario+" tick="+tick+" road="+cursor.roadId+" local="+cursor.localPhysicalDistance+" invalid="+car.GraphRoute.RequiresRecovery);
   if(tick%10==0){var p=car.Body.position;trace.WriteLine($"{scenario},{tick},{cursor.roadId},{cursor.localPhysicalDistance:R},{car.GraphRoute.CommonProgress:R},{speed:R},{driver.DesiredSpeed:R},{command.steer:R},{command.throttle:R},{command.brake:R},{command.nitro},{command.drift},{world[0].Valid},{car.GraphRoute.LastObservationContinuous},{car.PositionRevision},{session.Racers[0].ValidatedGateCount},{p.x:R},{p.y:R},{p.z:R},{car.GraphRoute.IsFlying},{car.ActiveJumpId},{car.Telemetry.grounded},{car.Body.linearVelocity.y:R}");trace.Flush();}
   if(!captured&&(car.GraphRoute.IsFlying&&!car.Telemetry.grounded&&cursor.localPhysicalDistance>car.GraphRoute.FlightJump.gapStart+5&&cursor.localPhysicalDistance<car.GraphRoute.FlightJump.gapEnd-2 || scenario==3&&cursor.localPhysicalDistance>740&&cursor.localPhysicalDistance<770)){Capture("catalog-case-"+scenario);captured=true;}
   bool done=session.Racers[0].Finished;
   if(done)Finish();
  }catch(Exception e){car=null;GraphCatalogPlayChecks.Fail(e);}}
  void Challenge() {
   if(challenged)return;var pose=new Pose(car.Body.position,car.Body.rotation);var velocity=car.Body.linearVelocity;long now=(long)Math.Round(Time.fixedTimeAsDouble/Time.fixedDeltaTime);
   if(!car.GraphRoute.IsFlying){for(int i=0;i<shadows.Length;i++)shadows[i].Observe(pose,car.PositionRevision,now,velocity,.01f);return;}
   Check(shadows[0].Observe(pose,car.PositionRevision,now,velocity,.01f)&&shadows[0].IsFlying,"same immutable physical motion independently admits actual launch");
   Check(!shadows[1].Observe(new Pose(pose.position+car.Frame.tangent*20,pose.rotation),car.PositionRevision,now,velocity,.01f)&&!shadows[1].RaceObservation.Valid,"unsupported20m scalar catchup denied");
   Check(!shadows[2].Observe(pose,car.PositionRevision+1,now,velocity,.01f)&&!shadows[2].RaceObservation.Valid,"stale position revision flight denied");
   Check(!shadows[3].Observe(pose,car.PositionRevision,now+1,velocity,.01f)&&!shadows[3].RaceObservation.Valid,"skipped tick cannot launch");
   Check(!shadows[4].Observe(pose,car.PositionRevision,now)&&!shadows[4].RaceObservation.Valid,"pose-only metadata cannot launch");
   Check(!car.GraphRoute.TryCaptureSafePose(pose,out _),"no airborne recovery pose admitted");challenged=true;
   File.WriteAllText(Path.Combine(GraphCatalogPlayChecks.Output,"flight-negative-"+scenario+".txt"),"FLIGHT_NEGATIVE_OK realSupportLaunch=1 deniedTeleport=1 deniedRevision=1 deniedTickGap=1 deniedPoseOnly=1 deniedAirborneSafePose=1 noBodyWrites=1\n");
  }
  void Capture(string name) {
   var f=car.Frame;camera.transform.position=car.Body.position+f.normal*14+f.right*22-f.tangent*32;camera.transform.LookAt(car.Body.position+f.tangent*15,f.normal);
   var rt=new RenderTexture(1280,720,24);var prior=RenderTexture.active;Texture2D image=null;
   try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(Path.Combine(GraphCatalogPlayChecks.Output,name+".png"),image.EncodeToPNG());}
   finally{camera.targetTexture=null;RenderTexture.active=prior;if(image!=null)Destroy(image);rt.Release();Destroy(rt);}
  }
  void Finish(){
   Check(peakHeight>120,"actual body traverses vertical loop");
   Check(captured&&inverted&&banked,"actual flight, inverted loop and vertical banking rendered");
   Check(branch==(scenario==2?2:1),"both independently selected roads exercised");
   if(scenario!=3){Check(challenged&&flightTicks>10,"actual flight admission");Check(landings==(scenario==2?4:2)&&flights.Count==(scenario==2?4:2),"ordinary selected optional/mandatory flights land on physical road");}
   else Check(flightTicks==0&&landings==0,"full bridge disables flight");
   Check(session.Racers[0].ValidatedGateCount==session.GraphCourse.Gates.Count,"all combined physical gates");
   File.WriteAllText(Path.Combine(GraphCatalogPlayChecks.Output,"case-"+scenario+".txt"),$"GRAPH_CATALOG_DRIVE_OK case={scenario} seconds={tick*.01:R} peakHeight={peakHeight:R} flightTicks={flightTicks} landings={landings} branch={branch} inverted={inverted} banked={banked} gates={session.Racers[0].ValidatedGateCount}/{session.GraphCourse.Gates.Count} controls=ordinary supportHulls={supportHulls} graph={graph.Identity}\n");
   Debug.Log("GRAPH_CATALOG_DRIVE_OK case="+scenario);DestroyImmediate(car.gameObject);car=null;
   if(++scenario<=3)Setup();else{trace.Dispose();trace=null;File.WriteAllText(Path.Combine(GraphCatalogPlayChecks.Output,"result.txt"),"GRAPH_CATALOG_PLAY_OK combined loop banking hill jumps fullBridge controls=ordinary CCD=8-substeps generator=inactive\n");EditorApplication.isPlaying=false;}
  }
  void OnDestroy(){trace?.Dispose();}
 }
}

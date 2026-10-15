using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using StarRacingPrototype.Procedural;
namespace StarRacingPrototype {
 [InitializeOnLoad] public static class GraphCommonVerticalPlayChecks {
  const string Key="StarRacing.CommonVerticalPlay";static int startup;static double began;
  public static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_VERTICAL_PLAY_QA_DIR");
  static GraphCommonVerticalPlayChecks(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run(){if(string.IsNullOrEmpty(Output))throw new Exception("Evidence directory required");Directory.CreateDirectory(Output);SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",false);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);startup=0;EditorApplication.update+=Enter;}
  static void Enter(){if(++startup<15)return;EditorApplication.update-=Enter;EditorApplication.isPlaying=true;}
  static void Guard(){if(EditorApplication.timeSinceStartup-began>900)Fail(new Exception("Vertical PlayerLoop timeout"));}
  static void Changed(PlayModeStateChange state){if(!SessionState.GetBool(Key,false))return;if(state==PlayModeStateChange.EnteredPlayMode){began=EditorApplication.timeSinceStartup;EditorApplication.update+=Guard;AudioListener.volume=0;Time.fixedDeltaTime=.01f;Time.maximumDeltaTime=.1f;Time.captureDeltaTime=.05f;new GameObject("Common vertical physical fixture").AddComponent<GraphCommonVerticalPlayDriver>();}if(state==PlayModeStateChange.EnteredEditMode){EditorApplication.update-=Guard;SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",false)?1:0);}}
  public static void Fail(Exception e){Debug.LogException(e);File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());SessionState.SetBool(Key+"Failed",true);EditorApplication.isPlaying=false;}
 }
 [DefaultExecutionOrder(-100)] public sealed class GraphCommonVerticalPlayDriver:MonoBehaviour {
  TrackBuilder track;IndependentGraph graph;ReleaseBalance balance;MagneticVehicle car;AiDriver driver;GraphAiWorldSnapshot world;RaceSession session;RaceObservation[] seen;DrivingInput command;int scenario=1,tick,revision,supportHulls;bool recovered,captured;StreamWriter trace;Camera camera;double startDistance;float peakHeight;
  void Check(bool ok,string why){if(!ok)throw new Exception("Common vertical physical: "+why);}
  void Start(){try {
   gameObject.AddComponent<RacePhysicsStepper>();track=new GameObject("Nonplanar production support").AddComponent<TrackBuilder>();graph=GraphCommonVerticalChecks.Geometry();track.BuildIndependentCommonVertical(graph);Physics.SyncTransforms();
   supportHulls=track.GetComponentsInChildren<MeshCollider>().Count(c=>c.sharedMesh!=null&&c.sharedMesh.name==RoadVolumeMesh.SupportName);Check(supportHulls>26876,"full production support hulls include elevations");
   balance=ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);
   var light=new GameObject("QA light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.5f;light.transform.rotation=Quaternion.Euler(55,25,0);
   camera=new GameObject("QA camera").AddComponent<Camera>();camera.farClipPlane=3000;camera.backgroundColor=new Color(.04f,.06f,.12f);camera.clearFlags=CameraClearFlags.SolidColor;
   trace=new StreamWriter(Path.Combine(GraphCommonVerticalPlayChecks.Output,"observations.csv"));trace.WriteLine("case,tick,road,local,progress,speed,desired,steer,throttle,brake,nitro,drift,valid,continuous,revision,gates,x,y,z");Setup();
  }catch(Exception e){car=null;GraphCommonVerticalPlayChecks.Fail(e);}}
  void Setup() {
   int road=scenario==2?5:0;double at=scenario==3?300:10;
   car=new GameObject("Actual vertical car "+scenario).AddComponent<MagneticVehicle>();car.InitializeIndependent(track,graph.Cursor(road,at,road==5?1:0),0,Color.cyan,balance,true);
   // One initial setup pose faces reverse; the driving loop writes controls only.
   if(scenario==3){var f=graph.Evaluate(car.GraphRoute.Cursor);var rotation=Quaternion.LookRotation(-f.tangent.Float,f.normal.Float);car.transform.rotation=rotation;car.Body.rotation=rotation;car.ResetPresentation();}
   revision=car.PositionRevision;driver=new AiDriver(0,DriverProfile.Racer,77,balance);world=new GraphAiWorldSnapshot(track.GraphSurface,1,balance);seen=new[]{RaceDirector.ObserveVehicle(car)};
   session=RaceDirector.CreateSessionForTrack(track,0,(float)(graph.Road(5).progressStart+300),1,1);session.Reset(seen);session.Begin();tick=0;recovered=captured=false;peakHeight=0;startDistance=at;command=default;Physics.SyncTransforms();
  }
  void FixedUpdate(){try {
   if(car==null)return;tick++;Check(tick<=24000,"did not finish case="+scenario+" within240 simulated seconds");car.PrepareProjection();seen[0]=RaceDirector.ObserveVehicle(car);session.Tick(.01,seen);world.Capture(new[]{car},new[]{driver},session.Elapsed);
   if(session.Phase==RacePhase.Countdown){car.Hold(true);car.SetInput(default);return;}
   car.SetRaceContext(true,1,1,session.Elapsed);float speed=Vector3.Dot(car.Body.linearVelocity,car.transform.forward);
   // Existing neutral straight assist deliberately aligns heading forward.
   // Ordinary held drift intent disables that assist; no steering/drift bonus
   // is requested at zero steer. Preserve production handling unchanged.
   if(scenario==3)command=new DrivingInput{throttle=speed<15?1:0,brake=Mathf.Clamp01((speed-15)/5),drift=true};
   else if(tick%10==0||!world[0].Valid)command=driver.StepGraph(world,.1f);
   car.SetInput(scenario==3?command:driver.ApplyGraphControl(car,command,Time.fixedDeltaTime));car.Hold(false);
   var cursor=car.GraphRoute.Cursor;if(scenario==3)Check(cursor.roadId==0&&Vector3.Dot(car.transform.forward,car.Frame.tangent)<-.99f,"reverse setup orientation and common road retained");peakHeight=Mathf.Max(peakHeight,car.Body.position.y);
   Check(car.PositionRevision==revision&&!car.IsFalling,"unexpected recovery case="+scenario+" tick="+tick+" road="+cursor.roadId+" local="+cursor.localPhysicalDistance+" invalid="+car.GraphRoute.RequiresRecovery);
   if(tick%10==0){var p=car.Body.position;trace.WriteLine($"{scenario},{tick},{cursor.roadId},{cursor.localPhysicalDistance:R},{car.GraphRoute.CommonProgress:R},{speed:R},{driver.DesiredSpeed:R},{command.steer:R},{command.throttle:R},{command.brake:R},{command.nitro},{command.drift},{world[0].Valid},{car.GraphRoute.LastObservationContinuous},{car.PositionRevision},{session.Racers[0].ValidatedGateCount},{p.x:R},{p.y:R},{p.z:R}");trace.Flush();}
   if(!captured&&(cursor.roadId==0||cursor.roadId==5)&&cursor.localPhysicalDistance>150&&cursor.localPhysicalDistance<170){Capture("vertical-case-"+scenario);captured=true;}
   if(scenario==2&&!recovered&&cursor.localPhysicalDistance>150){Check(car.HasProvenCheckpoint,"elevated safe pose proven");car.ResetToCheckpoint();revision++;Check(car.PositionRevision==revision&&car.GraphRoute.Cursor.roadId==5&&car.GraphRoute.CommittedBranch==1,"recovery preserves elevated road provenance");recovered=true;}
   bool done=scenario==1?session.Racers[0].Finished:scenario==2?cursor.localPhysicalDistance>300:cursor.localPhysicalDistance<20;
   if(done)Finish();
  }catch(Exception e){car=null;GraphCommonVerticalPlayChecks.Fail(e);}}
  void Capture(string name) {
   var f=car.Frame;camera.transform.position=car.Body.position+f.normal*14+f.right*22-f.tangent*32;camera.transform.LookAt(car.Body.position+f.tangent*15,f.normal);
   var rt=new RenderTexture(1280,720,24);var prior=RenderTexture.active;Texture2D image=null;
   try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(Path.Combine(GraphCommonVerticalPlayChecks.Output,name+".png"),image.EncodeToPNG());}
   finally{camera.targetTexture=null;RenderTexture.active=prior;if(image!=null)Destroy(image);rt.Release();Destroy(rt);}
  }
  void Finish(){Check(peakHeight>4.2f,"actual body passes elevated crest");Check(captured,"actual rendered crest captured");if(scenario==1)Check(session.Racers[0].ValidatedGateCount==session.GraphCourse.Gates.Count,"full AI route completes every elevated physical gate");if(scenario==2)Check(recovered,"actual common-road recovery exercised");if(scenario==3)Check(car.GraphRoute.Cursor.localPhysicalDistance<startDistance-100&&car.GraphRoute.CommittedBranch==0,"ordinary controls drive reverse on common elevation");
   File.WriteAllText(Path.Combine(GraphCommonVerticalPlayChecks.Output,"case-"+scenario+".txt"),$"COMMON_VERTICAL_DRIVE_OK case={scenario} seconds={tick*.01:R} peakHeight={peakHeight:R} recovery={recovered} gates={session.Racers[0].ValidatedGateCount}/{session.GraphCourse.Gates.Count} controls=ordinary supportHulls={supportHulls} graph={graph.Identity}\n");Debug.Log("COMMON_VERTICAL_DRIVE_OK case="+scenario);DestroyImmediate(car.gameObject);car=null;if(++scenario<=3)Setup();else{trace.Dispose();trace=null;File.WriteAllText(Path.Combine(GraphCommonVerticalPlayChecks.Output,"result.txt"),"COMMON_VERTICAL_PLAY_OK aiFullRoute=1 commonRecovery=1 reverse=1 controls=ordinary CCD=8-substeps generator=inactive\n");EditorApplication.isPlaying=false;}}
  void OnDestroy(){trace?.Dispose();}
 }
}

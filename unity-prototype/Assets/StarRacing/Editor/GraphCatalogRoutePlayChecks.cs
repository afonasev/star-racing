using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using StarRacingPrototype.Procedural;
namespace StarRacingPrototype {
 [InitializeOnLoad] public static class GraphCatalogRoutePlayChecks {
  const string Key="StarRacing.CatalogRoutes";static int startup;static double began;
  public static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_CATALOG_ROUTE_QA_DIR");
  static GraphCatalogRoutePlayChecks(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run()=>RunFrom(1);
  public static void RunTraffic()=>RunFrom(7);
  static void RunFrom(int first){SessionState.SetInt(Key+"First",first);if(string.IsNullOrEmpty(Output))throw new Exception("Evidence directory required");Directory.CreateDirectory(Output);SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",false);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);startup=0;EditorApplication.update+=Enter;}
  static void Enter(){if(++startup<15)return;EditorApplication.update-=Enter;EditorApplication.isPlaying=true;}
  static void Guard(){if(EditorApplication.timeSinceStartup-began>900)Fail(new Exception("Catalog routes PlayerLoop timeout"));}
  static void Changed(PlayModeStateChange state){if(!SessionState.GetBool(Key,false))return;if(state==PlayModeStateChange.EnteredPlayMode){began=EditorApplication.timeSinceStartup;EditorApplication.update+=Guard;AudioListener.volume=0;Time.fixedDeltaTime=.01f;Time.maximumDeltaTime=.1f;Time.captureDeltaTime=.05f;new GameObject("Actual catalog route scenarios").AddComponent<GraphCatalogRoutePlayDriver>();}if(state==PlayModeStateChange.EnteredEditMode){EditorApplication.update-=Guard;SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",false)?1:0);}}
  public static void Fail(Exception e){Debug.LogException(e);File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());SessionState.SetBool(Key+"Failed",true);EditorApplication.isPlaying=false;}
 }
 [DefaultExecutionOrder(-100)] public sealed class GraphCatalogRoutePlayDriver:MonoBehaviour {
  TrackBuilder track;IndependentGraph graph;ReleaseBalance balance;MagneticVehicle[] cars;AiDriver[] drivers;GraphAiWorldSnapshot world;RaceSession session;RaceObservation[] seen;DrivingInput[] commands;bool[] manual;int[] revisions;int scenario=1,tick,yields,selected;double entryAt=-1,mergeAt=-1;float entrySpeed,mergeSpeed;StreamWriter trace;bool recovered;
  void Check(bool ok,string why){if(!ok)throw new Exception("Catalog routes: "+why);}
  void Start(){try{scenario=SessionState.GetInt("StarRacing.CatalogRoutesFirst",1);gameObject.AddComponent<RacePhysicsStepper>();track=new GameObject("Full catalog production inventory").AddComponent<TrackBuilder>();graph=GraphCommonCatalogChecks.Geometry();track.BuildIndependentCommonCatalog(graph);Physics.SyncTransforms();Check(track.GetComponentsInChildren<MeshCollider>().Count(c=>c.sharedMesh!=null&&c.sharedMesh.name==RoadVolumeMesh.SupportName)==71264,"complete certified support inventory");balance=ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);trace=new StreamWriter(Path.Combine(GraphCatalogRoutePlayChecks.Output,"observations.csv"));trace.WriteLine("case,tick,id,road,local,progress,speed,desired,steer,throttle,brake,drift,choice,yield,valid,revision,gates");Setup();}catch(Exception e){cars=null;GraphCatalogRoutePlayChecks.Fail(e);}}
  void Setup(){int count=scenario==2?2:scenario==7?5:1;cars=new MagneticVehicle[count];drivers=new AiDriver[count];seen=new RaceObservation[count];commands=new DrivingInput[count];manual=new bool[count];revisions=new int[count];tick=yields=selected=0;entryAt=mergeAt=-1;entrySpeed=mergeSpeed=0;recovered=false;
   for(int i=0;i<count;i++){
    int road=scenario==2?(i==0?12:13):scenario==7&&i>0?1:0;
    double at=scenario==1?1900:scenario==2?5:scenario==7&&i>0?50+i*60:graph.Road(0).Length-350;
    cars[i]=new GameObject("Route car "+i).AddComponent<MagneticVehicle>();cars[i].InitializeIndependent(track,graph.Cursor(road,at),i,i==0?Color.cyan:Color.magenta,balance,true);drivers[i]=new AiDriver(i,DriverProfile.Racer,(uint)(77+i*31),balance);revisions[i]=cars[i].PositionRevision;
    if(scenario==1){var f=cars[i].Frame;var q=Quaternion.LookRotation(-f.tangent,f.normal);cars[i].Body.rotation=q;cars[i].transform.rotation=q;cars[i].ResetPresentation();}
    seen[i]=RaceDirector.ObserveVehicle(cars[i]);
   }
   Physics.SyncTransforms();world=new GraphAiWorldSnapshot(track.GraphSurface,count,balance);session=RaceDirector.CreateSessionForTrack(track,0,(float)(graph.Road(5).progressEnd-5),count,count);session.Reset(seen);session.Begin();
  }
  DrivingInput Aim(MagneticVehicle car,Vector3 target,float desired,bool reverse=false){
   float speed=Vector3.Dot(car.Body.linearVelocity,car.transform.forward);var offset=Vector3.ProjectOnPlane(target-car.Body.position,car.Frame.normal);float angle=Vector3.SignedAngle(car.transform.forward,offset.normalized,car.Frame.normal)*Mathf.Deg2Rad;
   float pursuit=2*Mathf.Max(6,Mathf.Abs(speed))*Mathf.Sin(angle)/Mathf.Max(5,offset.magnitude);float yaw=Mathf.Lerp(angle*6,pursuit,Mathf.SmoothStep(0,1,Mathf.InverseLerp(10,25,Mathf.Abs(speed))));float ratio=Mathf.Clamp01(Mathf.Abs(speed)/car.Drive.BaseSpeed),retention=1-(1-balance["highSpeedSteeringRetention"])*Mathf.Clamp01((ratio-.45f)/.55f),authority=(1.18f+ratio*1.62f)*retention;
   return new DrivingInput{throttle=speed<desired?1:0,brake=Mathf.Clamp01((speed-desired)/5),steer=Mathf.Clamp((yaw+.18f*(yaw-Vector3.Dot(car.Body.angularVelocity,car.Frame.normal)))/authority,-1,1),drift=reverse};
  }
  void FixedUpdate(){try{if(cars==null)return;tick++;Check(tick<=24000,"240s scenario deadline case="+scenario);foreach(var car in cars)car.PrepareProjection();for(int i=0;i<cars.Length;i++)seen[i]=RaceDirector.ObserveVehicle(cars[i]);session.Tick(.01,seen);world.Capture(cars,drivers,session.Elapsed);
   for(int i=0;i<cars.Length;i++){
    var car=cars[i];var c=car.GraphRoute.Cursor;float speed=Vector3.Dot(car.Body.linearVelocity,car.transform.forward);
    if(session.Phase==RacePhase.Countdown){car.Hold(true);car.SetInput(default);continue;}
    car.SetRaceContext(true,1,cars.Length,session.Elapsed);
    if(scenario==1||scenario==7&&i>0)manual[i]=true;
    if(scenario==1)commands[i]=Aim(car,graph.Road(0).Evaluate(c.localPhysicalDistance-20).position.Float,15,true);
    else if(scenario==7&&i>0)commands[i]=Aim(car,world.Navigation.Frame(c,1,12).position.Float,10);
    else {
     if(tick%10==0||!world[i].Valid){commands[i]=drivers[i].StepGraph(world,.1f);manual[i]=false;}
     if(scenario>=3&&scenario<=6&&(c.roadId==0||c.roadId==10||c.roadId==11)){
      manual[i]=true;int branch=scenario%2==1?1:2;float desired=scenario<=4?25:75;
      commands[i]=Aim(car,world.Navigation.Frame(c,branch,Mathf.Clamp(speed*.55f,12,35)).position.Float,desired);
     }
    }
    car.SetInput(manual[i]?commands[i]:drivers[i].ApplyGraphControl(car,commands[i],Time.fixedDeltaTime));car.Hold(false);if(drivers[i].MergeYieldTo>=0)yields++;
    Check(car.PositionRevision==revisions[i]&&!car.IsFalling,"unexpected recovery case="+scenario+" tick="+tick+" id="+i+" road="+c.roadId+" local="+c.localPhysicalDistance+" needs="+car.GraphRoute.RequiresRecovery);
    if(i==0&&(c.roadId==1||c.roadId==2)&&entryAt<0){entryAt=tick*.01;entrySpeed=speed;selected=c.roadId;}
    if(i==0&&c.roadId==5&&mergeAt<0){mergeAt=tick*.01;mergeSpeed=speed;}
    if(tick%10==0){trace.WriteLine($"{scenario},{tick},{i},{c.roadId},{c.localPhysicalDistance:R},{car.GraphRoute.CommonProgress:R},{speed:R},{drivers[i].DesiredSpeed:R},{commands[i].steer:R},{commands[i].throttle:R},{commands[i].brake:R},{commands[i].drift},{drivers[i].GraphChoice},{drivers[i].MergeYieldTo},{world[i].Valid},{car.PositionRevision},{session.Racers[i].ValidatedGateCount}");trace.Flush();}
   }
   bool done=scenario==1?cars[0].GraphRoute.Cursor.localPhysicalDistance<1200:scenario==2?cars.All(c=>c.GraphRoute.Cursor.roadId==5&&c.GraphRoute.Cursor.localPhysicalDistance>70):cars[0].GraphRoute.Cursor.roadId==5&&cars[0].GraphRoute.Cursor.localPhysicalDistance>70;
   if(done)Finish();
  }catch(Exception e){cars=null;GraphCatalogRoutePlayChecks.Fail(e);}}
  void Finish(){
   Check(session.Racers.All(r=>!r.Finished&&r.ValidatedGateCount==0),"mid-course fixture never earns missing earlier gates");
   if(scenario==1)Check(cars[0].GraphRoute.Cursor.roadId==0&&cars[0].GraphRoute.Cursor.localPhysicalDistance<1200,"ordinary reverse crosses entire inverted loop");
   if(scenario==2)Check(yields>0,"actual mixed merge exercises ordinary yielding");
   if(scenario>=3&&scenario<=6)Check(selected==(scenario%2==1?1:2)&&mergeAt>entryAt,"natural selected branch and timed directed merge");
   if(scenario==7)Check(selected==2,"actual technical traffic changes useful route choice");
   File.WriteAllText(Path.Combine(GraphCatalogRoutePlayChecks.Output,"case-"+scenario+".txt"),$"CATALOG_ROUTE_OK case={scenario} entrants={cars.Length} seconds={tick*.01:R} branch={selected} entryAt={entryAt:R} mergeAt={mergeAt:R} branchSeconds={mergeAt-entryAt:R} entrySpeed={entrySpeed:R} mergeSpeed={mergeSpeed:R} yieldTicks={yields} graph={graph.Identity} controls=ordinary CCD=8\n");Debug.Log("CATALOG_ROUTE_OK case="+scenario);
   foreach(var car in cars)DestroyImmediate(car.gameObject);cars=null;if(++scenario<=7)Setup();else{trace.Dispose();trace=null;File.WriteAllText(Path.Combine(GraphCatalogRoutePlayChecks.Output,"result.txt"),"CATALOG_ROUTES_PLAY_OK reverseLoop mixedMerge naturalPairEntrySpeed trafficChoice\n");EditorApplication.isPlaying=false;}
  }
  void OnDestroy(){trace?.Dispose();}
 }
}

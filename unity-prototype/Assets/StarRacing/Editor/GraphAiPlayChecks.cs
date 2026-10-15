using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using StarRacingPrototype.Procedural;
namespace StarRacingPrototype {
 [InitializeOnLoad] public static class GraphAiPlayChecks {
  public const string Key="StarRacing.GraphAiPlay";static int startup;static double began;
  public static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_AI_PLAY_QA_DIR");
  static GraphAiPlayChecks(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run(){if(string.IsNullOrEmpty(Output))throw new Exception("Evidence directory required");Directory.CreateDirectory(Output);SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",false);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);startup=0;EditorApplication.update+=Enter;}
  public static void RunMerge(){Run();SessionState.SetBool(Key+"MergeOnly",true);}
  static void Enter(){if(++startup<15)return;EditorApplication.update-=Enter;EditorApplication.isPlaying=true;}
  static void Guard(){if(EditorApplication.timeSinceStartup-began>900)Fail(new Exception("Graph AI PlayerLoop timeout"));}
  static void Changed(PlayModeStateChange state){if(!SessionState.GetBool(Key,false))return;if(state==PlayModeStateChange.EnteredPlayMode){began=EditorApplication.timeSinceStartup;EditorApplication.update+=Guard;AudioListener.volume=0;Time.fixedDeltaTime=.01f;Time.maximumDeltaTime=.1f;Time.captureDeltaTime=.05f;new GameObject("Graph AI physical fixture").AddComponent<GraphAiPlayDriver>();}if(state==PlayModeStateChange.EnteredEditMode){EditorApplication.update-=Guard;SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",false)?1:0);}}
  public static void Fail(Exception e){Debug.LogException(e);File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());SessionState.SetBool(Key+"Failed",true);EditorApplication.isPlaying=false;}
 }
 [DefaultExecutionOrder(-100)] public sealed class GraphAiPlayDriver:MonoBehaviour {
  TrackBuilder track;IndependentGraph graph;ReleaseBalance balance;MagneticVehicle[] cars;AiDriver[] drivers;GraphAiWorldSnapshot world;RaceSession session;RaceObservation[] seen;DrivingInput[] commands;int scenario=1,tick;StreamWriter trace;int[] revisions;bool recovery;bool[] cleared;int yieldTicks,nitroTicks;
  void Check(bool ok,string why){if(!ok)throw new Exception("Graph AI physical: "+why);}
  void Start(){try{if(SessionState.GetBool(GraphAiPlayChecks.Key+"MergeOnly",false))scenario=3;gameObject.AddComponent<RacePhysicsStepper>();track=new GameObject("Independent production support").AddComponent<TrackBuilder>();graph=IndependentGraph.Create(new DVec(),new DVec(0,0,1),new DVec(0,1,0),new DVec(-1,0,0));track.BuildIndependent(graph);Physics.SyncTransforms();Check(track.GetComponentsInChildren<MeshCollider>().Count(c=>c.sharedMesh!=null&&c.sharedMesh.name==RoadVolumeMesh.SupportName)==26876,"support inventory");balance=ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);trace=new StreamWriter(Path.Combine(GraphAiPlayChecks.Output,"observations.csv"));trace.WriteLine("case,tick,id,road,local,progress,speed,desired,lane,steer,throttle,brake,nitro,yield,valid,revision,gates");Setup();}catch(Exception e){cars=null;GraphAiPlayChecks.Fail(e);}}
  void Setup(){int count=scenario==3?2:1;cars=new MagneticVehicle[count];drivers=new AiDriver[count];seen=new RaceObservation[count];commands=new DrivingInput[count];revisions=new int[count];cleared=new bool[count];tick=yieldTicks=nitroTicks=0;recovery=false;
   for(int id=0;id<count;id++){int road=scenario==4?0:scenario==3?(id==0?12:13):scenario;double at=scenario==4?10:scenario==3?5:2;cars[id]=new GameObject("Actual AI "+id).AddComponent<MagneticVehicle>();cars[id].InitializeIndependent(track,graph.Cursor(road,at),id,id==0?Color.cyan:Color.magenta,balance,true);drivers[id]=new AiDriver(id,DriverProfile.Racer,(uint)(77+id*31),balance);revisions[id]=cars[id].PositionRevision;seen[id]=RaceDirector.ObserveVehicle(cars[id]);cars[id].Hold(true);}
   Physics.SyncTransforms();world=new GraphAiWorldSnapshot(track.GraphSurface,count,balance);session=RaceDirector.CreateSessionForTrack(track,0,(float)(graph.Road(5).progressStart+40),count,count);session.Reset(seen);session.Begin();
  }
  void FixedUpdate(){try{if(cars==null)return;tick++;Check(tick<=24000,"did not finish case="+scenario+" within 240 simulated seconds");foreach(var car in cars)car.PrepareProjection();for(int i=0;i<cars.Length;i++)seen[i]=RaceDirector.ObserveVehicle(cars[i]);session.Tick(.01,seen);world.Capture(cars,drivers,session.Elapsed);
   for(int id=0;id<cars.Length;id++){var car=cars[id];if(cleared[id]||scenario==4&&session.Racers[id].Finished){car.Hold(true);car.SetInput(default);continue;}if(session.Phase==RacePhase.Countdown){car.Hold(true);car.SetInput(default);continue;}car.SetRaceContext(true,session.Racers[id].Place,cars.Length,session.Elapsed);if(tick%10==0||!world[id].Valid)commands[id]=drivers[id].StepGraph(world,.1f);car.SetInput(drivers[id].ApplyGraphControl(car,commands[id],Time.fixedDeltaTime));car.Hold(false);if(drivers[id].MergeYieldTo>=0)yieldTicks++;if(commands[id].nitro)nitroTicks++;
    Check(car.PositionRevision==revisions[id]&&!car.IsFalling,"unexpected recovery case="+scenario+" tick="+tick+" id="+id+" road="+car.GraphRoute.Cursor.roadId+" local="+car.GraphRoute.Cursor.localPhysicalDistance);
    if(tick%10==0){var cursor=car.GraphRoute.Cursor;float speed=Vector3.Dot(car.Body.linearVelocity,car.transform.forward);trace.WriteLine($"{scenario},{tick},{id},{cursor.roadId},{cursor.localPhysicalDistance:R},{car.GraphRoute.CommonProgress:R},{speed:R},{drivers[id].DesiredSpeed:R},{drivers[id].Lane:R},{commands[id].steer:R},{commands[id].throttle:R},{commands[id].brake:R},{commands[id].nitro},{drivers[id].MergeYieldTo},{world[id].Valid},{car.PositionRevision},{session.Racers[id].ValidatedGateCount}");trace.Flush();}
    if(scenario<3&&!recovery&&car.GraphRoute.Cursor.roadId==scenario&&car.GraphRoute.Cursor.localPhysicalDistance>graph.Road(scenario).Length*.45){car.ResetToCheckpoint();revisions[id]++;Check(car.GraphRoute.CommittedBranch==scenario,"recovery branch retained");recovery=true;}
   }
   for(int id=0;id<cars.Length;id++)cleared[id]=scenario==4?session.Racers[id].Finished:cars[id].GraphRoute.Cursor.roadId==5&&cars[id].GraphRoute.Cursor.localPhysicalDistance>45;
   if(cleared.All(value=>value))Finish();
  }catch(Exception e){cars=null;GraphAiPlayChecks.Fail(e);}}
  void Finish(){Check(scenario>=3||recovery,"real recovery exercised");if(scenario==4)Check(session.Racers.All(r=>r.ValidatedGateCount==session.GraphCourse.Gates.Count),"approach AI completes all physical gates");File.WriteAllText(Path.Combine(GraphAiPlayChecks.Output,"case-"+scenario+".txt"),$"GRAPH_AI_DRIVE_OK case={scenario} entrants={cars.Length} seconds={tick*.01:R} yieldTicks={yieldTicks} nitroTicks={nitroTicks} recovery={recovery} gates={session.GraphCourse.Gates.Count} controls=ordinary supportHulls=26876 graphMarks={track.TireMarks?.TotalSegments??0}\n");Debug.Log("GRAPH_AI_DRIVE_OK case="+scenario);foreach(var car in cars)DestroyImmediate(car.gameObject);cars=null;if(!SessionState.GetBool(GraphAiPlayChecks.Key+"MergeOnly",false)&&++scenario<=4)Setup();else{trace.Dispose();trace=null;File.WriteAllText(Path.Combine(GraphAiPlayChecks.Output,"result.txt"),SessionState.GetBool(GraphAiPlayChecks.Key+"MergeOnly",false)?"GRAPH_AI_MERGE_PLAY_OK entrants=2 controls=ordinary\n":"GRAPH_AI_PLAY_OK branches=2 mixedMerge=2 recoveries=2 approachFullRace=1 controls=ordinary\n");EditorApplication.isPlaying=false;}}
  void OnDestroy(){trace?.Dispose();}
 }
}

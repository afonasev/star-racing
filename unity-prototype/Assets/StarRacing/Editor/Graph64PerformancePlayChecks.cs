using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.Profiling;
using UnityEngine.Profiling;
namespace StarRacingPrototype {
 [InitializeOnLoad] public static class Graph64PerformancePlayChecks {
  const string Key="StarRacing.Graph64Performance";static int startup;static double began;
  public static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_64_QA_DIR");
  static Graph64PerformancePlayChecks(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run(){if(!Environment.GetCommandLineArgs().Contains("-noaudio"))throw new Exception("64-car QA requires -noaudio from startup");if(string.IsNullOrEmpty(Output))throw new Exception("Evidence required");Directory.CreateDirectory(Output);var physics=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset")[0]);int broadphase=physics.FindProperty("m_BroadphaseType").intValue;File.WriteAllText(Path.Combine(Output,"loaded-physics.txt"),"broadphase="+broadphase+"\n");var expected=Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_BROADPHASE_EXPECTED");if(expected!=null&&broadphase!=int.Parse(expected))throw new Exception("Loaded broadphase mismatch");SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",false);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);startup=0;EditorApplication.update+=Enter;}
  static void Enter(){if(++startup<15)return;EditorApplication.update-=Enter;EditorApplication.isPlaying=true;}
  static void Guard(){if(EditorApplication.timeSinceStartup-began>900)Fail(new Exception("64-car Editor performance deadline"));}
  static void Changed(PlayModeStateChange state){if(!SessionState.GetBool(Key,false))return;if(state==PlayModeStateChange.EnteredPlayMode){began=EditorApplication.timeSinceStartup;EditorApplication.update+=Guard;AudioListener.volume=0;Time.fixedDeltaTime=.01f;Time.maximumDeltaTime=.1f;Time.captureDeltaTime=.01f;new GameObject("Real 64-car graph runtime").AddComponent<Graph64PerformanceDriver>();}if(state==PlayModeStateChange.EnteredEditMode){EditorApplication.update-=Guard;SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",false)?1:0);}}
  public static void Fail(Exception e){UnityEngine.Debug.LogException(e);File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());SessionState.SetBool(Key+"Failed",true);EditorApplication.isPlaying=false;}
 }
 [DefaultExecutionOrder(-100)] public sealed class Graph64PerformanceDriver:MonoBehaviour {
  const int Warmup=500,Samples=1200;readonly double[] duration=new double[Samples],control=new double[Samples];ProfilerRecorder gc;ProfilerRecorder[] markers;readonly string[] markerNames={"StarRacing.Graph.Project","StarRacing.Graph.Support","StarRacing.Graph.Observe","StarRacing.Graph.Checkpoint","StarRacing.Graph.AI","StarRacing.Graph.HasCorridor","StarRacing.Graph.PredictedObstacle","StarRacing.Graph.ChoiceSeconds","StarRacing.Graph.NativePhysics","StarRacing.Graph.SupportGroups","StarRacing.Graph.SupportPrepare","StarRacing.Graph.SupportScan","StarRacing.Graph.SupportNative","StarRacing.Graph.SupportNormal","StarRacing.Graph.CorridorProject","StarRacing.Graph.SupportInventory","StarRacing.Graph.SupportCandidates","StarRacing.Graph.SupportEligibility","StarRacing.Graph.SupportBoundsSnapshot"};
  MagneticVehicle[] cars;AiDriver[] drivers;DrivingInput[] commands;RaceObservation[] seen;GraphAiWorldSnapshot world;RaceSession session;TrackBuilder track;int tick,recoveries,first,last;long began,bytes,controlled,steps;bool measuring,priorProfiler,priorBinary,cpuProfiling;ProfilerArea[] areas;bool[] areaStates;string priorLog;RenderTexture frameTarget;
  public void Start(){try{
   gameObject.AddComponent<RacePhysicsStepper>();gameObject.AddComponent<Graph64PerformanceObserver>().owner=this;
   track=new GameObject("Full graph physical inventory").AddComponent<TrackBuilder>();var graph=GraphCommonCatalogChecks.Geometry();track.BuildIndependentCommonCatalog(graph);Physics.SyncTransforms();
   int hulls=track.GetComponentsInChildren<MeshCollider>().Count(c=>c.sharedMesh!=null&&c.sharedMesh.name==RoadVolumeMesh.SupportName);if(hulls!=71264)throw new Exception("Production hull inventory missing");
   var balance=ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);cars=new MagneticVehicle[64];drivers=new AiDriver[64];commands=new DrivingInput[64];seen=new RaceObservation[64];
   // Initial physical grid only. Every subsequent movement uses ordinary AI input.
   for(int i=0;i<64;i++){cars[i]=new GameObject("Performance car "+i).AddComponent<MagneticVehicle>();cars[i].InitializeIndependent(track,graph.Cursor(0,graph.Road(0).Length-550-(i/2)*8),i,Color.cyan,balance,false);cars[i].ResetOnGraph(graph.Cursor(0,graph.Road(0).Length-550-(i/2)*8),i%2==0?-3.5f:3.5f);drivers[i]=new AiDriver(i,DriverProfile.Racer,(uint)(77+i*31),balance);seen[i]=RaceDirector.ObserveVehicle(cars[i]);}
   cpuProfiling=Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_CPU_PROFILE")=="yes";areas=(ProfilerArea[])Enum.GetValues(typeof(ProfilerArea));areaStates=areas.Select(Profiler.GetAreaEnabled).ToArray();priorProfiler=Profiler.enabled;priorBinary=Profiler.enableBinaryLog;priorLog=Profiler.logFile;Profiler.logFile=Path.Combine(Graph64PerformancePlayChecks.Output,"profile.raw");Profiler.enabled=false;Profiler.enableBinaryLog=true;foreach(var area in areas)Profiler.SetAreaEnabled(area,area==ProfilerArea.Memory||area==ProfilerArea.CPU&&cpuProfiling);
   var camera=new GameObject("Profiler frame pump").AddComponent<Camera>();camera.cullingMask=0;frameTarget=new RenderTexture(8,8,24);camera.targetTexture=frameTarget;
   world=new GraphAiWorldSnapshot(track.GraphSurface,64,balance);session=RaceDirector.CreateSessionForTrack(track,0,(float)(graph.Road(5).progressEnd-5),64,1);session.Reset(seen);session.Begin();gc=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame",Samples);markers=markerNames.Select(name=>ProfilerRecorder.StartNew(ProfilerCategory.Scripts,name,Samples)).ToArray();if(!gc.Valid||cpuProfiling&&markers.Any(r=>!r.Valid))throw new Exception("Profiler counters unavailable");steps=RacePhysicsStepper.NativeSteps;first=GC.CollectionCount(0);last=first;Physics.SyncTransforms();
  }catch(Exception e){cars=null;Graph64PerformancePlayChecks.Fail(e);}}
  void FixedUpdate(){try{
   if(cars==null)return;began=Stopwatch.GetTimestamp();bytes=0;measuring=true;tick++;
   for(int i=0;i<64;i++){cars[i].PrepareProjection();seen[i]=RaceDirector.ObserveVehicle(cars[i]);}
   session.Tick(.01,seen);world.Capture(cars,drivers,session.Elapsed);
   for(int i=0;i<64;i++){
    var car=cars[i];if(session.Phase==RacePhase.Countdown){car.Hold(true);car.SetInput(default);continue;}
    car.SetRaceContext(true,session.Racers[i].Place,64,session.Elapsed);if(tick%10==i%10||!world[i].Valid)commands[i]=drivers[i].StepGraph(world,.1f);car.SetInput(drivers[i].ApplyGraphControl(car,commands[i],Time.fixedDeltaTime));car.Hold(false);
    if(car.IsFalling)recoveries++;
   }
   controlled=Stopwatch.GetTimestamp();
  }catch(Exception e){cars=null;measuring=false;Graph64PerformancePlayChecks.Fail(e);}}
  public void AfterPhysics(){if(!measuring||cars==null)return;measuring=false;long ended=Stopwatch.GetTimestamp();
   if(tick==Warmup){GraphRoadSurface.ResetSupportCounters();gc.Reset();gc.Start();foreach(var recorder in markers){recorder.Reset();recorder.Start();}first=GC.CollectionCount(0);Profiler.enabled=true;}
   if(tick>Warmup){int i=tick-Warmup-1;duration[i]=(ended-began)*1000d/Stopwatch.Frequency;control[i]=(controlled-began)*1000d/Stopwatch.Frequency;}
   if(tick!=Warmup+Samples)return;
   try{
    if(RacePhysicsStepper.NativeSteps-steps!=(long)tick*8)throw new Exception("CCD substep contract changed");
    var allocations=gc.ToArray();bool gcAvailable=allocations.Length>0&&allocations.Any(s=>s.Value>0);
    var measured=new Graph64Metrics{entrants=64,warmupTicks=Warmup,sampleTicks=Samples,simulatedSeconds=Samples*.01,controllerHz=100,substeps=8,medianTickMs=Percentile(duration,.5),p95TickMs=Percentile(duration,.95),p99TickMs=Percentile(duration,.99),maxTickMs=duration.Max(),medianControlMs=Percentile(control,.5),p95ControlMs=Percentile(control,.95),allocatedBytesPerFrame=allocations.Length>0?allocations.Average(s=>s.Value):0,allocatedBytesTotal=allocations.Sum(s=>s.Value),gcFrameSamples=allocations.Length,gcCounterAvailable=gcAvailable,profile=markers.Select((r,i)=>new Graph64ProfileMetric{name=markerNames[i],available=r.Count>0,count=r.Count,meanMs=r.Count>0?r.ToArray().Average(s=>s.Value)*1e-6:0}).ToArray(),generation0Collections=GC.CollectionCount(0)-first,recoveringCarTicks=recoveries,validAtEnd=Enumerable.Range(0,64).Count(i=>world[i].Valid),branchesAtEnd=cars.GroupBy(c=>c.GraphRoute.Cursor.roadId).Select(g=>g.Key+":"+g.Count()).ToArray(),mode="exclusive",runtime="Editor PlayMode real full physical inventory",cpuProfiling=cpuProfiling,limitation="Stopwatch CPU Editor ticks; GC frame counter includes all threads and Editor overhead, recorders reset after500warmup ticks; one completed-frame lag; no Player/device/GPU acceptance"};
    File.WriteAllText(Path.Combine(Graph64PerformancePlayChecks.Output,"metrics.json"),JsonUtility.ToJson(measured,true)+"\n");
    if(GraphRoadSurface.SupportCountersEnabled)
     File.WriteAllText(Path.Combine(Graph64PerformancePlayChecks.Output,"support-counters.json"),GraphRoadSurface.GetSupportCounterReport(Warmup,Samples));
    if(!gcAvailable)throw new Exception("GC frame counter unavailable; raw CPU metrics saved, no performance pass");
    File.WriteAllText(Path.Combine(Graph64PerformancePlayChecks.Output,"result.txt"),"GRAPH_64_MEASURED samples=1200 warmup=500 realCars=64 fullSupport=71264 ordinaryControls CCD=8\n");UnityEngine.Debug.Log("GRAPH_64_MEASURED p95="+measured.p95TickMs+" allocation="+measured.allocatedBytesPerFrame);EditorApplication.isPlaying=false;
   }catch(Exception e){Graph64PerformancePlayChecks.Fail(e);}
  }
  void OnDestroy(){Profiler.enabled=false;if(areas!=null)for(int i=0;i<areas.Length;i++)Profiler.SetAreaEnabled(areas[i],areaStates[i]);Profiler.enableBinaryLog=priorBinary;Profiler.logFile=priorLog;Profiler.enabled=priorProfiler;if(frameTarget!=null){frameTarget.Release();Destroy(frameTarget);}gc.Dispose();if(markers!=null)foreach(var recorder in markers)recorder.Dispose();}
  static double Percentile(double[] values,double p){var copy=(double[])values.Clone();Array.Sort(copy);return copy[(int)Math.Ceiling(p*copy.Length)-1];}
 }
 [Serializable] public sealed class Graph64Metrics {
  public int entrants,warmupTicks,sampleTicks,controllerHz,substeps,generation0Collections,recoveringCarTicks,validAtEnd,gcFrameSamples;
  public double simulatedSeconds,medianTickMs,p95TickMs,p99TickMs,maxTickMs,medianControlMs,p95ControlMs,allocatedBytesPerFrame;
  public bool gcCounterAvailable,cpuProfiling;public Graph64ProfileMetric[] profile;public long allocatedBytesTotal;public string[] branchesAtEnd;public string mode,runtime,limitation;
 }
 [Serializable] public sealed class Graph64ProfileMetric{public string name;public bool available;public int count;public double meanMs;}
 [DefaultExecutionOrder(2000)] public sealed class Graph64PerformanceObserver:MonoBehaviour {public Graph64PerformanceDriver owner;void FixedUpdate()=>owner?.AfterPhysics();}
}

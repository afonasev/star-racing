using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Profiling;
namespace StarRacingPrototype {
 [InitializeOnLoad] public static class Cloud32PerformanceDiagnostics {
  const string Key="StarRacing.Cloud32Diagnostics";static double deadline;
  public static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_CLOUD32_DIAG_DIR");
  static Cloud32PerformanceDiagnostics(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run(){
   if(string.IsNullOrEmpty(Output))throw new Exception("STAR_RACING_CLOUD32_DIAG_DIR required");
   if(!Environment.GetCommandLineArgs().Contains("-noaudio")||!Environment.GetCommandLineArgs().Contains("-muted"))throw new Exception("Silent startup required");
   Directory.CreateDirectory(Output);SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",true);
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.isPlaying=true;
  }
  static void Changed(PlayModeStateChange state){if(!SessionState.GetBool(Key,false))return;
   if(state==PlayModeStateChange.EnteredPlayMode){deadline=EditorApplication.timeSinceStartup+600;EditorApplication.update+=Guard;new GameObject("Cloud32 normal lifecycle diagnostic").AddComponent<Cloud32DiagnosticDriver>();}
   if(state==PlayModeStateChange.EnteredEditMode){EditorApplication.update-=Guard;SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",true)?1:0);}
  }
  static void Guard(){EditorApplication.QueuePlayerLoopUpdate();if(EditorApplication.timeSinceStartup>deadline)Finish(new TimeoutException("Cloud32 diagnostic deadline"));}
  public static void Finish(Exception failure=null){if(failure!=null){File.WriteAllText(Path.Combine(Output,"failure.txt"),failure.ToString());UnityEngine.Debug.LogException(failure);}SessionState.SetBool(Key+"Failed",failure!=null);EditorApplication.isPlaying=false;}
 }
 [DefaultExecutionOrder(-200)] public sealed class Cloud32DiagnosticDriver:MonoBehaviour {
  [Serializable] public sealed class Leaf {public string name;public int calls;public double seconds,maxSeconds;}
  [Serializable] public sealed class Metrics {public string limitation="Exclusive Editor CPU timings; real ordinary32-car physics and cameras, not Player/GPU/FPS or human acceptance";public double menuSeconds,preparationSeconds,firstFrameSeconds,medianTickMs,p95TickMs,maxTickMs,simulatedSeconds;public long nativeSteps;public bool preparationAllocationCounterAvailable,runtimeAllocationCounterAvailable;public long preparationAllocatedBytes,runtimeAllocatedBytes,managedBytesAfterPreparation;public int runtimeGcCollections,gcTicks;public double maxGcTickMs;public int maxSlices;public string inventorySHA256;public int colliderCount;public int entrants=32,samples;public string config;public Leaf[] leaves;public Marker[] markers;}
  [Serializable] public sealed class Marker {public string name;public int count;public double meanMs;}
  readonly Dictionary<string,Leaf> leaves=new Dictionary<string,Leaf>();readonly List<double> ticks=new List<double>();
  readonly string[] names={"StarRacing.Graph.NativePhysics","StarRacing.Graph.Project","StarRacing.Graph.Support","StarRacing.Graph.Observe","StarRacing.Graph.Checkpoint","StarRacing.Graph.AI","StarRacing.Physics.Refine","StarRacing.Physics.Snapshot","StarRacing.Physics.Pairs","StarRacing.Physics.Forces"};
  ProfilerRecorder[] recorders;GameObject host;RaceDirector director;Keyboard addedKeyboard;bool had,restored,measuring;string saved,settingsPath;long tickBegan,steps,tickSteps,allocated;int collections,tickCollections;double seconds;Metrics result=new Metrics();
  IEnumerator Start(){
   had=PlayerPrefs.HasKey(LocalRaceConfig.PreferenceKey);saved=PlayerPrefs.GetString(LocalRaceConfig.PreferenceKey);settingsPath=UserSettingsStore.OverridePath;
   UserSettingsStore.OverridePath=Path.Combine(Cloud32PerformanceDiagnostics.Output,"isolated-user-settings.json");UserSettingsStore.Reload();Application.runInBackground=true;
   if(Keyboard.current==null)addedKeyboard=InputSystem.AddDevice<Keyboard>();
   new LocalRaceConfig{entrants=32,humans=1,seed="211933141",theme="cloud-city",rails="normal",jumps=true,generatorRevision=12}.Save();
   LoadingWork.StepMeasured=Measure;RoadVolumeMesh.WorkMeasured=Measure;var run=Run();
   while(true){bool more;try{more=run.MoveNext();}catch(Exception e){Restore();Cloud32PerformanceDiagnostics.Finish(e);yield break;}if(!more)break;yield return run.Current;}
   Restore();Cloud32PerformanceDiagnostics.Finish();
  }
  void Measure(string name,double time){if(!leaves.TryGetValue(name,out var leaf))leaves[name]=leaf=new Leaf{name=name};leaf.calls++;leaf.seconds+=time;leaf.maxSeconds=Math.Max(leaf.maxSeconds,time);}
  void Event(string text)=>File.AppendAllText(Path.Combine(Cloud32PerformanceDiagnostics.Output,"events.txt"),DateTime.UtcNow.ToString("O")+" "+text+"\n");
  IEnumerator Run(){
   double began=Time.realtimeSinceStartupAsDouble;Event("startup");host=new GameObject("Normal startup");host.AddComponent<RaceStartup>();
   while(director==null||!director.Ready){director=host.GetComponent<RaceDirector>();yield return null;}
   result.menuSeconds=Time.realtimeSinceStartupAsDouble-began;Event("menu ready "+result.menuSeconds);
   var menu=director.GetComponent<RaceMenu>();var config=new LocalRaceConfig{entrants=32,humans=1,seed="211933141",theme="cloud-city",rails="normal",jumps=true,generatorRevision=12};
   typeof(RaceMenu).GetProperty("Selected").SetValue(menu,config);typeof(RaceMenu).GetField("seedText",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(menu,config.seed);
   leaves.Clear();allocated=GC.GetAllocatedBytesForCurrentThread();began=Time.realtimeSinceStartupAsDouble;Event("prepare");menu.StartSelected();
   while(menu.Loading)yield return null;
   result.preparationAllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-allocated;result.preparationAllocationCounterAvailable=result.preparationAllocatedBytes>0;result.managedBytesAfterPreparation=GC.GetTotalMemory(false);result.preparationSeconds=Time.realtimeSinceStartupAsDouble-began;result.leaves=leaves.Values.OrderByDescending(l=>l.seconds).ToArray();Save();
   if(!director.Started||director.Cars.Length!=32)throw new Exception("Ordinary32-car start failed "+menu.Error);
   result.config=JsonUtility.ToJson(director.AppliedConfig);yield return null;result.firstFrameSeconds=Time.realtimeSinceStartupAsDouble-began;Event("prepared "+result.firstFrameSeconds);
   Fingerprint();
   Time.captureDeltaTime=.01f;new GameObject("After physics CPU observer").AddComponent<Cloud32DiagnosticObserver>().owner=this;
   while(director.Session.Phase==RacePhase.Countdown)yield return null;
   int warm=0;while(warm++<200)yield return new WaitForFixedUpdate();
   recorders=names.Select(n=>ProfilerRecorder.StartNew(ProfilerCategory.Scripts,n,2048,ProfilerRecorderOptions.Default|ProfilerRecorderOptions.SumAllSamplesInFrame)).ToArray();
   collections=GC.CollectionCount(0);allocated=GC.GetAllocatedBytesForCurrentThread();steps=RacePhysicsStepper.NativeSteps;seconds=RacePhysicsStepper.SimulatedSeconds;measuring=true;Event("sample begin");
   while(ticks.Count<1200)yield return null;
   measuring=false;result.runtimeGcCollections=GC.CollectionCount(0)-collections;result.runtimeAllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-allocated;result.runtimeAllocationCounterAvailable=result.runtimeAllocatedBytes>0;director.SetPaused(true);result.nativeSteps=RacePhysicsStepper.NativeSteps-steps;result.simulatedSeconds=RacePhysicsStepper.SimulatedSeconds-seconds;
   result.samples=ticks.Count;var sorted=ticks.OrderBy(x=>x).ToArray();result.medianTickMs=sorted[sorted.Length/2];result.p95TickMs=sorted[(int)Math.Ceiling(sorted.Length*.95)-1];result.maxTickMs=sorted.Last();
   result.markers=recorders.Select((r,i)=>new Marker{name=names[i],count=r.Count,meanMs=r.Count>0?r.ToArray().Average(s=>s.Value)*1e-6:0}).ToArray();Capture();Save();Event("complete p95="+result.p95TickMs+" nativeSteps="+result.nativeSteps);
  }
  void Capture(){
   var camera=director.CameraFor(0).GetComponent<Camera>();var prior=camera.targetTexture;var active=RenderTexture.active;var rt=new RenderTexture(1280,720,24);Texture2D pixels=null;
   try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(Cloud32PerformanceDiagnostics.Output,"race-camera.png"),pixels.EncodeToPNG());}
   finally{camera.targetTexture=prior;RenderTexture.active=active;if(pixels!=null)Destroy(pixels);rt.Release();Destroy(rt);}
  }
  void Fingerprint(){
   using(var hash=System.Security.Cryptography.SHA256.Create())using(var stream=new MemoryStream())using(var w=new BinaryWriter(stream)){
    void V(Vector3 v){w.Write(v.x);w.Write(v.y);w.Write(v.z);}
    foreach(var c in director.Track.GetComponentsInChildren<MeshCollider>(true)){
     result.colliderCount++;w.Write(c.name);V(c.transform.position);V(c.transform.lossyScale);var q=c.transform.rotation;w.Write(q.x);w.Write(q.y);w.Write(q.z);w.Write(q.w);
     w.Write(c.gameObject.layer);w.Write(c.convex);w.Write(c.enabled);w.Write(c.isTrigger);w.Write(c.contactOffset);w.Write((int)c.cookingOptions);w.Write(c.hasModifiableContacts);
     var m=c.sharedMesh;w.Write(m!=null);if(m!=null){w.Write(m.name);var vertices=m.vertices;w.Write(vertices.Length);foreach(var v in vertices)V(v);var indices=m.triangles;w.Write(indices.Length);foreach(int i in indices)w.Write(i);}
    }
    w.Flush();result.inventorySHA256=BitConverter.ToString(hash.ComputeHash(stream.ToArray())).Replace("-","").ToLowerInvariant();
   }
  }
  void Save()=>File.WriteAllText(Path.Combine(Cloud32PerformanceDiagnostics.Output,"metrics.json"),JsonUtility.ToJson(result,true));
  void FixedUpdate(){if(measuring){tickBegan=Stopwatch.GetTimestamp();tickSteps=RacePhysicsStepper.NativeSteps;tickCollections=GC.CollectionCount(0);}}
  public void AfterPhysics(){if(measuring){ticks.Add((Stopwatch.GetTimestamp()-tickBegan)*1000d/Stopwatch.Frequency);if(GC.CollectionCount(0)>tickCollections){result.gcTicks++;result.maxGcTickMs=Math.Max(result.maxGcTickMs,ticks[ticks.Count-1]);}result.maxSlices=Math.Max(result.maxSlices,(int)(RacePhysicsStepper.NativeSteps-tickSteps));}}
  void Restore(){if(restored)return;restored=true;LoadingWork.StepMeasured=null;RoadVolumeMesh.WorkMeasured=null;Time.captureDeltaTime=0;Time.timeScale=1;
   if(had)PlayerPrefs.SetString(LocalRaceConfig.PreferenceKey,saved);else PlayerPrefs.DeleteKey(LocalRaceConfig.PreferenceKey);PlayerPrefs.Save();UserSettingsStore.OverridePath=settingsPath;UserSettingsStore.Reload();
   if(addedKeyboard!=null&&addedKeyboard.added)InputSystem.RemoveDevice(addedKeyboard);if(recorders!=null)foreach(var recorder in recorders)recorder.Dispose();if(host!=null)Destroy(host);
  }
  void OnDestroy()=>Restore();
 }
 [DefaultExecutionOrder(2000)] public sealed class Cloud32DiagnosticObserver:MonoBehaviour {public Cloud32DiagnosticDriver owner;void FixedUpdate()=>owner.AfterPhysics();}
}

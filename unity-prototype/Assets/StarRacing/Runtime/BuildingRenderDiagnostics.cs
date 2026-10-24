using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Profiling;
using UnityEngine;

namespace StarRacingPrototype {
 // Explicitly opt-in attribution only. No settings or scene changes without the command argument.
 public sealed class BuildingRenderDiagnostics : MonoBehaviour {
  const string Argument="--building-render-diagnostics-dir";
  [Serializable] public sealed class Sample {
   public double wall,deltaMs,unscaledDeltaMs;public bool focused;
   public int unityFrame;public float scale;public bool simulationFrozen;public long ticks,batches,setPass,triangles;public bool timingAvailable;public double cpuMs,mainMs,renderMs,gpuMs;
  }
  [Serializable] public sealed class MeshEntry {public string name,mesh,group,shadowMode;public long triangles;public bool enabled,active,frustum;public Vector3 position,scale;}
  [Serializable] public sealed class Window {
   public bool buildings;public int index;public long nativeDelta;public double simulationDelta;
   public List<Sample> samples=new List<Sample>();
  }
  [Serializable] public sealed class Report {
   public string version,config,failure,mode;
   public string limitation="Diagnostic only. Frozen screen isolates selected building renderers, including their shadows; mist remains enabled. Timing packets delayed; missing counters are unavailable (-1), GPU zero unavailable. No gameplay/human acceptance.";
   public int width,height,entrants,renderers,colliders;public bool restored,completed;
   public Vector3 cameraPosition;public Quaternion cameraRotation;public float farClip;
   public double menuSeconds,preparationSeconds;public MeshEntry[] inventory;public List<Window> windows=new List<Window>();
  }
  readonly List<Renderer> chunkSources=new List<Renderer>(),chunkRenderers=new List<Renderer>();readonly List<Mesh> chunkMeshes=new List<Mesh>();
  LoadingWork.Freeze renderFreeze;int savedVsync,savedFrameLimit;bool frozenInput;RaceMenu frozenMenu;bool chunkScreen;Report report=new Report();string output;RaceDirector director;Renderer[] buildings;bool[] flags;
  RaceFrameDiagnostics liveProbe;int originalMask;bool maskSaved;
  bool hadPrefs,restored;string prefs;Camera camera;ChaseCamera chase;bool chaseEnabled;
  readonly Stopwatch clock=Stopwatch.StartNew();double deadline=600;FrameTiming[] timings=new FrameTiming[1];ulong timestamp;
  ProfilerRecorder batches,setPass,triangles;
  static bool Requested(out string path){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,Argument);path=i>=0&&i+1<args.Length?args[i+1]:null;return i>=0;}
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)] static void Install(){
   if(!Requested(out var path))return;
   if(!SilentStartup.Enabled||!Path.IsPathRooted(path))throw new InvalidOperationException("Silent absolute diagnostic output required");
   Directory.CreateDirectory(path);if(File.Exists(Path.Combine(path,"started.json")))throw new IOException("Fresh output required");
   var host=new GameObject("Opt-in building render attribution");DontDestroyOnLoad(host);var probe=host.AddComponent<BuildingRenderDiagnostics>();probe.output=path;
   probe.hadPrefs=PlayerPrefs.HasKey(LocalRaceConfig.PreferenceKey);probe.prefs=PlayerPrefs.GetString(LocalRaceConfig.PreferenceKey);
   probe.report.version=Application.version;probe.report.mode="frozen";
   File.WriteAllText(Path.Combine(path,"started.json"),JsonUtility.ToJson(probe.report,true));
  }
  IEnumerator Start(){
   var work=Run();while(true){bool more;try{more=work.MoveNext();}catch(Exception e){Finish(e);yield break;}if(!more)break;yield return work.Current;}Finish(null);
  }
  void Update(){if(output!=null&&clock.Elapsed.TotalSeconds>deadline){deadline=double.PositiveInfinity;Finish(new TimeoutException("Building attribution wall deadline"));}}
  double Now=>clock.Elapsed.TotalSeconds;
  IEnumerator Run(){
   Application.runInBackground=true;
   bool requireFocus=Array.IndexOf(Environment.GetCommandLineArgs(),"--building-render-require-focus")>=0;
   if(requireFocus){
    UnityEngine.Debug.Log("BUILDING_FOCUS_WAIT preparation");
    while(!Application.isFocused)yield return null;
    UnityEngine.Debug.Log("BUILDING_FOCUS_READY preparation");
   }
   while(director==null||!director.Ready){director=FindAnyObjectByType<RaceDirector>();yield return null;}
   report.menuSeconds=Now;
   var menu=director.GetComponent<RaceMenu>();
   var config=new LocalRaceConfig{entrants=32,humans=1,seed="211933141",theme="cloud-city",rails="normal",jumps=true,generatorRevision=12};
   typeof(RaceMenu).GetProperty("Selected").SetValue(menu,config);typeof(RaceMenu).GetField("seedText",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(menu,config.seed);
   double began=Now;menu.StartSelected();while(menu.Loading)yield return null;
   report.preparationSeconds=Now-began;
   if(!director.Started||director.Cars.Length!=32)throw new Exception("Normal cloud32 preparation failed: "+menu.Error);
   director.SetPaused(true);report.entrants=director.Cars.Length;report.config=JsonUtility.ToJson(director.AppliedConfig);
   var environment=(TrackEnvironmentBuilder)typeof(RaceDirector).GetField("environment",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(director);
   buildings=environment.Root.GetComponentsInChildren<Renderer>(true).Where(r=>r.name=="City tower"||r.name=="City tower trim"||r.name=="City district").ToArray();
   flags=buildings.Select(r=>r.enabled).ToArray();report.renderers=buildings.Length;
   if(buildings.Length==0)throw new Exception("No building renderer selection");
   report.colliders=director.Track.GetComponentsInChildren<Collider>(true).Length;
   if(environment.Root.GetComponentsInChildren<Collider>(true).Length!=0)throw new Exception("Unexpected environment collider");
   camera=director.CameraFor(0).GetComponent<Camera>();chase=camera.GetComponent<ChaseCamera>();chaseEnabled=chase.enabled;
   // Let the ordinary camera settle before fixing its transform and far clip for all arms.
   double until=Now+3;while(Now<until)yield return null;chase.enabled=false;
   report.cameraPosition=camera.transform.position;report.cameraRotation=camera.transform.rotation;report.farClip=camera.farClipPlane;report.width=Screen.width;report.height=Screen.height;
   var planes=GeometryUtility.CalculateFrustumPlanes(camera);
   report.inventory=FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Select(r=>{
    var mesh=r.GetComponent<MeshFilter>()?.sharedMesh;long count=0;if(mesh!=null)for(int sub=0;sub<mesh.subMeshCount;sub++)count+=(long)mesh.GetIndexCount(sub)/3;
    return new MeshEntry{name=r.name,mesh=mesh==null?"":mesh.name,group=r.GetComponentInParent<StaticVehicleVisual>()!=null?"vehicle":r.transform.IsChildOf(environment.Root)?"environment":r.transform.IsChildOf(director.Track.transform)?"track":"other",triangles=count,enabled=r.enabled,active=r.gameObject.activeInHierarchy,frustum=GeometryUtility.TestPlanesAABB(planes,r.bounds),shadowMode=r.shadowCastingMode.ToString(),position=r.transform.position,scale=r.transform.lossyScale};
   }).ToArray();
   batches=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Batches Count",1);
   setPass=ProfilerRecorder.StartNew(ProfilerCategory.Render,"SetPass Calls Count",1);
   triangles=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Triangles Count",1);
   if(Array.IndexOf(Environment.GetCommandLineArgs(),"--building-render-live")>=0){
    bool hidden=Array.IndexOf(Environment.GetCommandLineArgs(),"--building-render-hidden")>=0;
    report.mode=hidden?"live-geometry-off":"live-geometry-on";
    originalMask=camera.cullingMask;maskSaved=true;if(hidden)camera.cullingMask=0;chase.enabled=chaseEnabled;
    ScreenCapture.CaptureScreenshot(Path.Combine(output,"live-before-measurement.png"));
    yield return null;until=Now+1;while(Now<until)yield return null;
    if(requireFocus){
     UnityEngine.Debug.Log("BUILDING_FOCUS_WAIT measurement");
     while(!Application.isFocused)yield return null;
     UnityEngine.Debug.Log("BUILDING_FOCUS_READY measurement");
    }
    liveProbe=gameObject.AddComponent<RaceFrameDiagnostics>();liveProbe.Begin(Path.Combine(output,"frames"),director,true,true,60);
    var window=new Window{index=0,buildings=!hidden};report.windows.Add(window);double previous=Now;
    director.SetPaused(false);
    while(!liveProbe.Completed){yield return null;double now=Now;window.samples.Add(new Sample{unityFrame=Time.frameCount,scale=Time.timeScale,simulationFrozen=LoadingWork.SimulationFrozen,wall=now,deltaMs=(now-previous)*1000,unscaledDeltaMs=Time.unscaledDeltaTime*1000,focused=Application.isFocused,ticks=RacePhysicsStepper.NativeSteps,batches=batches.Valid?batches.LastValue:-1,setPass=setPass.Valid?setPass.LastValue:-1,triangles=triangles.Valid?triangles.LastValue:-1});previous=now;}
    if(liveProbe.Failure!=null)throw new Exception(liveProbe.Failure);
    yield break;
   }
   // Frozen attribution excludes user-driven resume and menu input during each arm.
   director.SetPaused(true);if(!director.Paused||Time.timeScale!=0)throw new Exception("Frozen pause precondition");
   renderFreeze=new LoadingWork.Freeze();renderFreeze.Keep(0);
   savedVsync=QualitySettings.vSyncCount;savedFrameLimit=Application.targetFrameRate;QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
   frozenMenu=director.GetComponent<RaceMenu>();frozenMenu.enabled=false;director.enabled=false;frozenInput=true;
   bool ringScreen=Array.IndexOf(Environment.GetCommandLineArgs(),"--visual-distant-ring-screen")>=0;
   chunkScreen=ringScreen||Array.IndexOf(Environment.GetCommandLineArgs(),"--visual-chunk-screen")>=0;
   if(chunkScreen){
    report.mode=ringScreen?"distant-rings-1500":"visual-chunks-512";report.limitation="Frozen render-only ABBA, input disabled, process-local uncapped frames, no physics progression; buildings and horizon retained. Not live race FPS or human acceptance.";var timer=Stopwatch.StartNew();
    foreach(var source in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)){
     var filter=source.GetComponent<MeshFilter>();var mesh=filter?.sharedMesh;
     if(!source.enabled||mesh==null)continue;
     if(ringScreen ? !(source.name=="City district"||source.name=="City tower trim") : !(mesh.name.Contains("environment road visual")||mesh.name.Contains("environment rail visual")||mesh.name=="Environment road visual"||mesh.name=="Environment rail visual"||mesh.name==RoadVolumeMesh.MeshName))continue;
     chunkSources.Add(source);
     foreach(var part in ringScreen?new List<Mesh>{WithoutDistantRings(mesh,source.transform,camera.transform.position)}:VisualMeshChunks.Create(mesh)){
      chunkMeshes.Add(part);var go=new GameObject("Diagnostic visual section");go.transform.SetParent(source.transform,false);
      go.AddComponent<MeshFilter>().sharedMesh=part;var r=go.AddComponent<MeshRenderer>();r.sharedMaterials=source.sharedMaterials;
      r.shadowCastingMode=source.shadowCastingMode;r.receiveShadows=source.receiveShadows;r.lightProbeUsage=source.lightProbeUsage;r.reflectionProbeUsage=source.reflectionProbeUsage;r.enabled=false;chunkRenderers.Add(r);
     }
    }
    UnityEngine.Debug.Log("VISUAL_CHUNKS_SETUP sources="+chunkSources.Count+" chunks="+chunkMeshes.Count+" ms="+timer.Elapsed.TotalMilliseconds);
    if(chunkSources.Count==0)throw new Exception("No visual sources selected");
   }
   for(int arm=0;arm<4;arm++){
    bool on=arm==0||arm==3;
    if(chunkScreen){foreach(var r in chunkSources)r.enabled=on;foreach(var r in chunkRenderers)r.enabled=!on;}
    else for(int i=0;i<buildings.Length;i++)buildings[i].enabled=on&&flags[i];
    until=Now+2;while(Now<until)yield return null; // Exclude toggles and delayed timing packets.
    ScreenCapture.CaptureScreenshot(Path.Combine(output,"arm-"+arm+"-"+(on?"on":"off")+".png"));
    yield return null;until=Now+1;while(Now<until)yield return null; // Screenshot outside measurement.
    var window=new Window{index=arm,buildings=on};report.windows.Add(window);
    long steps=RacePhysicsStepper.NativeSteps;double sim=RacePhysicsStepper.SimulatedSeconds;
    double previous=Now;until=previous+5;
    while(Now<until){yield return null;double now=Now;
     var sample=new Sample{scale=Time.timeScale,simulationFrozen=LoadingWork.SimulationFrozen,wall=now,deltaMs=(now-previous)*1000,unscaledDeltaMs=Time.unscaledDeltaTime*1000,focused=Application.isFocused,ticks=RacePhysicsStepper.NativeSteps-steps,
      batches=batches.Valid?batches.LastValue:-1,setPass=setPass.Valid?setPass.LastValue:-1,triangles=triangles.Valid?triangles.LastValue:-1};previous=now;
     if(FrameTimingManager.IsFeatureEnabled()){
      FrameTimingManager.CaptureFrameTimings();if(FrameTimingManager.GetLatestTimings(1,timings)>0&&timings[0].frameStartTimestamp>timestamp){var t=timings[0];timestamp=t.frameStartTimestamp;sample.timingAvailable=true;sample.cpuMs=t.cpuFrameTime;sample.mainMs=t.cpuMainThreadFrameTime;sample.renderMs=t.cpuRenderThreadFrameTime;sample.gpuMs=t.gpuFrameTime;}
     }
     window.samples.Add(sample);
     if(!camera.transform.position.Equals(report.cameraPosition)||!camera.transform.rotation.Equals(report.cameraRotation)||camera.farClipPlane!=report.farClip||Screen.width!=report.width||Screen.height!=report.height)throw new Exception("Camera/display changed");
    }
    window.nativeDelta=RacePhysicsStepper.NativeSteps-steps;window.simulationDelta=RacePhysicsStepper.SimulatedSeconds-sim;
    if(window.nativeDelta!=0||window.simulationDelta!=0)throw new Exception("Frozen screen advanced physics");
    if(director.Track.GetComponentsInChildren<Collider>(true).Length!=report.colliders)throw new Exception("Collider inventory changed");
   }
  }
  static Mesh WithoutDistantRings(Mesh source,Transform transform,Vector3 camera){
   var vertices=source.vertices;var triangles=source.triangles;var keep=new List<int>();
   for(int i=0;i<triangles.Length;i+=3){int start=triangles[i]/24*24;float min=float.MaxValue,max=float.MinValue;var center=Vector3.zero;
    for(int j=start;j<Mathf.Min(start+24,vertices.Length);j++){min=Mathf.Min(min,vertices[j].y);max=Mathf.Max(max,vertices[j].y);center+=vertices[j]/24;}
    if(max-min<1&&Vector3.Distance(camera,transform.TransformPoint(center))>1500)continue;
    keep.Add(triangles[i]);keep.Add(triangles[i+1]);keep.Add(triangles[i+2]);
   }
   var mesh=UnityEngine.Object.Instantiate(source);mesh.triangles=keep.ToArray();return mesh;
  }
  void Restore(){
   if(restored)return;restored=true;renderFreeze?.Dispose();renderFreeze=null;
   if(frozenInput){QualitySettings.vSyncCount=savedVsync;Application.targetFrameRate=savedFrameLimit;if(director!=null)director.enabled=true;if(frozenMenu!=null)frozenMenu.enabled=true;}
   foreach(var r in chunkSources)if(r!=null)r.enabled=true;foreach(var r in chunkRenderers)if(r!=null)Destroy(r.gameObject);foreach(var m in chunkMeshes)if(m!=null)Destroy(m);
   if(buildings!=null)for(int i=0;i<buildings.Length;i++)if(buildings[i]!=null)buildings[i].enabled=flags[i];
   report.restored=buildings==null||buildings.Select((r,i)=>r!=null&&r.enabled==flags[i]).All(x=>x);
   if(maskSaved&&camera!=null){camera.cullingMask=originalMask;report.restored&=camera.cullingMask==originalMask;}
   if(chase!=null)chase.enabled=chaseEnabled;
   batches.Dispose();setPass.Dispose();triangles.Dispose();
   if(hadPrefs)PlayerPrefs.SetString(LocalRaceConfig.PreferenceKey,prefs);else PlayerPrefs.DeleteKey(LocalRaceConfig.PreferenceKey);PlayerPrefs.Save();
  }
  void Finish(Exception error){Restore();report.failure=error?.ToString();report.completed=error==null;File.WriteAllText(Path.Combine(output,"render-report.json"),JsonUtility.ToJson(report,true));if(error!=null)UnityEngine.Debug.LogException(error);Application.Quit(error==null?0:1);}
  void OnApplicationQuit(){Restore();if(liveProbe!=null){report.completed=liveProbe.Completed&&liveProbe.Failure==null;report.failure=liveProbe.Failure;File.WriteAllText(Path.Combine(output,"render-report.json"),JsonUtility.ToJson(report,true));}}
 }
}

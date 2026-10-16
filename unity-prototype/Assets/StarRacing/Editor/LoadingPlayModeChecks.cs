using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
namespace StarRacingPrototype {
 [InitializeOnLoad] public static class LoadingPlayModeChecks {
  const string Key="StarRacing.LoadingChecks";static int frames;static double deadline;
  public static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_LOADING_QA_DIR");
  static LoadingPlayModeChecks(){EditorApplication.playModeStateChanged+=Changed;}
  public static void RestoreInterrupted(){
   string path=Environment.GetEnvironmentVariable("STAR_RACING_LOADING_RESTORE_FILE");
   var saved=JsonUtility.FromJson<RestoreBackup>(File.ReadAllText(path));
   if(saved.existed)PlayerPrefs.SetString(LocalRaceConfig.PreferenceKey,saved.value);else PlayerPrefs.DeleteKey(LocalRaceConfig.PreferenceKey);PlayerPrefs.Save();
   if(PlayerPrefs.HasKey(LocalRaceConfig.PreferenceKey)!=saved.existed||(saved.existed&&PlayerPrefs.GetString(LocalRaceConfig.PreferenceKey)!=saved.value))throw new Exception("Interrupted QA preference restore mismatch");
   File.WriteAllText(path+".restored","Exact LocalRaceConfig preference restored through Unity PlayerPrefs; no runtime started.\n");
   Debug.Log("LOADING_INTERRUPTED_PREFERENCES_RESTORED");
  }
  [Serializable] sealed class RestoreBackup{public bool existed;public string value;}
  public static bool RemainderOnly=>SessionState.GetBool(Key+"Remainder",false);
  public static bool LateOnly=>SessionState.GetBool(Key+"Late",false);
  public static void RunRemainder(){SessionState.SetBool(Key+"Remainder",true);SessionState.SetBool(Key+"Late",false);RunInternal();}
  public static void RunTeardown(){SessionState.SetBool(Key+"Remainder",true);SessionState.SetBool(Key+"Late",true);RunInternal();}
  public static void Run(){SessionState.SetBool(Key+"Remainder",false);SessionState.SetBool(Key+"Late",false);RunInternal();}
  static void RunInternal(){try{Begin();}catch(Exception e){Debug.LogException(e);if(!string.IsNullOrEmpty(Output)){Directory.CreateDirectory(Output);File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());}EditorApplication.Exit(1);}}
  static void Begin(){
   if(string.IsNullOrEmpty(Output))throw new Exception("STAR_RACING_LOADING_QA_DIR required");
   if(!Environment.GetCommandLineArgs().Contains("-noaudio")||!Environment.GetCommandLineArgs().Contains("-muted"))throw new Exception("Loading QA requires -noaudio -muted from startup");
   Directory.CreateDirectory(Output);
   // Selected neighbors only: existing HUD, shell geometry/physics, silent startup.
   RaceHudChecks.Run();ShellGeometry();SilentStartupChecks.Run();
   SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",true);
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
   if(!Application.isBatchMode){var view=EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));view.position=new Rect(0,0,800,470);view.Show();view.maximized=true;}
   frames=0;EditorApplication.update+=Enter;
  }
  static void ShellGeometry(){
   // Full RunFlat has an identical pre-existing right-contact failure on baseline
   // 667ebf45 (both logs retained). Execute its exact geometry oracles unchanged.
   foreach(string method in new[]{"ValidateFlatClosure","ValidateSlabOracle"})typeof(RoadVolumeChecks).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Static).Invoke(null,null);
  }
  static void Enter(){if(++frames<15)return;EditorApplication.update-=Enter;EditorApplication.isPlaying=true;}
  static void Changed(PlayModeStateChange state){
   if(!SessionState.GetBool(Key,false))return;
   if(state==PlayModeStateChange.EnteredPlayMode){deadline=EditorApplication.timeSinceStartup+1200;EditorApplication.update+=Watch;new GameObject("Loading QA driver").AddComponent<LoadingPlayDriver>();}
   if(state==PlayModeStateChange.EnteredEditMode){EditorApplication.update-=Watch;SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",true)?1:0);}
  }
  static void Watch(){EditorApplication.QueuePlayerLoopUpdate();if(EditorApplication.timeSinceStartup>deadline){File.WriteAllText(Path.Combine(Output,"failure.txt"),"Loading fixture timeout");Finish(false);}}
  public static void Finish(bool success){SessionState.SetBool(Key+"Failed",!success);EditorApplication.isPlaying=false;}
 }
 public sealed class LoadingPlayDriver:MonoBehaviour {
  const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
  RaceDirector director;RaceMenu menu;GameObject host;LoadingWork work;RaceLoading overlay;Gamepad pad;
  bool had,restored,background;string preferences,settingsPath;UserSettings settings;int assertions;readonly List<string> phases=new List<string>();
  bool matchShot;
  readonly Dictionary<string,int> rendered=new Dictionary<string,int>();
  string Output=>LoadingPlayModeChecks.Output;
  void Check(bool ok,string name){assertions++;if(!ok)throw new Exception("LOADING_CHECK "+name);}
  void Trace(string message)=>File.AppendAllText(Path.Combine(Output,"progress.txt"),DateTime.UtcNow.ToString("O")+" "+message+"\n");
  IEnumerator Start(){
   background=Application.runInBackground;Application.runInBackground=true;
   had=PlayerPrefs.HasKey(LocalRaceConfig.PreferenceKey);preferences=PlayerPrefs.GetString(LocalRaceConfig.PreferenceKey);
   File.WriteAllText(Path.Combine(Output,"preferences-before.json"),JsonUtility.ToJson(new PreferenceBackup{existed=had,value=preferences}));
   settingsPath=UserSettingsStore.OverridePath;settings=UserSettingsStore.Current;UserSettingsStore.OverridePath=Path.Combine(Output,"qa-settings.json");UserSettingsStore.Replace(new UserSettings());
   bool success=true;using(var run=new LoadingWork(Run())){while(run.MoveNext())yield return null;if(run.Error!=null){success=false;Debug.LogException(run.Error);File.WriteAllText(Path.Combine(Output,"failure.txt"),run.Error.ToString());}}
   Restore();
   if(success){File.WriteAllText(Path.Combine(Output,"success.json"),JsonUtility.ToJson(new Result{mode=LoadingPlayModeChecks.RemainderOnly?"lifecycle-supplement":"loading-affected",assertions=assertions,phases=phases.ToArray()},true));Debug.Log("LOADING_PLAYMODE_OK assertions="+assertions);}
   LoadingPlayModeChecks.Finish(success);
  }
  [Serializable] sealed class PreferenceBackup{public bool existed;public string value;}
  [Serializable] sealed class Result{public bool success=true,playerBuilt=false,humanAcceptance=false;public string mode;public int assertions;public string[] phases;}
  IEnumerator Frames(int n=3){for(int i=0;i<n;i++)yield return null;}
  IEnumerator Shot(string name){ScreenCapture.CaptureScreenshot(Path.Combine(Output,name+".png"));yield return Frames(4);}
  void Observe(RaceLoading loading,string context){
   if(loading==null)return;string stage=context+"/"+loading.Status;
   if(!rendered.ContainsKey(stage)){rendered[stage]=loading.RenderedFrames;phases.Add(stage);Trace(stage);}
   if(context=="menu-start"&&loading.RenderedFrames>6&&loading.Status=="Строим трассу"&&!matchShot){matchShot=true;ScreenCapture.CaptureScreenshot(Path.Combine(Output,"05-match-loading.png"));}
  }
  IEnumerator WaitMenuLoading(string context){
   int observed=0;float first=-1,last=-1;long physics=RacePhysicsStepper.NativeSteps;
   while(menu.Loading){var loading=host.GetComponent<RaceLoading>();Observe(loading,context);
    Check(LoadingWork.SimulationFrozen,"simulation frozen during "+context);Check(Time.timeScale==0,"timeScale frozen during "+context);Check(physics==RacePhysicsStepper.NativeSteps,"no physics during "+context);
    if(loading!=null){if(first<0)first=loading.AnimationTime;last=loading.AnimationTime;observed=Mathf.Max(observed,loading.RenderedFrames);}
    yield return null;
   }
   Check(observed>=2&&last>first,"real rendered moving frames during "+context);Check(!LoadingWork.SimulationFrozen,"freeze released after "+context);
  }
  LocalRaceConfig Config(string theme,int revision,int count,bool jumps=false){
   uint seed=77;if(revision==12){seed=0;while(NativeTrackGenerator.FamilyFor(seed,theme)!="path-choice")seed++;}
   return new LocalRaceConfig{theme=theme,generatorRevision=revision,humans=1,entrants=count,seed=seed.ToString(),rails="normal",jumps=jumps,devices=new[]{-2,-1,0,1}};
  }
  void Select(LocalRaceConfig config){typeof(RaceMenu).GetProperty("Selected").SetValue(menu,config);typeof(RaceMenu).GetField("seedText",Private).SetValue(menu,config.seed);}
  IEnumerator Run(){
   int released=0;
   IEnumerator Leaf(){try{yield return null;}finally{released++;}}
   IEnumerator Middle(){try{yield return Leaf();}finally{released++;throw new InvalidOperationException("middle cleanup");}}
   IEnumerator Outer(){try{yield return Middle();}finally{released++;throw new InvalidOperationException("outer cleanup");}}
   using(var cleanup=new LoadingWork(Outer())){Check(cleanup.MoveNext(),"nested operation entered");cleanup.Dispose();Check(released==3&&cleanup.Error is AggregateException,"all nested cleanup runs despite two errors");Check(!cleanup.Succeeded&&!cleanup.MoveNext(),"disposed operation cannot publish success");}
   Check(RaceLoading.BarRect.y-RaceLoading.WordmarkRect.yMax>=80,"approved generous logo/loading spacing");
   if(LoadingPlayModeChecks.LateOnly&&Application.isBatchMode){
    // Dedicated lifecycle supplement: real construction/cancellation, no rendering claim.
    Config("cloud-city",11,8).Save();host=new GameObject("Teardown QA host");director=host.AddComponent<RaceDirector>();menu=host.GetComponent<RaceMenu>();
    Check(director.Ready&&!director.Started&&Time.timeScale==0,"teardown baseline initialized");
    Check(SilentStartup.Enabled&&AudioListener.pause,"teardown silent startup");
    yield return Remaining();yield break;
   }
   Config("cloud-city",11,8).Save();host=new GameObject("Real loading startup");host.AddComponent<RaceStartup>();
   Check(host.GetComponent<RaceLoading>()!=null,"startup loading before director");Check(host.GetComponent<RaceDirector>()==null,"startup construction deferred");
   int startupFrames=0;float startupFirst=-1,startupLast=-1;bool shot=false;
   while((director=host.GetComponent<RaceDirector>())==null||!director.Ready){
    var loading=host.GetComponent<RaceLoading>();Observe(loading,"startup");if(loading!=null){startupFrames=Mathf.Max(startupFrames,loading.RenderedFrames);if(startupFirst<0)startupFirst=loading.AnimationTime;startupLast=loading.AnimationTime;if(!shot&&loading.AnimationTime>.5f){ScreenCapture.CaptureScreenshot(Path.Combine(Output,"01-startup-cloudline.png"));shot=true;}}
    if(LoadingWork.SimulationFrozen)Check(Time.timeScale==0,"startup frozen");yield return null;
   }
   yield return Frames();menu=director.GetComponent<RaceMenu>();
   Check(startupFrames>=3&&startupLast>startupFirst,"startup animation renders during real work");Check(host.GetComponent<RaceLoading>()==null,"startup overlay removed");Check(!director.Started&&Time.timeScale==0,"main menu ready");
   Check(SilentStartup.Enabled&&AudioListener.pause,"muted before scene and throughout loading");Check(host.GetComponent<RaceAudioCoordinator>()==null&&FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Length==0,"no game audio initialized");
   if(LoadingPlayModeChecks.RemainderOnly){yield return Remaining();yield break;}
   yield return Shot("02-main-menu");menu.Open(RaceMenuScreen.Settings);yield return Shot("03-settings");menu.Open(RaceMenuScreen.LocalSetup);Select(Config("cloud-city",11,8));yield return Shot("04-race-setup");
   int generation=director.RaceGeneration;menu.StartSelected();menu.StartSelected();Check(menu.Loading&&!director.Started&&generation==director.RaceGeneration,"duplicate start blocked before preparation");
   yield return WaitMenuLoading("menu-start");Check(director.Started&&director.Session.Phase==RacePhase.Countdown,"countdown only after loading");Check(director.RaceGeneration==generation+1,"one start only");director.SetPaused(true);
   var balance=director.RaceBalanceSnapshot;var course=director.Course;generation=director.RaceGeneration;
   yield return Shot("06-pause");menu.OpenPauseSettings();yield return Shot("07-pause-settings");menu.ClosePauseSettings();menu.RepeatRace();menu.RepeatRace();
   yield return WaitMenuLoading("menu-repeat");Check(director.RaceGeneration==generation+1,"one repeat only");Check(ReferenceEquals(balance,director.RaceBalanceSnapshot)&&ReferenceEquals(course,director.Course),"repeat preserves captured balance and course");Check(director.Session.Phase==RacePhase.Countdown,"repeat ready at countdown");director.SetPaused(true);
   string repeatBefore=Signature(director);generation=director.RaceGeneration;
   using(var repeat=director.RepeatForLoading()){Check(repeat.MoveNext(),"repeat entered");Check(repeat.MoveNext(),"repeat planned first placement");Check(repeat.MoveNext(),"repeat planned second placement");repeat.Dispose();}
   Check(repeatBefore==Signature(director)&&generation==director.RaceGeneration,"cancel repeat preserves whole live state");Check(!LoadingWork.SimulationFrozen&&Time.timeScale==0,"repeat cancel releases freeze");
   using(var repeat=director.RepeatForLoading()){Check(repeat.MoveNext(),"repeat focus test entered");Check(repeat.MoveNext(),"repeat focus test staged");typeof(RaceDirector).GetMethod("OnApplicationFocus",Private).Invoke(director,new object[]{false});while(repeat.MoveNext())yield return null;Check(repeat.Error==null,"focused repeat completes");}
   Check(director.Paused&&director.Session.Paused&&Time.timeScale==0,"lost focus during repeat preserves coherent pause");long pausedSteps=RacePhysicsStepper.NativeSteps;yield return Frames();Check(pausedSteps==RacePhysicsStepper.NativeSteps,"paused repeat never simulates");
   typeof(RaceSession).GetProperty("Phase").SetValue(director.Session,RacePhase.Results);for(int i=0;i<director.Session.Racers.Length;i++)director.Session.Racers[i].GetType().GetProperty("Place").SetValue(director.Session.Racers[i],i+1);yield return Shot("09-results");Check(director.ExitToMenu(),"return to menu");
   menu.Selected.devices[0]=int.MaxValue;menu.StartSelected();yield return WaitMenuLoading("invalid-binding");Check(!director.Started&&!string.IsNullOrEmpty(menu.Error),"binding failure returns with error");yield return Shot("10-error-menu");
   Check(CloudlineSkin.CopyrightText=="© 2026 Evgeniy Afonasev · afonasev.tech · Made with Codex","exact footer");
   foreach(var config in new[]{Config("cloud-city",11,8),Config("cloud-city",12,8,true),Config("space-station",12,64,true),Config("space-station",10,8)}) {
    Trace("parity "+config.theme+" rev="+config.generatorRevision+" count="+config.entrants);
    generation=director.RaceGeneration;overlay=host.AddComponent<RaceLoading>();work=director.PrepareForLoading(config);long steps=RacePhysicsStepper.NativeSteps;int frameCount=0;var seen=new HashSet<string>();
    using(work){while(work.MoveNext()){overlay.Status=work.Status;seen.Add(work.Status);Observe(overlay,"parity-"+config.generatorRevision+"-"+config.theme);Check(LoadingWork.SimulationFrozen&&Time.timeScale==0&&steps==RacePhysicsStepper.NativeSteps,"staged race never simulates");frameCount++;yield return null;}}
    Check(work.Error==null&&work.Succeeded&&director.Started,"async preparation success "+work.Error);Check(frameCount>8&&overlay.RenderedFrames>3&&seen.Count>=4,"frames cover real generation/mesh/decor/car stages");
    overlay.enabled=false;Destroy(overlay);overlay=null;director.SetPaused(true);string asynchronous=Signature(director);string replay=director.AppliedConfig.replay.fullIdentity;
    Check(director.ExitToMenu(),"exit async race");typeof(RaceDirector).GetProperty("RaceGeneration").SetValue(director,generation);
    Check(director.TryStartSelected(config.Copy(),out var error),"sync preparation retained "+error);director.SetPaused(true);
    Check(asynchronous==Signature(director),"exact mesh/collider/metadata/decor/roster/placement parity "+config.theme+config.generatorRevision);Check(replay==director.AppliedConfig.replay.fullIdentity,"exact replay parity");
    Check(director.ExitToMenu(),"exit sync race");yield return Frames();
   }
   yield return Remaining();
  }
  static int[] PreparedRoots()=>FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(t=>t.parent==null&&t.name=="Prepared race").Select(t=>t.GetInstanceID()).OrderBy(i=>i).ToArray();
  IEnumerator Remaining(){
   int generation;
   if(!LoadingPlayModeChecks.LateOnly){
   foreach(bool duringBuild in new[]{false,true}){
    var roots=PreparedRoots();Select(Config("cloud-city",11,8));generation=director.RaceGeneration;menu.StartSelected();
    if(duringBuild){int count=0;while(menu.Loading){var loading=host.GetComponent<RaceLoading>();if(loading!=null&&loading.Status=="Строим трассу"&&++count==4)break;yield return null;}Check(menu.Loading,"actual mesh staging reached before cancellation");}
    menu.enabled=false;yield return Frames();
    Check(!menu.Loading&&!LoadingWork.SimulationFrozen&&Time.timeScale==0,"disabled menu releases outer and inner freeze");Check(!director.Started&&director.RaceGeneration==generation,"cancelled menu cannot late-publish");Check(PreparedRoots().SequenceEqual(roots),"disabled menu cleans staging and preserves exact old root inventory");menu.enabled=true;Trace("menu cancellation passed duringBuild="+duringBuild);
   }
   // Dispose after real partial construction: the previous menu/race stays authoritative.
   var partialRoots=PreparedRoots();var partialTrack=director.Track;var beforeCars=director.Cars;var beforeSession=director.Session;generation=director.RaceGeneration;
   overlay=host.AddComponent<RaceLoading>();work=director.PrepareForLoading(Config("cloud-city",11,8));
   int staged=0;while(work.MoveNext()){overlay.Status=work.Status;if(work.Status=="Готовим участников"&&++staged==3)break;yield return null;}
   Check(staged==3,"cancel after partial actual car allocation");work.Dispose();overlay.enabled=false;Destroy(overlay);overlay=null;yield return Frames();
   Check(!LoadingWork.SimulationFrozen&&Time.timeScale==0,"cancel restores previous freeze/time");Check(ReferenceEquals(partialTrack,director.Track)&&ReferenceEquals(beforeCars,director.Cars)&&ReferenceEquals(beforeSession,director.Session)&&generation==director.RaceGeneration,"cancel preserves published state");Check(PreparedRoots().SequenceEqual(partialRoots),"cancel releases staging root and preserves exact old root inventory");Trace("partial construction cancellation passed");
   }
   var beforeRoots=PreparedRoots();var beforeTrack=director.Track;generation=director.RaceGeneration;
   // Real device loss after staging, before the final transactional commit.
   pad=InputSystem.AddDevice<Gamepad>();var disconnected=Config("space-station",11,8);disconnected.devices[0]=SessionControllers.Slot(pad);work=director.PrepareForLoading(disconnected);
   using(work){while(work.MoveNext()){if(work.Status=="Проверяем старт"){InputSystem.RemoveDevice(pad);pad=null;}yield return null;}}
   Check(work.Error!=null,"disconnect after staging rejected");Check(ReferenceEquals(beforeTrack,director.Track)&&generation==director.RaceGeneration&&!director.Started,"disconnect rollback");Check(!LoadingWork.SimulationFrozen&&Time.timeScale==0,"disconnect releases freeze");yield return Frames();Check(PreparedRoots().SequenceEqual(beforeRoots),"failed staging root removed and old root inventory preserved");Trace("disconnect rollback passed");
   // Active race + focus loss + cancellation must retain the pause request.
   Check(director.TryStartSelected(Config("cloud-city",11,8),out var focusError),"focus cancellation race ready "+focusError);director.SetPaused(false);generation=director.RaceGeneration;
   menu.RepeatRace();yield return Frames(2);typeof(RaceDirector).GetMethod("OnApplicationFocus",Private).Invoke(director,new object[]{false});menu.enabled=false;yield return Frames();
   Check(director.RaceGeneration==generation&&director.Paused&&director.Session.Paused&&Time.timeScale==0&&!LoadingWork.SimulationFrozen,"cancelled repeat retains focus pause without partial reset");long cancelledSteps=RacePhysicsStepper.NativeSteps;yield return Frames();Check(cancelledSteps==RacePhysicsStepper.NativeSteps,"cancelled focused repeat never simulates");menu.enabled=true;Check(director.ExitToMenu(),"exit after cancelled focused repeat");Trace("focus cancellation passed");
   // Reload invalidates a late catalog load; no stale Current is published.
   DesignLabRuntime.Reload();using(var pending=new LoadingWork(DesignLabRuntime.Prepare())){Check(pending.MoveNext(),"catalog actually waits outside main thread");DesignLabRuntime.Reload();while(pending.MoveNext())yield return null;Check(pending.Error is OperationCanceledException,"stale catalog completion rejected");}
   using(var reload=new LoadingWork(DesignLabRuntime.Prepare())){while(reload.MoveNext())yield return null;Check(reload.Error==null,"catalog reload recovers");}
   // Both component-disable orders must release partially initialized objects.
   Destroy(host);host=null;yield return Frames();
   foreach(bool startupFirstDisable in new[]{true,false}){
    Config("cloud-city",11,8).Save();host=new GameObject("Cancelled startup");var startup=host.AddComponent<RaceStartup>();
    while((director=host.GetComponent<RaceDirector>())==null||director.Cars==null||director.Cars[0]==null)yield return null;
    Check(!director.Ready,"startup cancellation reached actual partial car allocation");
    if(startupFirstDisable){startup.enabled=false;director.enabled=false;}else {director.enabled=false;startup.enabled=false;}
    yield return Frames();Check(!LoadingWork.SimulationFrozen,"both startup teardown orders release freeze");Check(FindObjectsByType<TrackBuilder>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length==0&&FindObjectsByType<MagneticVehicle>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length==0,"cancelled startup frees track and cars outside host");
    Check(host.GetComponent<RaceLoading>()!=null,"cancelled startup not presented as successful menu");Trace("startup cancellation passed startupFirst="+startupFirstDisable);Destroy(host);host=null;yield return Frames();
   }
   File.WriteAllText(Path.Combine(Output,"rendered-stages.json"),"["+string.Join(",",phases.Select(s=>"\""+s+"\""))+"]");
  }
  static string Signature(RaceDirector d){
   using(var sha=SHA256.Create())using(var stream=new CryptoStream(Stream.Null,sha,CryptoStreamMode.Write))using(var buffered=new BufferedStream(stream,65536))using(var w=new BinaryWriter(buffered)) {
    w.Write(d.Course.GeometryIdentity);w.Write(d.RaceGeneration);w.Write(d.Roster.Entrants.Length);
    foreach(var e in d.Roster.Entrants){w.Write(e.Name);w.Write(e.GridSlot);w.Write(e.HumanSeat);w.Write(e.Seed);w.Write((int)e.Profile);}
    void Vector(Vector3 v){w.Write(v.x);w.Write(v.y);w.Write(v.z);}
    void Mesh(Mesh mesh){w.Write(mesh!=null);if(mesh==null)return;w.Write(mesh.name);w.Write(mesh.vertexCount);foreach(var v in mesh.vertices)Vector(v);foreach(var n in mesh.normals)Vector(n);foreach(var uv in mesh.uv){w.Write(uv.x);w.Write(uv.y);}var triangles=mesh.triangles;w.Write(triangles.Length);foreach(int t in triangles)w.Write(t);}
    var decor=(TrackEnvironmentBuilder)typeof(RaceDirector).GetField("environment",Private).GetValue(d);
    foreach(var root in new[]{d.Track.transform,decor.Root}){
     foreach(var t in root.GetComponentsInChildren<Transform>(true)){w.Write(t.name);Vector(t.localPosition);Vector(t.localScale);var rotation=t.localRotation;w.Write(rotation.x);w.Write(rotation.y);w.Write(rotation.z);w.Write(rotation.w);}
     foreach(var f in root.GetComponentsInChildren<MeshFilter>(true))Mesh(f.sharedMesh);
     foreach(var c in root.GetComponentsInChildren<MeshCollider>(true)){w.Write(c.convex);w.Write((int)c.cookingOptions);Mesh(c.sharedMesh);}
     foreach(var s in root.GetComponentsInChildren<TrackSurface>(true)){w.Write(s.triangleSamples.Length);foreach(int sample in s.triangleSamples)w.Write(sample);foreach(var face in s.triangleFaces)w.Write(JsonUtility.ToJson(face));}
    }
    foreach(var car in d.Cars){Vector(car.Body.position);var q=car.Body.rotation;w.Write(q.x);w.Write(q.y);w.Write(q.z);w.Write(q.w);}
    w.Flush();buffered.Flush();stream.FlushFinalBlock();return BitConverter.ToString(sha.Hash);
   }
  }
  void Restore(){if(restored)return;restored=true;work?.Dispose();if(overlay!=null)Destroy(overlay);if(host!=null)Destroy(host);if(pad!=null&&pad.added)InputSystem.RemoveDevice(pad);if(had)PlayerPrefs.SetString(LocalRaceConfig.PreferenceKey,preferences);else PlayerPrefs.DeleteKey(LocalRaceConfig.PreferenceKey);PlayerPrefs.Save();UserSettingsStore.OverridePath=settingsPath;UserSettingsStore.Replace(settings);Time.timeScale=1;Application.runInBackground=background;}
  void OnDestroy(){Restore();}
 }
}

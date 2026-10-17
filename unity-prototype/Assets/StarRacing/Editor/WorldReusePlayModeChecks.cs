using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Collections;
namespace StarRacingPrototype {
 [InitializeOnLoad] public static class WorldReusePlayModeChecks {
  const string Key="StarRacing.WorldReuseChecks";static double deadline;
  public static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_WORLD_QA_DIR");
  static WorldReusePlayModeChecks(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run(){SessionState.SetBool(Key+"Assets",false);RunInternal();}
  public static void RunLastAssets(){SessionState.SetBool(Key+"Assets",true);RunInternal();}
  static void RunInternal(){
   if(string.IsNullOrEmpty(Output))throw new Exception("STAR_RACING_WORLD_QA_DIR required");
   if(!Environment.GetCommandLineArgs().Contains("-noaudio")||!Environment.GetCommandLineArgs().Contains("-muted"))throw new Exception("Silent startup required");
   Directory.CreateDirectory(Output);SilentStartupChecks.Run();DesktopUpdateChecks.Run();
   foreach(string name in new[]{"ValidateFlatClosure","ValidateSlabOracle"})typeof(RoadVolumeChecks).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Static).Invoke(null,null);
   SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",true);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.isPlaying=true;
  }
  static void Changed(PlayModeStateChange state){
   if(!SessionState.GetBool(Key,false))return;
   if(state==PlayModeStateChange.EnteredPlayMode){deadline=EditorApplication.timeSinceStartup+1200;EditorApplication.update+=Watch;var host=new GameObject("World reuse QA");if(SessionState.GetBool(Key+"Assets",false))host.AddComponent<LastWorldAssetsPlayDriver>();else host.AddComponent<WorldReusePlayDriver>();}
   if(state==PlayModeStateChange.EnteredEditMode){EditorApplication.update-=Watch;SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",true)?1:0);}
  }
  static void Watch(){EditorApplication.QueuePlayerLoopUpdate();if(EditorApplication.timeSinceStartup>deadline){File.WriteAllText(Path.Combine(Output,"failure.txt"),"World reuse timeout");Finish(false);}}
  public static void Finish(bool success){SessionState.SetBool(Key+"Failed",!success);EditorApplication.isPlaying=false;}
 }
 public sealed class WorldReusePlayDriver:MonoBehaviour {
  const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
  GameObject host;RaceDirector director;RaceMenu menu;LoadingWork work;Gamepad pad;
  bool had,restored,background;string preferences,settingsPath;UserSettings settings;int assertions;
  readonly List<string> results=new List<string>();
  readonly HashSet<int> roadIds=new HashSet<int>();int roadContactPairs;
  string Output=>WorldReusePlayModeChecks.Output;
  int RetainedCount=>(int)typeof(RaceDirector).GetProperty("RetainedWorldCount",Private).GetValue(director);
  bool Prewarming=>(bool)typeof(RaceDirector).GetProperty("WorldPrewarming",Private).GetValue(director);
  static double BudgetSeconds=>(double)typeof(LoadingWork).GetProperty("BudgetSeconds",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
  void LeaseChecks(){
   var memory=typeof(RaceDirector).GetMethod("CacheMemoryUsage",BindingFlags.NonPublic|BindingFlags.Static);
   Check((long)memory.Invoke(null,new object[]{123L})==123,"Engine allocation reading retained");
   Check((long)memory.Invoke(null,new object[]{0L})>0,"No-Profiler fallback reads actual process memory");
   var type=typeof(LoadingWork).GetNestedType("Freeze",BindingFlags.NonPublic);float time=Time.timeScale;
   var ordinary=(IDisposable)Activator.CreateInstance(type,new object[]{false});var throughput=(IDisposable)Activator.CreateInstance(type,new object[]{true});
   Check(LoadingWork.SimulationFrozen&&BudgetSeconds==.012,"Explicit preparation upgrades existing budgets");throughput.Dispose();throughput.Dispose();
   Check(LoadingWork.SimulationFrozen&&BudgetSeconds==.004,"Double disposal restores speculative budget only");ordinary.Dispose();
   Check(!LoadingWork.SimulationFrozen&&Time.timeScale==time,"Nested leases restore time and counters");
   try{using((IDisposable)Activator.CreateInstance(type,new object[]{true}))throw new InvalidOperationException("lease unwind");}catch(InvalidOperationException){}
   Check(!LoadingWork.SimulationFrozen&&BudgetSeconds==.004,"Exception releases throughput lease");
  }
  void Check(bool ok,string message){assertions++;if(!ok)throw new Exception(message);}
  void Trace(string message){results.Add(message);File.AppendAllText(Path.Combine(Output,"progress.txt"),DateTime.UtcNow.ToString("O")+" "+message+"\n");}
  static LocalRaceConfig Config(string theme,uint seed,int revision=12,int count=32)=>new LocalRaceConfig{theme=theme,seed=seed.ToString(),generatorRevision=revision,entrants=count,humans=1,jumps=true,rails="normal",devices=new[]{-2,-1,0,1}};
  void Select(LocalRaceConfig config){typeof(RaceMenu).GetProperty("Selected").SetValue(menu,config);typeof(RaceMenu).GetField("seedText",Private).SetValue(menu,config.seed);}
  IEnumerator Start(){
   had=PlayerPrefs.HasKey(LocalRaceConfig.PreferenceKey);preferences=PlayerPrefs.GetString(LocalRaceConfig.PreferenceKey);background=Application.runInBackground;Application.runInBackground=true;
   File.WriteAllText(Path.Combine(Output,"preferences-before.json"),"{\"existed\":"+(had?"true":"false")+",\"value\":"+Quote(preferences)+"}");
   settingsPath=UserSettingsStore.OverridePath;settings=UserSettingsStore.Current;UserSettingsStore.OverridePath=Path.Combine(Output,"settings.json");UserSettingsStore.Replace(new UserSettings());
   bool success;using(var run=new LoadingWork(Run())){while(run.MoveNext())yield return null;success=run.Succeeded&&run.Error==null;if(!success){Debug.LogException(run.Error);File.WriteAllText(Path.Combine(Output,"failure.txt"),run.Error?.ToString()??"Incomplete");}}
   Restore();yield return null;
   if(success){File.WriteAllText(Path.Combine(Output,"success.json"),"{\"success\":true,\"assertions\":"+assertions+",\"playerBuilt\":false,\"humanAcceptance\":false,\"results\":["+string.Join(",",results.Select(Quote))+"]}");Debug.Log("WORLD_REUSE_OK assertions="+assertions);}
   WorldReusePlayModeChecks.Finish(success);
  }
  static string Quote(string value)=>"\""+(value??"").Replace("\\","\\\\").Replace("\"","\\\"").Replace("\n","\\n").Replace("\r","\\r")+"\"";
  IEnumerator Frames(int count=3){for(int i=0;i<count;i++)yield return null;}
  void Contacts(PhysicsScene scene,NativeArray<ModifiableContactPair> pairs){foreach(var pair in pairs)if(roadIds.Contains(pair.colliderInstanceID)||roadIds.Contains(pair.otherColliderInstanceID))System.Threading.Interlocked.Add(ref roadContactPairs,pair.contactCount);}
  IEnumerator ExerciseContacts(){
   roadIds.Clear();foreach(var collider in director.Track.GetComponentsInChildren<MeshCollider>())roadIds.Add(collider.GetInstanceID());roadContactPairs=0;
   Physics.ContactModifyEvent+=Contacts;Physics.ContactModifyEventCCD+=Contacts;
   try{director.SetPaused(false);float until=Time.realtimeSinceStartup+30;long before=RacePhysicsStepper.NativeSteps;
    while((director.Session.Phase!=RacePhase.Racing||director.Session.Elapsed<1||roadContactPairs==0)&&Time.realtimeSinceStartup<until)yield return null;
    director.SetPaused(true);Check(RacePhysicsStepper.NativeSteps>before&&director.Session.Phase==RacePhase.Racing&&director.Session.Elapsed>=1&&roadContactPairs>0,"Reactivated world executes real racing road contacts");Trace("road contacts="+roadContactPairs+" elapsed="+director.Session.Elapsed);
   }finally{Physics.ContactModifyEvent-=Contacts;Physics.ContactModifyEventCCD-=Contacts;}
  }
  IEnumerator Prepare(LocalRaceConfig config){
   var clock=System.Diagnostics.Stopwatch.StartNew();long physics=RacePhysicsStepper.NativeSteps;double maximum=0;var timings=new List<double>();
   work=director.PrepareForLoading(config);
   using(work){while(true){var step=System.Diagnostics.Stopwatch.StartNew();bool pending=work.MoveNext();double seconds=step.Elapsed.TotalSeconds;timings.Add(seconds);maximum=Math.Max(maximum,seconds);if(!pending)break;Check(LoadingWork.SimulationFrozen&&Time.timeScale==0&&physics==RacePhysicsStepper.NativeSteps,"No staged physics");yield return null;}}
   Check(work.Succeeded&&work.Error==null&&director.Started,"Prepare succeeds: "+work.Error);director.SetPaused(true);yield return null;
   Check(BudgetSeconds==.004,"Preparation restores background budget");
   timings.Sort();Trace(config.theme+"/"+config.seed+"/v"+config.generatorRevision+"/"+config.entrants+" seconds="+clock.Elapsed.TotalSeconds.ToString("F3")+" maxStep="+maximum.ToString("F3")+" p95Step="+timings[(int)((timings.Count-1)*.95)].ToString("F3")+" frames="+timings.Count+" worlds="+RetainedCount+" memory="+UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong());work=null;
  }
  string Signature()=> (string)typeof(LoadingPlayDriver).GetMethod("Signature",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{director});
  IEnumerator Run(){
   LeaseChecks();
   Config("cloud-city",3650307150).Save();float start=Time.realtimeSinceStartup;host=new GameObject("Light runtime startup");host.AddComponent<RaceStartup>();
   while((director=host.GetComponent<RaceDirector>())==null||!director.Ready)yield return null;
   menu=host.GetComponent<RaceMenu>();Check(director.Track==null&&director.Course==null&&director.Cars.Length==0&&director.Session==null,"Runtime menu allocates no world or participants");Check(Enumerable.Range(0,4).All(i=>!director.CameraFor(i).GetComponent<Camera>().enabled),"Menu cameras cannot overwrite IMGUI");Check(director.RaceGeneration==1&&!director.Started,"Initial generation preserved");Check(RetainedCount==0&&!LoadingWork.SimulationFrozen,"Light menu ready and freeze released");Check(SilentStartup.Enabled&&AudioListener.pause&&host.GetComponent<RaceAudioCoordinator>()==null,"Silent startup preserved");Trace("menu seconds="+(Time.realtimeSinceStartup-start).ToString("F3"));
   // A partial selected-world build transfers to Start rather than being rebuilt.
   menu.Open(RaceMenuScreen.LocalSetup);Select(Config("cloud-city",3650307150));
   while(!Prewarming)yield return null;
   var request=typeof(RaceDirector).GetField("prewarm",Private).GetValue(director);var pending=(LoadingWork)request.GetType().GetField("Work").GetValue(request);
   while(pending.IsPending&&pending.Status!="Строим трассу")yield return null;
   Check(pending.IsPending,"Prewarm entered actual build");long steps=RacePhysicsStepper.NativeSteps;menu.StartSelected();menu.StartSelected();
   bool upgraded=false;while(menu.Loading){Check(RacePhysicsStepper.NativeSteps==steps,"No physics during adopted preparation");upgraded|=BudgetSeconds==.012;yield return null;}
   Check(upgraded&&BudgetSeconds==.004,"Adoption upgrades budget and releases it");
   Check(pending.Succeeded&&director.Started&&director.RaceGeneration==2,"Adopted operation completes once: pending="+pending.IsPending+" succeeded="+pending.Succeeded+" started="+director.Started+" generation="+director.RaceGeneration+" error="+menu.Error);director.SetPaused(true);Trace("adopted prewarm world ready");
   var cloud=director.Track;var cloudMesh=cloud.GetComponentInChildren<MeshFilter>().sharedMesh;var identity=director.AppliedConfig.replay.fullIdentity;int generation=director.RaceGeneration;string cold=Signature();
   Check(director.ExitToMenu(),"Exit first race");menu.Open(RaceMenuScreen.Main);typeof(RaceDirector).GetProperty("RaceGeneration").SetValue(director,generation-1);
   yield return Prepare(Config("cloud-city",3650307150));
   Check(ReferenceEquals(cloud,director.Track)&&ReferenceEquals(cloudMesh,director.Track.GetComponentInChildren<MeshFilter>().sharedMesh),"Warm start reuses track and cooked meshes");Check(cold==Signature()&&identity==director.AppliedConfig.replay.fullIdentity,"Exact cold/cache geometry collider roster placement replay parity");
   // Reused colliders must still enter real simulation/contact processing.
   yield return ExerciseContacts();
   Check(director.ExitToMenu(),"Exit warm race");menu.Open(RaceMenuScreen.Main);
   // Cancellation on a cache hit must leave the published state untouched.
   var oldSession=director.Session;generation=director.RaceGeneration;work=director.PrepareForLoading(Config("cloud-city",3650307150));int staged=0;
   while(work.MoveNext()){if(work.Status=="Готовим участников"&&++staged==3)break;yield return null;}Check(staged==3,"Real partial participant allocation");work.Dispose();work=null;yield return Frames();
   Check(ReferenceEquals(cloud,director.Track)&&ReferenceEquals(oldSession,director.Session)&&generation==director.RaceGeneration&&!director.Started&&!LoadingWork.SimulationFrozen,"Cache-hit cancellation preserves live state");
   // Corrupt replay is rejected even on a hit, without altering the old world.
   var corrupted=director.AppliedConfig.Copy();corrupted.replay.fullIdentity="corrupt";work=director.PrepareForLoading(corrupted);using(work){while(work.MoveNext())yield return null;}Check(work.Error!=null&&!director.Started&&ReferenceEquals(cloud,director.Track),"Cache cannot bypass replay verification");work=null;
   foreach(var config in new[]{Config("space-station",77),Config("cloud-city",77,11,8),Config("space-station",77,10,8)}){
    yield return Prepare(config);var track=director.Track;string signature=Signature();int g=director.RaceGeneration;Check(director.ExitToMenu(),"Exit cold case");menu.Open(RaceMenuScreen.Main);
    if(config.generatorRevision==12){
     Check(cloud==null,"Historical cloud world released after replacement");
     var session=director.Session;
     using(var borrowed=director.PrepareForLoading(config.Copy())){Check(borrowed.MoveNext()&&borrowed.Status=="Используем готовую трассу","Borrow current at cache-hit yield");typeof(RaceDirector).GetMethod("ReleaseInactiveWorlds",Private).Invoke(director,null);yield return null;Check(track!=null,"Memory pressure pins current candidate before next MoveNext");}
     using(var borrowed=director.PrepareForLoading(config.Copy())){int count=0;while(borrowed.MoveNext()){if(borrowed.Status=="Готовим участников"&&++count==3)break;yield return null;}Check(count==3,"Borrow reaches partial cars");typeof(RaceDirector).GetMethod("ReleaseInactiveWorlds",Private).Invoke(director,null);yield return null;Check(track!=null,"Memory pressure pins partially admitted current world");}
     yield return Frames();Check(ReferenceEquals(track,director.Track)&&ReferenceEquals(session,director.Session)&&track.gameObject.activeInHierarchy&&!LoadingWork.SimulationFrozen,"Borrow rollback preserves current world and authority");
    }
    typeof(RaceDirector).GetProperty("RaceGeneration").SetValue(director,g-1);
    yield return Prepare(config.Copy());Check(ReferenceEquals(track,director.Track)&&signature==Signature(),"Exact warm parity "+config.theme+config.generatorRevision);Check(RetainedCount<=2,"Current plus selected candidate bound");Check(director.ExitToMenu(),"Exit parity");menu.Open(RaceMenuScreen.Main);
   }
   uint graphSeed=0;while(NativeTrackGenerator.FamilyFor(graphSeed,"space-station")!="path-choice")graphSeed++;
   var graph=Config("space-station",graphSeed,12,64);yield return Prepare(graph);Check(director.Track.GraphSurface!=null,"Independent graph case");var graphTrack=director.Track;string graphSignature=Signature();generation=director.RaceGeneration;Check(director.ExitToMenu(),"Exit graph");menu.Open(RaceMenuScreen.Main);typeof(RaceDirector).GetProperty("RaceGeneration").SetValue(director,generation-1);
   yield return Prepare(graph.Copy());Check(ReferenceEquals(graphTrack,director.Track)&&graphSignature==Signature(),"Graph64 exact reused parity");yield return ExerciseContacts();director.ExitToMenu();menu.Open(RaceMenuScreen.Main);
   // Key excludes roster size, but every race still creates the selected roster.
   var resized=graph.Copy();resized.entrants=8;yield return Prepare(resized);Check(ReferenceEquals(graphTrack,director.Track)&&director.Cars.Length==8,"World reuse with fresh participant count");director.ExitToMenu();menu.Open(RaceMenuScreen.Main);
   // A late binding failure must restore borrowed-entry activity and old authority.
   pad=InputSystem.AddDevice<Gamepad>();var disconnected=graph.Copy();disconnected.devices[0]=SessionControllers.Slot(pad);generation=director.RaceGeneration;work=director.PrepareForLoading(disconnected);
   using(work){while(work.MoveNext()){if(work.Status=="Проверяем старт"){InputSystem.RemoveDevice(pad);pad=null;}yield return null;}}Check(work.Error!=null&&generation==director.RaceGeneration&&!director.Started,"Late disconnect rolls back cached preparation");work=null;
   typeof(RaceDirector).GetMethod("ReleaseInactiveWorlds",Private).Invoke(director,null);yield return Frames();Check(RetainedCount==1&&graphTrack!=null,"Low-memory release pins published world");
   // Selection change cancels partial work, without a late completion publishing it.
   menu.Open(RaceMenuScreen.LocalSetup);Select(Config("cloud-city",1001));while(!Prewarming)yield return null;var stale=typeof(RaceDirector).GetField("prewarm",Private).GetValue(director);var staleWork=(LoadingWork)stale.GetType().GetField("Work").GetValue(stale);Select(Config("cloud-city",1002));yield return Frames();Check(!staleWork.IsPending&&!staleWork.Succeeded,"Changed selection cancels owned request");
   while(!Prewarming)yield return null;typeof(RaceDirector).GetMethod("ReleaseInactiveWorlds",Private).Invoke(director,null);yield return Frames(30);Check(!Prewarming&&!LoadingWork.SimulationFrozen,"Memory pressure suppresses reallocation of same selection");
   Select(Config("cloud-city",1003));while(!Prewarming)yield return null;typeof(RaceMenu).GetField("seedText",Private).SetValue(menu,"invalid");yield return Frames();Check(!Prewarming&&!LoadingWork.SimulationFrozen,"Invalid selection releases speculative world and freeze");
   menu.Open(RaceMenuScreen.Main);yield return Frames();Check(!Prewarming&&!LoadingWork.SimulationFrozen,"Leaving setup releases speculative freeze");
   var mesh=graphTrack.GetComponentInChildren<MeshFilter>().sharedMesh;Destroy(host);host=null;yield return Frames();Check(mesh==null&&FindObjectsByType<TrackBuilder>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length==0,"Owner destroys all retained worlds and meshes");Check(!LoadingWork.SimulationFrozen,"Teardown releases all freezes");
  }
  void Restore(){if(restored)return;restored=true;work?.Dispose();if(host!=null)Destroy(host);if(pad!=null&&pad.added)InputSystem.RemoveDevice(pad);if(had)PlayerPrefs.SetString(LocalRaceConfig.PreferenceKey,preferences);else PlayerPrefs.DeleteKey(LocalRaceConfig.PreferenceKey);PlayerPrefs.Save();UserSettingsStore.OverridePath=settingsPath;UserSettingsStore.Replace(settings);Application.runInBackground=background;Time.timeScale=1;}
  void OnDestroy()=>Restore();
 }
 // Focused F1 ownership gate; unchanged broad geometry fixtures remain available.
 public sealed class LastWorldAssetsPlayDriver:MonoBehaviour {
  const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
  GameObject host;RaceDirector director;RaceMenu menu;LoadingWork work;IDisposable lease;
  string prefs,settingsPath;bool had,restored,background;UserSettings settings;int assertions;
  int contactCount;readonly HashSet<int> roads=new HashSet<int>();
  string Output=>WorldReusePlayModeChecks.Output;
  void Check(bool value,string message){assertions++;if(!value)throw new Exception("ASSETS_WORLD "+message);}
  void Trace(string value)=>File.AppendAllText(Path.Combine(Output,"progress.txt"),DateTime.UtcNow.ToString("O")+" "+value+"\n");
  static int AssetCount=>(int)typeof(SharedRaceAssets).GetProperty("RetainedCount",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
  static int Loads=>(int)typeof(SharedRaceAssets).GetProperty("LoadCount",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
  static bool Warm=>(bool)typeof(SharedRaceAssets).GetProperty("Warm",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
  int Worlds=>(int)typeof(RaceDirector).GetProperty("RetainedWorldCount",Private).GetValue(director);
  static LocalRaceConfig Config(string theme,uint seed,int revision)=>new LocalRaceConfig{theme=theme,seed=seed.ToString(),generatorRevision=revision,entrants=8,humans=1,rails="normal",jumps=true,devices=new[]{-2,-1,0,1}};
  void Select(LocalRaceConfig config){typeof(RaceMenu).GetProperty("Selected").SetValue(menu,config);typeof(RaceMenu).GetField("seedText",Private).SetValue(menu,config.seed);}
  object Request=>typeof(RaceDirector).GetField("prewarm",Private).GetValue(director);
  LoadingWork RequestWork(object request)=>(LoadingWork)request.GetType().GetField("Work").GetValue(request);
  string Signature()=>(string)typeof(LoadingPlayDriver).GetMethod("Signature",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{director});
  IEnumerator Frames(int n=3){for(int i=0;i<n;i++)yield return null;}
  IEnumerator Start(){
   had=PlayerPrefs.HasKey(LocalRaceConfig.PreferenceKey);prefs=PlayerPrefs.GetString(LocalRaceConfig.PreferenceKey);background=Application.runInBackground;Application.runInBackground=true;
   File.WriteAllText(Path.Combine(Output,"preferences-before.json"),JsonUtility.ToJson(new Backup{existed=had,value=prefs}));
   settingsPath=UserSettingsStore.OverridePath;settings=UserSettingsStore.Current;UserSettingsStore.OverridePath=Path.Combine(Output,"settings.json");UserSettingsStore.Replace(new UserSettings());
   bool success;using(var run=new LoadingWork(Run())){while(run.MoveNext())yield return null;success=run.Succeeded&&run.Error==null;if(!success){Debug.LogException(run.Error);File.WriteAllText(Path.Combine(Output,"failure.txt"),run.Error?.ToString()??"Incomplete");}}
   Restore();yield return Frames();
   if(success){File.WriteAllText(Path.Combine(Output,"success.json"),"{\"success\":true,\"assertions\":"+assertions+",\"playerBuilt\":false,\"fullSuiteRun\":false,\"humanAcceptance\":false}");Debug.Log("LAST_WORLD_ASSETS_OK assertions="+assertions);}
   WorldReusePlayModeChecks.Finish(success);
  }
  [Serializable] sealed class Backup {public bool existed;public string value;}
  IEnumerator Prepare(LocalRaceConfig config){
   work=director.PrepareForLoading(config);long steps=RacePhysicsStepper.NativeSteps;
   using(work){while(work.MoveNext()){Check(LoadingWork.SimulationFrozen&&steps==RacePhysicsStepper.NativeSteps,"No staged physics");yield return null;}}
   Check(work.Succeeded&&work.Error==null&&director.Started,"Prepared current world "+work.Error);director.SetPaused(true);work=null;yield return null;
  }
  void Contacts(PhysicsScene scene,NativeArray<ModifiableContactPair> pairs){foreach(var pair in pairs)if(roads.Contains(pair.colliderInstanceID)||roads.Contains(pair.otherColliderInstanceID))System.Threading.Interlocked.Add(ref contactCount,pair.contactCount);}
  IEnumerator ContactGate(){
   roads.Clear();foreach(var c in director.Track.GetComponentsInChildren<MeshCollider>())roads.Add(c.GetInstanceID());contactCount=0;
   Physics.ContactModifyEvent+=Contacts;Physics.ContactModifyEventCCD+=Contacts;
   try{director.SetPaused(false);float until=Time.realtimeSinceStartup+30;bool impact=false;long before=RacePhysicsStepper.NativeSteps;
    while((director.Session.Phase!=RacePhase.Racing||director.Session.Elapsed<1||contactCount==0)&&Time.realtimeSinceStartup<until){
     // Suspension can hover forever without chassis-road contact. Exercise the
     // retained cooked collider with a controlled impact, using real simulation.
     if(!impact&&director.Session.Phase==RacePhase.Racing&&director.Session.Elapsed>=1){var car=director.Cars[0];var frame=car.Frame;car.Body.position=frame.position+frame.normal*.5f;car.Body.rotation=frame.Rotation;car.Body.linearVelocity=-frame.normal*8;car.Body.angularVelocity=Vector3.zero;car.ResetPresentation();Physics.SyncTransforms();impact=true;}
     yield return null;
    }
    Check(RacePhysicsStepper.NativeSteps>before,"Reactivated colliders enter real native physics");
    director.SetPaused(true);Check(director.Session.Phase==RacePhase.Racing&&director.Session.Elapsed>=1&&contactCount>0,"Real road contacts after activation phase="+director.Session.Phase+" elapsed="+director.Session.Elapsed+" contacts="+contactCount+" paused="+director.Paused+" frozen="+LoadingWork.SimulationFrozen+" steps="+RacePhysicsStepper.NativeSteps);Trace("contacts="+contactCount);
   }finally{Physics.ContactModifyEvent-=Contacts;Physics.ContactModifyEventCCD-=Contacts;}
  }
  IEnumerator Run(){
   // Pending sync reads finish the same request, never issue Resources.Load again.
   lease=SharedRaceAssets.Acquire();SharedRaceAssets.TickPrewarm();Check(Loads==1,"One asynchronous prefab request");
   int prefab=SharedRaceAssets.Load<GameObject>("Vehicle/Cloudline").GetInstanceID();Check(Loads==1,"Sync tooling consumes same pending request");
   using(var prepare=new LoadingWork(SharedRaceAssets.EnsureSteps(true))){while(prepare.MoveNext())yield return null;Check(prepare.Succeeded&&prepare.Error==null,"Finite manifest preloaded");}
   Trace("manifest preloaded");int count=Loads;Check(Warm&&count==45&&AssetCount==45,"Only 45 shared source assets retained");
   var unload=Resources.UnloadUnusedAssets();while(!unload.isDone)yield return null;
   Check(SharedRaceAssets.Load<GameObject>("Vehicle/Cloudline").GetInstanceID()==prefab&&Loads==count,"Shared source retained independently of worlds");
   lease.Dispose();lease=null;Check(AssetCount==0,"Final owner releases references");
   lease=SharedRaceAssets.Acquire();SharedRaceAssets.TickPrewarm();lease.Dispose();lease=null;yield return Frames();Check(AssetCount==0,"Late completion cannot repopulate released cache");
   lease=SharedRaceAssets.Acquire();var stale=lease;typeof(SharedRaceAssets).GetMethod("Reset",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null);lease=SharedRaceAssets.Acquire();SharedRaceAssets.Load<GameObject>("Vehicle/Cloudline");stale.Dispose();Check(AssetCount==1,"Prior epoch lease cannot clear new session");lease.Dispose();lease=null;
   var a=Config("cloud-city",77,11);var b=Config("space-station",5,12);a.Save();host=new GameObject("Current level and shared sources");host.AddComponent<RaceStartup>();
   while((director=host.GetComponent<RaceDirector>())==null||!director.Ready)yield return null;menu=host.GetComponent<RaceMenu>();
   Check(director.Track==null&&!Warm,"Menu ready before world/assets preparation completes");Check(SilentStartup.Enabled&&host.GetComponent<RaceAudioCoordinator>()==null,"Startup stays silent");
   Trace("asset lifecycle complete");yield return Prepare(a);Trace("initial legacy ready");Check(director.Track.GraphSurface==null,"Legacy case");int loaded=Loads;Check(Warm&&loaded==45,"Race waited for shared imports");
   int modelId=SharedRaceAssets.Load<GameObject>("Vehicle/Cloudline").GetInstanceID();int textureId=SharedRaceAssets.Load<Texture2D>("Environment/Textures/Road").GetInstanceID();var paint=SharedRaceAssets.Load<Material>("Vehicle/CloudlineMaterials/Liquid_Silver");var paintColor=paint.color;
   var oldTrack=director.Track;var oldMesh=oldTrack.GetComponentInChildren<MeshFilter>().sharedMesh;var oldMaterial=oldTrack.GetComponentInChildren<MeshRenderer>().sharedMaterial;
   using(var repeat=director.RepeatForLoading()){while(repeat.MoveNext())yield return null;Check(repeat.Succeeded&&repeat.Error==null,"Repeat succeeds");}director.SetPaused(true);Check(ReferenceEquals(oldTrack,director.Track)&&oldMesh!=null&&Loads==loaded,"Repeat reuses current world and source assets");yield return ContactGate();
   director.ExitToMenu();menu.Open(RaceMenuScreen.Main);string signature=Signature();int generation=director.RaceGeneration;
   work=director.PrepareForLoading(b);int staged=0;while(work.MoveNext()){if(work.Status=="Строим трассу"&&++staged==4)break;yield return null;}Check(staged==4,"Real partial alternate world");work.Dispose();work=null;yield return Frames();
   Check(ReferenceEquals(oldTrack,director.Track)&&signature==Signature()&&generation==director.RaceGeneration&&!LoadingWork.SimulationFrozen,"Cancelled B preserves exact published A");Check(FindObjectsByType<TrackBuilder>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length==1,"Partial candidate released");
   menu.Open(RaceMenuScreen.LocalSetup);Select(b);while(Request==null)yield return null;var cancelled=RequestWork(Request);while(cancelled.Status!="Строим трассу")yield return null;
   Select(Config("cloud-city",78,11));yield return Frames();Check(!cancelled.IsPending&&!cancelled.Succeeded,"Changing candidate cancels old producer");menu.Open(RaceMenuScreen.Main);yield return Frames();Check(Worlds==1&&oldTrack!=null,"Only current level survives leaving setup");
   menu.Open(RaceMenuScreen.LocalSetup);Select(b);while(Request==null)yield return null;var selected=RequestWork(Request);while(selected.IsPending)yield return null;Check(selected.Succeeded&&Worlds==2,"Current plus one completed selected candidate");
   menu.StartSelected();while(menu.Loading){Check(LoadingWork.SimulationFrozen,"Frozen while adopting completed candidate");yield return null;}director.SetPaused(true);yield return Frames();
   Check(director.Started&&director.Track.GraphSurface!=null&&Worlds==1,"Selected graph becomes only current world");Check(oldTrack==null&&oldMesh==null&&oldMaterial==null,"Previous generated world and resources destroyed");
   Check(SharedRaceAssets.Load<GameObject>("Vehicle/Cloudline").GetInstanceID()==modelId&&SharedRaceAssets.Load<Texture2D>("Environment/Textures/Road").GetInstanceID()==textureId&&Loads==loaded,"Imported models/textures reused across different worlds with zero extra loads");Check(paint!=null&&paint.color==paintColor,"Shared source material immutable");yield return ContactGate();
   director.ExitToMenu();menu.Open(RaceMenuScreen.Main);using(var history=director.PrepareForLoading(a)){Check(history.MoveNext()&&history.Status!="Используем готовую трассу","Historical level cannot be returned from cache");}yield return Frames();Check(Worlds==1,"Historical request cancellation keeps current only");
   var currentMesh=director.Track.GetComponentInChildren<MeshFilter>().sharedMesh;Destroy(host);host=null;yield return Frames();Check(currentMesh==null&&AssetCount==0&&!LoadingWork.SimulationFrozen,"Owner teardown releases worlds, asset references and freezes");Trace("source assets="+loaded+" historical worlds=0 current+candidate max=2");
  }
  void Restore(){if(restored)return;restored=true;work?.Dispose();lease?.Dispose();if(host!=null)Destroy(host);if(had)PlayerPrefs.SetString(LocalRaceConfig.PreferenceKey,prefs);else PlayerPrefs.DeleteKey(LocalRaceConfig.PreferenceKey);PlayerPrefs.Save();UserSettingsStore.OverridePath=settingsPath;UserSettingsStore.Replace(settings);Application.runInBackground=background;Time.timeScale=1;}
  void OnDestroy()=>Restore();
 }
}

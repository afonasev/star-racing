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
  public static void Run(){
   if(string.IsNullOrEmpty(Output))throw new Exception("STAR_RACING_WORLD_QA_DIR required");
   if(!Environment.GetCommandLineArgs().Contains("-noaudio")||!Environment.GetCommandLineArgs().Contains("-muted"))throw new Exception("Silent startup required");
   Directory.CreateDirectory(Output);SilentStartupChecks.Run();DesktopUpdateChecks.Run();
   foreach(string name in new[]{"ValidateFlatClosure","ValidateSlabOracle"})typeof(RoadVolumeChecks).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Static).Invoke(null,null);
   SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",true);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.isPlaying=true;
  }
  static void Changed(PlayModeStateChange state){
   if(!SessionState.GetBool(Key,false))return;
   if(state==PlayModeStateChange.EnteredPlayMode){deadline=EditorApplication.timeSinceStartup+1200;EditorApplication.update+=Watch;new GameObject("World reuse QA").AddComponent<WorldReusePlayDriver>();}
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
     var session=director.Session;
     using(var borrowed=director.PrepareForLoading(Config("cloud-city",3650307150))){Check(borrowed.MoveNext()&&borrowed.Status=="Используем готовую трассу","Borrow at cache-hit yield");typeof(RaceDirector).GetMethod("ReleaseInactiveWorlds",Private).Invoke(director,null);yield return null;Check(cloud!=null,"Memory pressure pins returned candidate before next MoveNext");}
     using(var borrowed=director.PrepareForLoading(Config("cloud-city",3650307150))){int count=0;while(borrowed.MoveNext()){if(borrowed.Status=="Готовим участников"&&++count==3)break;yield return null;}Check(count==3,"Borrow reaches partial cars");typeof(RaceDirector).GetMethod("ReleaseInactiveWorlds",Private).Invoke(director,null);yield return null;Check(cloud!=null,"Memory pressure pins partially admitted world");}
     yield return Frames();Check(ReferenceEquals(track,director.Track)&&ReferenceEquals(session,director.Session)&&!cloud.gameObject.activeInHierarchy&&!LoadingWork.SimulationFrozen,"Borrow rollback restores inactive entry and authority");
    }
    typeof(RaceDirector).GetProperty("RaceGeneration").SetValue(director,g-1);
    yield return Prepare(config.Copy());Check(ReferenceEquals(track,director.Track)&&signature==Signature(),"Exact warm parity "+config.theme+config.generatorRevision);Check(RetainedCount<=3,"Cache capacity");Check(director.ExitToMenu(),"Exit parity");menu.Open(RaceMenuScreen.Main);
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
}

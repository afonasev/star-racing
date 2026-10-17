using System;
using System.Collections;
using UnityEngine;
namespace StarRacingPrototype {
 public sealed partial class RaceDirector {
  // Worlds own geometry/PhysX resources only. Balance, cars, AI and input are
  // rebuilt transactionally for every new race, irrespective of cache hits.
  sealed class TrackWorld {
   public string Key;public GameObject Root;public TrackBuilder Track;
   public TrackEnvironmentBuilder Decor;public NativeTrackCourse Course;
   public void Dispose(){if(Root!=null)Root.SetActive(false);Decor?.Clear();if(Root!=null)Destroy(Root);Root=null;}
  }
  sealed class WorldRequest:IDisposable {
   public string Key;public LoadingWork Work;public TrackWorld Result;
   public void Dispose(){Work?.Dispose();Work=null;Result?.Dispose();Result=null;}
  }
  // Only the published level and the currently selected candidate are retained.
  long WorldMemoryBudget=>Math.Max(512L<<20,(long)SystemInfo.systemMemorySize*(1L<<20)/4);
  TrackWorld activeWorld,leasedWorld;WorldRequest prewarm;
  string selectedWorldKey;float selectionChanged,nextMemoryCheck;bool prewarmFailed;
  internal int RetainedWorldCount=>(activeWorld!=null?1:0)+(prewarm?.Result!=null?1:0)+(leasedWorld!=null&&leasedWorld!=activeWorld?1:0);
  internal bool WorldPrewarming=>prewarm?.Work.IsPending==true;
  static long CacheMemoryUsage(long engineBytes){
   if(engineBytes>0)return engineBytes;
   // Non-development Players can have no Profiler. Read this process only;
   // unsupported platforms retain the count bound even without byte telemetry.
   try{using(var process=System.Diagnostics.Process.GetCurrentProcess()){process.Refresh();return process.WorkingSet64;}}
   catch(NotSupportedException){return 0;}
   catch(System.ComponentModel.Win32Exception){return 0;}
  }
  static bool ForcedFallback(LocalRaceConfig config,string theme)=>config.replay!=null&&config.replay.Matches(config,theme)&&config.replay.fallback;
  static string WorldKey(LocalRaceConfig config)=>config.Seed+"|"+ResolvedTheme(config)+"|"+config.rails+"|"+config.jumps+"|"+config.generatorRevision+"|"+ForcedFallback(config,ResolvedTheme(config));
  void RegisterInitialWorld(){
   var root=new GameObject("Retained track world");Track.transform.SetParent(root.transform,true);environment.Root.SetParent(root.transform,true);
   var generated=AppliedConfig.Copy();generated.replay=null;
   activeWorld=new TrackWorld{Key=WorldKey(generated),Root=root,Track=Track,Course=Course,Decor=environment};
  }
  TrackWorld FindWorld(string key)=>activeWorld!=null&&activeWorld.Key==key?activeWorld:null;
  WorldRequest NewWorldRequest(LocalRaceConfig config,bool background){
   var request=new WorldRequest{Key=WorldKey(config)};
   request.Work=new LoadingWork(BuildWorldSteps(config.Copy(),background,request));return request;
  }
  IEnumerator BuildWorldSteps(LocalRaceConfig config,bool background,WorldRequest request){
   TrackWorld staging=null;
   using(var freeze=new LoadingWork.Freeze())try{
    yield return "Загружаем общие модели";yield return SharedRaceAssets.EnsureSteps(background);
    string theme=ResolvedTheme(config);yield return "Рассчитываем трассу";
    NativeTrackCourse course;
    if(background){var settings=new Procedural.GraphCourseSettings(theme,config.rails,config.jumps);bool fallback=ForcedFallback(config,theme);
     var task=LoadingWork.Background(()=>NativeTrackGenerator.GenerateData(config.Seed,settings,config.generatorRevision,fallback));while(!task.IsCompleted)yield return null;course=task.GetAwaiter().GetResult().Complete();}
    else course=GenerateCourse(config,theme,true);
    staging=new TrackWorld{Key=request.Key,Root=new GameObject("Retained track world"),Course=course,Decor=new TrackEnvironmentBuilder()};
    var track=new GameObject("Track");track.transform.SetParent(staging.Root.transform,false);staging.Track=track.AddComponent<TrackBuilder>();
    // Builders query active surfaces. The whole construction holds a freeze;
    // only completed inactive worlds may be retained without one.
    yield return "Строим трассу";yield return staging.Track.BuildNativeSteps(course);
    yield return "Оформляем мир";yield return BuildDecorSteps(staging.Decor,staging.Track,course,config.Seed,theme,background);
    staging.Decor.Root.SetParent(staging.Root.transform,true);staging.Root.SetActive(false);
    request.Result=staging;staging=null;
   }finally{staging?.Dispose();}
  }
  static void CheckWorld(WorldRequest request){
   if(request.Work.Error!=null)throw request.Work.Error;
   if(!request.Work.Succeeded||request.Result==null)throw new InvalidOperationException("Подготовка мира не завершена");
  }
  IEnumerator AcquireWorldSteps(LocalRaceConfig config,bool background,bool reuseWorld,Action<TrackWorld> receive){
   string key=WorldKey(config);
   if(prewarm!=null&&(!reuseWorld||prewarm.Key!=key))CancelWorldPrewarm();
   var found=reuseWorld?FindWorld(key):null;
   if(found!=null){CancelWorldPrewarm();leasedWorld=found;receive(found);yield return "Используем готовую трассу";yield break;}
   // Transfer ownership: Update can no longer advance or cancel this request.
   var request=prewarm??NewWorldRequest(config,background);prewarm=null;
   TrackWorld built=null;
   using(request){
    while(request.Work.MoveNext())yield return request.Work.Status;
    CheckWorld(request);built=request.Result;request.Result=null;
   }
   leasedWorld=built;receive(leasedWorld);
  }
  void CancelWorldPrewarm(){prewarm?.Dispose();prewarm=null;}
  void TickWorldPrewarm(){
   if(!Ready||Started||activePreparation?.IsPending==true)return;
   var menu=GetComponent<RaceMenu>();
   // Present() renders several loading frames before PrepareSteps takes over.
   // Preserve the producer during that gap so Start can transfer it intact.
   if(menu!=null&&menu.Loading)return;
   if(menu==null||!menu.enabled||menu.ScreenState!=RaceMenuScreen.LocalSetup){CancelWorldPrewarm();selectedWorldKey=null;return;}
   if(Time.realtimeSinceStartup>=nextMemoryCheck){nextMemoryCheck=Time.realtimeSinceStartup+1;
    if(CacheMemoryUsage(UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong())>WorldMemoryBudget){ReleaseInactiveWorlds();selectedWorldKey=WorldKey(menu.Selected);return;}}
   if(!menu.CanStartSelected){CancelWorldPrewarm();selectedWorldKey=null;return;}
   var config=menu.Selected;var key=WorldKey(config);
   if(key!=selectedWorldKey){CancelWorldPrewarm();selectedWorldKey=key;selectionChanged=Time.realtimeSinceStartup;prewarmFailed=false;}
   if(FindWorld(key)!=null||prewarmFailed||Time.realtimeSinceStartup-selectionChanged<.25f)return;
   if(prewarm==null)prewarm=NewWorldRequest(config,true);
   if(prewarm.Work.Succeeded)return;
   if(prewarm.Work.MoveNext())return;
   try{CheckWorld(prewarm);}catch(Exception error){CancelWorldPrewarm();prewarmFailed=true;Debug.LogWarning("TRACK_PREWARM "+error.Message);}
  }
  void ReleaseInactiveWorlds(){CancelWorldPrewarm();prewarmFailed=true;}
  void ReleaseAllWorlds(){activeWorld?.Dispose();activeWorld=null;}
 }
}

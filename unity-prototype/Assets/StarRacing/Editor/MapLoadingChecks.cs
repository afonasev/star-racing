using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
namespace StarRacingPrototype {
 // The production loading fixture needs a keyboard even in a headless Editor.
 // Observe stages without changing the loader, its clocks or its deadlines.
 [InitializeOnLoad] public static class MapLoadingChecks {
  const string Key="StarRacing.MapLoadingChecks";
  static MapLoadingChecks(){EditorApplication.playModeStateChanged+=Changed;}
  static Keyboard keyboard;
  static readonly SortedDictionary<string,Measure> measured=new SortedDictionary<string,Measure>();
  sealed class Measure {public int count;public double seconds,maximum;}
  static void Prepare(){
   // Unity 6.3 cannot create its first Search index after Play Mode is queued.
   // Initialize the worktree-local index before starting the fixture.
   var database=Type.GetType("UnityEditor.Search.SearchDatabase, UnityEditor.QuickSearchModule",true);
   database.GetMethod("GetDefaultSearchDatabase",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static).Invoke(null,null);
   SessionState.SetBool(Key,true);
  }
  public static bool DecorOnly=>SessionState.GetBool(Key+"Graph",false);
  public static bool SupportsOnly=>SessionState.GetBool(Key+"Supports",false);
  public static void RunSupports(){SessionState.SetBool(Key+"Supports",true);SessionState.SetBool(Key+"Graph",false);Prepare();WorldReusePlayModeChecks.Run();}
  public static void Run(){SessionState.SetBool(Key+"Supports",false);SessionState.SetBool(Key+"Graph",false);Prepare();WorldReusePlayModeChecks.Run();}
  public static void RunDecor(){SessionState.SetBool(Key+"Supports",false);SessionState.SetBool(Key+"Graph",true);Prepare();WorldReusePlayModeChecks.Run();}
  public static void RunTeardown(){SessionState.SetBool(Key+"Supports",false);Prepare();LoadingPlayModeChecks.RunTeardown();}
  public static IEnumerator MeasureRepeats(RaceDirector director){
   var generation=typeof(RaceDirector).GetProperty("RaceGeneration");
   var signature=typeof(LoadingPlayDriver).GetMethod("Signature",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
   for(int sample=0;sample<3;sample++){
    int before=director.RaceGeneration,frames=0;long physics=RacePhysicsStepper.NativeSteps;double active=0,maximum=0;
    var clock=System.Diagnostics.Stopwatch.StartNew();
    using(var work=director.RepeatForLoading()){
     while(true){var step=System.Diagnostics.Stopwatch.StartNew();bool pending=work.MoveNext();double elapsed=step.Elapsed.TotalSeconds;active+=elapsed;maximum=Math.Max(maximum,elapsed);if(!pending)break;frames++;
      if(!LoadingWork.SimulationFrozen||physics!=RacePhysicsStepper.NativeSteps)throw new Exception("Repeat simulated before publication");yield return null;
     }
     if(!work.Succeeded||work.Error!=null)throw new Exception("Repeat failed",work.Error);
    }
    double wall=clock.Elapsed.TotalSeconds;director.SetPaused(true);
    var actual=(string)signature.Invoke(null,new object[]{director});
    generation.SetValue(director,before);director.Restart();director.SetPaused(true);
    if(actual!=(string)signature.Invoke(null,new object[]{director}))throw new Exception("Frame-driven/synchronous repeat parity");
    director.AppliedConfig.replay.Verify(director.Course,director.Track);
    File.AppendAllText(Path.Combine(WorldReusePlayModeChecks.Output,"repeats.txt"),$"entrants={director.Cars.Length} sample={sample} frames={frames} wall={wall:R} active={active:R} maxStep={maximum:R} signature={actual}\n");
   }
  }
  static void Changed(PlayModeStateChange state){
   if(!SessionState.GetBool(Key,false))return;
   if(state==PlayModeStateChange.EnteredPlayMode){
    bool added=Keyboard.current==null;if(added)keyboard=InputSystem.AddDevice<Keyboard>();
    measured.Clear();LoadingWork.StepMeasured=(name,seconds)=>{if(!measured.TryGetValue(name,out var value))measured[name]=value=new Measure();value.count++;value.seconds+=seconds;value.maximum=Math.Max(value.maximum,seconds);};
    File.AppendAllText(Path.Combine(WorldReusePlayModeChecks.Output,"stages.txt"),"virtualKeyboard="+added+"\n");
    new GameObject("Map loading observer").AddComponent<MapLoadingObserver>();
   }
   if(state==PlayModeStateChange.ExitingPlayMode){
    SupportLoadingProfile.Abort();
    LoadingWork.StepMeasured=null;
    using(var writer=new StreamWriter(Path.Combine(WorldReusePlayModeChecks.Output,"steps.tsv"))){writer.WriteLine("iterator\tcount\tinclusiveSeconds\tmaxSeconds");foreach(var pair in measured)writer.WriteLine($"{pair.Key}\t{pair.Value.count}\t{pair.Value.seconds:R}\t{pair.Value.maximum:R}");}
    if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);keyboard=null;
   }
   if(state==PlayModeStateChange.EnteredEditMode)SessionState.SetBool(Key,false);
  }
 }
 public sealed class MapLoadingObserver:MonoBehaviour {
  string previous;int previousTrack;
  void Update(){
   var director=FindFirstObjectByType<RaceDirector>();if(director==null)return;
   var menu=director.GetComponent<RaceMenu>();if(menu==null)return;
   if(director.Started&&director.Track!=null&&director.Track.GetInstanceID()!=previousTrack){
    previousTrack=director.Track.GetInstanceID();
    var decor=(TrackEnvironmentBuilder)typeof(RaceDirector).GetField("environment",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(director);
    File.AppendAllText(Path.Combine(WorldReusePlayModeChecks.Output,"inventory.txt"),$"{director.AppliedConfig.theme}/{director.AppliedConfig.seed}/v{director.AppliedConfig.generatorRevision} independent={director.Course.IsIndependent} trackFilters={director.Track.GetComponentsInChildren<MeshFilter>(true).Length} decorFilters={decor.Root.GetComponentsInChildren<MeshFilter>(true).Length}\n");
   }
   LocalInputRouter.CanBind(menu.Selected,out var error);
   var request=typeof(RaceDirector).GetField("prewarm",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(director);
   var work=request==null?null:(LoadingWork)request.GetType().GetField("Work").GetValue(request);
   string state=menu.ScreenState+" ready="+director.Ready+" canStart="+menu.CanStartSelected+" input="+error+" status="+work?.Status;
   if(state==previous)return;previous=state;
   File.AppendAllText(Path.Combine(WorldReusePlayModeChecks.Output,"stages.txt"),DateTime.UtcNow.ToString("O")+" "+state+"\n");
  }
 }
}

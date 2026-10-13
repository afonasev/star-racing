using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using StarRacingPrototype.Procedural;
namespace StarRacingPrototype {
 [InitializeOnLoad] public static class TrackCompositionPlayModeChecks {
  const string Key="StarRacing.TrackCompositionPlayMode";
  static readonly MethodInfo Tick=typeof(MagneticVehicle).GetMethod("FixedUpdate",BindingFlags.Instance|BindingFlags.NonPublic);
  static int startup;
  static TrackCompositionPlayModeChecks(){EditorApplication.playModeStateChanged+=Changed;}
  [Serializable] public sealed class Result {public string family,role,hash;public uint seed;public int startIndex,endIndex,respawns;public float elapsed,progress,maxStall;public bool passed;}
  [Serializable] public sealed class Report {public List<Result> cases=new List<Result>();}
  public static void Run(){if(string.IsNullOrEmpty(TrackCompositionChecks.Output))throw new Exception("STAR_RACING_COMPOSITION_QA_DIR required");Directory.CreateDirectory(TrackCompositionChecks.Output);SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",false);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);startup=0;EditorApplication.update+=Enter;}
  static void Enter(){if(++startup<10)return;EditorApplication.update-=Enter;EditorApplication.isPlaying=true;}
  static void Changed(PlayModeStateChange state){if(!SessionState.GetBool(Key,false))return;if(state==PlayModeStateChange.EnteredPlayMode)EditorApplication.delayCall+=Execute;if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",false)?1:0);}}
  static void Execute(){var old=Physics.simulationMode;float dt=Time.fixedDeltaTime,volume=AudioListener.volume;var report=new Report();try{
   Physics.simulationMode=SimulationMode.Script;Time.fixedDeltaTime=.01f;AudioListener.volume=0;
   foreach(string family in Generator.DrivingFamilies){uint seed=TrackCompositionChecks.CorpusSeeds().First(x=>Generator.FamilyFor(x,"cloud-city")==family);var d=Generator.Generate(seed,"normal","cloud-city",true,true);
    foreach(var section in d.composition.Where(x=>x.role!="recovery"))report.cases.Add(Drive(d,section));
    // A partial/mandatory jump pair uses the unchanged flight and landing envelope.
    foreach(var jump in d.jumps.Take(2))report.cases.Add(Drive(d,new CompositionSection{role=jump.kind+"-jump",startIndex=jump.rampStartIndex,endIndex=jump.ballisticLandingEndIndex}));
   }
   File.WriteAllText(Path.Combine(TrackCompositionChecks.Output,"playmode.json"),JsonUtility.ToJson(report,true));
   TrackCompositionChecks.Check(report.cases.All(x=>x.passed),"physics traversal failed; inspect playmode.json");
   File.WriteAllText(Path.Combine(TrackCompositionChecks.Output,"playmode-result.txt"),$"COMPOSITION_PLAYMODE_OK cases={report.cases.Count}\n");
   Debug.Log($"COMPOSITION_PLAYMODE_OK cases={report.cases.Count}");
  }catch(Exception e){SessionState.SetBool(Key+"Failed",true);Debug.LogException(e);File.WriteAllText(Path.Combine(TrackCompositionChecks.Output,"playmode-failure.txt"),e.ToString());File.WriteAllText(Path.Combine(TrackCompositionChecks.Output,"playmode.json"),JsonUtility.ToJson(report,true));}finally{Physics.simulationMode=old;Time.fixedDeltaTime=dt;AudioListener.volume=volume;EditorApplication.isPlaying=false;}}
  public static void RunNarrowingPack() {
   var old=Physics.simulationMode;float dt=Time.fixedDeltaTime;var root=new GameObject("narrowing pack fixture");
   var result=new Result{family="technical",role="four-AI-narrowing"};
   try {
    Physics.simulationMode=SimulationMode.Script;Time.fixedDeltaTime=.01f;
    uint seed=TrackCompositionChecks.CorpusSeeds().First(x=>Generator.FamilyFor(x,"cloud-city")=="technical");
    var d=Generator.Generate(seed,"normal","cloud-city",false,true);var pattern=d.patterns.First(x=>x.kind=="tightening-radius");
    result.seed=seed;result.hash=d.hash;result.startIndex=pattern.startIndex;result.endIndex=pattern.endIndex;
    var road=new GameObject("road");road.transform.SetParent(root.transform);var track=road.AddComponent<TrackBuilder>();track.Build(new TrackRoute(d));
    var balance=ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);var cars=new MagneticVehicle[4];var drivers=new AiDriver[4];var inputs=new DrivingInput[4];var revisions=new int[4];var stalls=new float[4];
    float start=30+pattern.startIndex*5-60,finish=30+pattern.endIndex*5+40;
    for(int i=0;i<4;i++) {
     var obj=new GameObject("ordinary pack AI "+i);obj.transform.SetParent(root.transform);var car=obj.AddComponent<MagneticVehicle>();car.Initialize(track,i,Color.cyan,balance,false);car.enabled=false;car.ResetAt(start-i/2*14,i%2==0?-3.2f:3.2f);
     cars[i]=car;drivers[i]=new AiDriver(i,DriverProfile.Racer,seed+(uint)i,balance);revisions[i]=car.PositionRevision;
    }
    Physics.SyncTransforms();var world=new AiWorldSnapshot(4);
    for(int tick=0;tick<6000;tick++) {
     double now=10+tick*.01;
     foreach(var car in cars){car.Hold(false);car.SetRaceContext(true,1,4,now);car.PrepareProjection();}
     if(tick%10==0){world.Capture(cars,track.Route,now);for(int i=0;i<4;i++)inputs[i]=drivers[i].Step(world,track.Route,.1f,false);}
     for(int i=0;i<4;i++){cars[i].SetInput(inputs[i]);Tick.Invoke(cars[i],null);}RacePhysicsStepper.Simulate(.01f);
     for(int i=0;i<4;i++) {
      if(cars[i].PositionRevision!=revisions[i]){result.respawns++;revisions[i]=cars[i].PositionRevision;}
      if(tick>200&&cars[i].Body.linearVelocity.magnitude<3){stalls[i]+=.01f;result.maxStall=Mathf.Max(stalls[i],result.maxStall);}else stalls[i]=0;
     }
     result.elapsed=(tick+1)*.01f;result.progress=cars.Min(car=>car.Distance-start);
     if(cars.All(car=>car.Distance>=finish)){result.passed=result.respawns==0&&result.maxStall<3;break;}
    }
    File.WriteAllText(Path.Combine(TrackCompositionChecks.Output,"narrowing-pack.json"),JsonUtility.ToJson(result,true));
    TrackCompositionChecks.Check(result.passed,"four-AI physical narrowing traversal");
    Debug.Log($"COMPOSITION_NARROWING_PACK_OK count=4 progress={result.progress:F1} respawns={result.respawns} stall={result.maxStall:F1}");
   }finally{UnityEngine.Object.DestroyImmediate(root);Physics.SyncTransforms();Physics.simulationMode=old;Time.fixedDeltaTime=dt;}
  }
  static Result Drive(Definition d,CompositionSection section){var root=new GameObject("composition physical fixture");try{
   var road=new GameObject("road");road.transform.SetParent(root.transform);var track=road.AddComponent<TrackBuilder>();track.Build(new TrackRoute(d));
   var balance=ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);var obj=new GameObject("ordinary AI");obj.transform.SetParent(root.transform);
   var car=obj.AddComponent<MagneticVehicle>();car.Initialize(track,0,Color.cyan,balance,false);car.enabled=false;
   float start=30+section.startIndex*5-50,finish=30+section.endIndex*5+50;car.ResetAt(start,0);Physics.SyncTransforms();
   var driver=new AiDriver(0,DriverProfile.Racer,d.requestedSeed,balance);var world=new AiWorldSnapshot(1);var input=new DrivingInput();int revision=car.PositionRevision;float stall=0;
   var result=new Result{family=d.family,role=section.role,hash=d.hash,seed=d.requestedSeed,startIndex=section.startIndex,endIndex=section.endIndex};
   for(int tick=0;tick<12000;tick++){
    double now=10+tick*.01;car.Hold(false);car.SetRaceContext(true,1,1,now);car.PrepareProjection();
    if(tick%10==0){world.Capture(new[]{car},track.Route,now);input=driver.Step(world,track.Route,.1f,false);}
    car.SetInput(input);Tick.Invoke(car,null);RacePhysicsStepper.Simulate(.01f);
    if(car.PositionRevision!=revision){result.respawns++;revision=car.PositionRevision;}
    if(tick>200&&car.Body.linearVelocity.magnitude<3){stall+=.01f;result.maxStall=Mathf.Max(stall,result.maxStall);}else stall=0;
    result.elapsed=(tick+1)*.01f;result.progress=car.Distance-start;
    if(car.Distance>=finish){result.passed=result.respawns==0&&result.maxStall<3;break;}
   }
   Debug.Log($"COMPOSITION_DRIVE family={d.family} role={section.role} elapsed={result.elapsed:F1} progress={result.progress:F1} respawns={result.respawns} stall={result.maxStall:F1} passed={result.passed}");return result;
  }finally{UnityEngine.Object.DestroyImmediate(root);Physics.SyncTransforms();}}
 }
}

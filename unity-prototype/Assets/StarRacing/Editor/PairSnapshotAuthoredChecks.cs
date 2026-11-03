using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StarRacingPrototype {
 // Temporary opt-in falsifier. No shipping controller, geometry or cadence changes.
 [InitializeOnLoad] public static class PairSnapshotAuthoredChecks {
  const string Key="StarRacing.PairSnapshotAuthored";
  const int Count=64;
  static int startup;static long paritySlices;static int largestSnapshot;
  static readonly List<RacePhysicsStepper.ContactVehicle> oracleSnapshot=new List<RacePhysicsStepper.ContactVehicle>(128);
  static void Audit(RacePhysicsStepper.ContactVehicle[] cars,int count,float remaining,float limit){
   oracleSnapshot.Clear();for(int i=0;i<count;i++)oracleSnapshot.Add(cars[i]);
   float expected=RacePhysicsStepper.EditorOriginalPairLimit(oracleSnapshot,remaining,limit,out int a,out int b);
   float actual=RacePhysicsStepper.EditorPairLimit(cars,count,remaining,limit,out int x,out int y);
   Require(BitConverter.SingleToInt32Bits(expected)==BitConverter.SingleToInt32Bits(actual)&&a==x&&b==y,"Actual authored slice parity mismatch");
   float sa=remaining/Mathf.Max(1,Mathf.CeilToInt(remaining/expected)),sb=remaining/Mathf.Max(1,Mathf.CeilToInt(remaining/actual));
   Require(BitConverter.SingleToInt32Bits(sa)==BitConverter.SingleToInt32Bits(sb),"Actual authored slice duration mismatch");
   paritySlices++;largestSnapshot=Math.Max(largestSnapshot,count);
  }
  static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_PAIR_AUTHORED_DIR");
  static readonly MethodInfo Tick=typeof(MagneticVehicle).GetMethod("FixedUpdate",BindingFlags.Instance|BindingFlags.NonPublic);
  static PairSnapshotAuthoredChecks(){EditorApplication.playModeStateChanged+=Changed;}
  [Serializable] public sealed class Mode {public int value;public string name,stage,utc;}
  public static Mode LoadedMode(string stage) {
   var manager=AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset")[0];
   var settings=new SerializedObject(manager);settings.Update();var property=settings.FindProperty("m_BroadphaseType");
   var result=new Mode{value=property.intValue,name=property.enumDisplayNames[property.enumValueIndex],stage=stage,utc=DateTime.UtcNow.ToString("O")};
   int expected=int.Parse(Environment.GetEnvironmentVariable("STAR_RACING_PROFILE_BROADPHASE")??throw new ArgumentException("Expected broadphase required"));
   string name=expected==2?"Automatic Box Pruning":expected==0?"Sweep And Prune Broadphase":throw new ArgumentException("Only ABP/SAP permitted");
   if(result.value!=expected||!string.Equals(result.name,name,StringComparison.OrdinalIgnoreCase))throw new Exception("Loaded mode mismatch: "+JsonUtility.ToJson(result));
   return result;
  }
  public static void Run(){
   if(string.IsNullOrEmpty(Output))throw new ArgumentException("STAR_RACING_PAIR_AUTHORED_DIR required");
   Directory.CreateDirectory(Output);
   // Native serialized settings read before creating the empty fixture scene or entering Play Mode.
   File.WriteAllText(Path.Combine(Output,"loaded-before-scene.json"),JsonUtility.ToJson(LoadedMode("before-newscene-playmode"),true));
   SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",true);
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);startup=0;EditorApplication.update+=Enter;
  }
  static void Enter(){if(++startup<15)return;EditorApplication.update-=Enter;EditorApplication.isPlaying=true;}
  static void Changed(PlayModeStateChange state){if(!SessionState.GetBool(Key,false))return;
   if(state==PlayModeStateChange.EnteredPlayMode){startup=0;EditorApplication.update+=Measure;}
   if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",true)?1:0);}
  }
  [Serializable] public sealed class Sample {
   public string scenario;public int repeat,tick,falls,respawns,fallingBefore,fallingAfter;
   public double projectionMs,vehicleMs,physicsMs,totalMs;public long nativeSteps;
  }
  [Serializable] public sealed class Case {
   public string scenario;public int repeat,falls,respawns;public double resetMs,warmupMs;
  }
  [Serializable] public sealed class Report {
   public string source,unity,course,geometry,notes;public Mode loaded;
   public bool success,usesBasis;public int entrants=Count,shapes,staticColliders,substeps=RacePhysicsStepper.Substeps;
   public double setupMs;public List<Case> cases=new List<Case>();public List<Sample> samples=new List<Sample>();
  }
  static double Ms(long start)=>(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;
  static void Require(bool value,string label){if(!value)throw new Exception(label);}
  static void Save(Report report)=>File.WriteAllText(Path.Combine(Output,report.course+".json"),JsonUtility.ToJson(report,true));
  static void Measure(){if(++startup<15)return;EditorApplication.update-=Measure;
   var previous=Physics.simulationMode;float dt=Time.fixedDeltaTime;
   try {
    var loaded=LoadedMode("playmode-before-fixture-colliders");
    Physics.simulationMode=SimulationMode.Script;Time.fixedDeltaTime=.01f;
    paritySlices=0;largestSnapshot=0;RacePhysicsStepper.DiagnosticOriginalSnapshot=false;RacePhysicsStepper.EditorPairInputs=Audit;
    foreach(bool orbital in new[]{false,true})MeasureCourse(orbital,loaded);
    Require(paritySlices>0&&largestSnapshot==64,"64 actual pair inputs not audited");
    File.WriteAllText(Path.Combine(Output,"slice-parity.json"),"{\"success\":true,\"slices\":"+paritySlices+",\"largestSnapshot\":"+largestSnapshot+"}");
    UnityEngine.Debug.Log("PAIR_AUTHORED_PARITY_OK slices="+paritySlices+" largestSnapshot="+largestSnapshot);
    SessionState.SetBool(Key+"Failed",false);
   }catch(Exception e){File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());UnityEngine.Debug.LogException(e);}
   finally{RacePhysicsStepper.EditorPairInputs=null;RacePhysicsStepper.DiagnosticOriginalSnapshot=false;Physics.simulationMode=previous;Time.fixedDeltaTime=dt;EditorApplication.isPlaying=false;}
  }
  static void MeasureCourse(bool orbital,Mode loaded){
   GameObject root=null;
   var report=new Report{source=Environment.GetEnvironmentVariable("STAR_RACING_PROFILE_SOURCE"),unity=Application.unityVersion,
    course=orbital?"orbital":"city",loaded=loaded,notes="Exclusive Editor synchronous controller/production stepper diagnostic. 64 actual vehicles; stock roster/grid seed2273265884; 10ms/8substeps; no AI/render/decor. Not Player FPS or human acceptance. Timings include differential observer overhead and are NOT performance evidence. Per-slice candidate/original bit and winning-seat/slice parity; unchanged production Physics.Simulate calls from the same observed body state."};
   try {
    long at=Stopwatch.GetTimestamp();root=new GameObject("authored64 exact pair snapshot screen");
    var road=new GameObject("road");road.transform.SetParent(root.transform,false);var track=road.AddComponent<TrackBuilder>();
    var course=CourseSource.Generate(orbital?OrbitalRiftChecks.Config():AuthoredCourseChecks.Config(),orbital?"space-station":"cloud-city");
    Require(course.UsesBasis,"Shipping authored basis required");track.BuildNativeCourse(course);report.usesBasis=track.UsesBasis;report.geometry=course.GeometryIdentity;
    report.staticColliders=track.GetComponentsInChildren<Collider>().Length;
    var roster=new RaceRoster(Count,4,2273265884u);var cars=new MagneticVehicle[Count];var ticks=new Action[Count];
    var balance=ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);
    foreach(var entrant in roster.Entrants){
     var obj=new GameObject("entrant "+entrant.Id);obj.transform.SetParent(root.transform,false);var car=obj.AddComponent<MagneticVehicle>();
     car.Initialize(track,entrant.Id,Color.cyan,balance,true);car.enabled=false;cars[entrant.Id]=car;
     ticks[entrant.Id]=(Action)Delegate.CreateDelegate(typeof(Action),car,Tick);
     int shapes=car.GetComponentsInChildren<Collider>().Length;Require(shapes==9,"Actual9shape vehicle required");report.shapes+=shapes;
    }
    Physics.SyncTransforms();report.setupMs=Ms(at);var events=new List<RecoveryEvent>();
    void Step(int tick,Sample sample){
     long began=Stopwatch.GetTimestamp(),part=began,native=RacePhysicsStepper.NativeSteps;
     foreach(var car in cars){if(sample!=null&&car.IsFalling)sample.fallingBefore++;car.PrepareProjection();car.SetRaceContext(true,1,Count,tick*.01);}
     if(sample!=null)sample.projectionMs=Ms(part);part=Stopwatch.GetTimestamp();
     foreach(var action in ticks)action();
     if(sample!=null)sample.vehicleMs=Ms(part);part=Stopwatch.GetTimestamp();
     RacePhysicsStepper.SimulatePrepared(cars,.01f);
     if(sample!=null){sample.physicsMs=Ms(part);sample.nativeSteps=RacePhysicsStepper.NativeSteps-native;sample.totalMs=Ms(began);}
     events.Clear();foreach(var car in cars){car.DrainRecoveryEvents(events);if(sample!=null){if(car.IsFalling)sample.fallingAfter++;var p=car.Body.position;Require(float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z),"Nonfinite vehicle");}}
     if(sample!=null)foreach(var e in events)if(e.Kind==RecoveryEventKind.Fall)sample.falls++;else sample.respawns++;
    }
    for(int repeat=0;repeat<2;repeat++)foreach(string scenario in new[]{"drive","edge","remote"}){
     var current=new Case{scenario=scenario,repeat=repeat};report.cases.Add(current);at=Stopwatch.GetTimestamp();
     foreach(var entrant in roster.Entrants){var car=cars[entrant.Id];car.ResetAt(RaceRoster.GridDistance(track.Route,entrant.GridSlot),RaceRoster.GridLateral(track.Route,entrant.GridSlot));car.SetInput(default);Require(car.HasProvenCheckpoint,"Unproven grid checkpoint "+entrant.Id);}
     Physics.SyncTransforms();current.resetMs=Ms(at);at=Stopwatch.GetTimestamp();for(int t=0;t<40;t++)Step(t,null);current.warmupMs=Ms(at);
     foreach(var car in cars){var f=car.Frame;float side=car.Seat%2==0?-1:1;
      if(scenario=="drive")car.Body.linearVelocity=f.tangent*car.Drive.BaseSpeed;
      else {var p=f.position+f.right*side*(f.halfWidth+(scenario=="edge"?.8f:30f))+f.normal*.15f;car.Body.position=p;car.transform.position=p;car.ResetPresentation();car.Body.linearVelocity=scenario=="edge"?f.right*side*10:Vector3.zero;}
      car.SetInput(new DrivingInput{throttle=scenario=="drive"?1:0});
     }
     Physics.SyncTransforms();int length=scenario=="drive"?100:300;
     for(int t=0;t<length;t++){var sample=new Sample{scenario=scenario,repeat=repeat,tick=t};Step(t,sample);report.samples.Add(sample);current.falls+=sample.falls;current.respawns+=sample.respawns;}
     Save(report);UnityEngine.Debug.Log($"AUTHORED64_CASE course={report.course} scenario={scenario} repeat={repeat} falls={current.falls} respawns={current.respawns}");
     if(scenario=="drive")Require(current.falls==0&&current.respawns==0,"Ordinary recovery regression");
     else {int minimum=scenario=="remote"?64:32;Require(current.falls>=minimum&&current.respawns>=minimum,"Mass recovery not reproduced "+scenario);}
    }
    Require(report.samples.Count==1400,"Incomplete samples");report.success=true;Save(report);
   }finally{if(root!=null)UnityEngine.Object.DestroyImmediate(root);Save(report);}
  }
 }
}

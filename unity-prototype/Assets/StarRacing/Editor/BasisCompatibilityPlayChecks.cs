using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace StarRacingPrototype {
 [InitializeOnLoad] public static class BasisCompatibilityPlayChecks {
  const string Key="StarRacing.BasisCompatiblePlay";static int ticks;
  static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_BASIS_COMPAT_DIR");
  static readonly MethodInfo Tick=typeof(MagneticVehicle).GetMethod("FixedUpdate",BindingFlags.Instance|BindingFlags.NonPublic);
  static void Check(bool ok,string label)=>BasisCompatibilityChecks.Check(ok,label);
  static BasisCompatibilityPlayChecks(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run(){Directory.CreateDirectory(Output);SessionState.SetBool(Key,true);SessionState.SetBool(Key+"failed",true);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);ticks=0;EditorApplication.update+=Enter;}
  static void Enter(){if(++ticks<15)return;EditorApplication.update-=Enter;EditorApplication.isPlaying=true;}
  static void Changed(PlayModeStateChange state){if(!SessionState.GetBool(Key,false))return;
   if(state==PlayModeStateChange.EnteredPlayMode){ticks=0;EditorApplication.update+=Execute;}
   if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"failed",true)?1:0);}
  }
  static void Execute(){if(++ticks<15)return;EditorApplication.update-=Execute;var mode=Physics.simulationMode;float dt=Time.fixedDeltaTime;
   try{
    foreach(var go in SceneManager.GetActiveScene().GetRootGameObjects())UnityEngine.Object.DestroyImmediate(go);
    Physics.simulationMode=SimulationMode.Script;Time.fixedDeltaTime=.01f;AudioListener.volume=0;
    string phase=Environment.GetEnvironmentVariable("STAR_RACING_BASIS_COMPAT_PHASE");
    if(Environment.GetEnvironmentVariable("STAR_RACING_CONTACT_BUFFER")=="1")BasisContactBuffer.Begin(Output);
    if(phase=="original-seam-control"){
     var flags=BindingFlags.NonPublic|BindingFlags.Static;
     typeof(PhysicalContactChecks).GetField("failures",flags).SetValue(null,0);
     var observer=new GameObject("original seam observer");RoadNativeContactObserver.Begin(observer);
     try{typeof(PhysicalContactChecks).GetMethod("Ramp",flags).Invoke(null,new object[]{1f,0f,138.8889f,false});}
     finally{RoadNativeContactObserver.End();UnityEngine.Object.DestroyImmediate(observer);}
     Check((int)typeof(PhysicalContactChecks).GetField("failures",flags).GetValue(null)==0,"unchanged PhysicalContactChecks.Ramp control");
    }
    foreach(bool candidate in phase=="original-seam-control"?Array.Empty<bool>():(phase=="seams-candidate"||phase=="visual-candidate")?new[]{true}:new[]{false,true}){
     if(phase=="physical-flat")RoadVolumeChecks.RunFlatDefinition(candidate?BasisNativeExperiment.Transform(RoadVolumeChecks.FlatRoad()):RoadVolumeChecks.FlatRoad());
     else if(phase=="physical-generated"){
      var source=BasisCompatibilityChecks.Course(false).Single;RoadVolumeChecks.RunGeneratedDefinition(candidate?BasisNativeExperiment.Transform(source):source);
     }else if(phase=="seams"||phase=="seams-candidate"||phase=="seam-diagnostic")Seams(candidate);
     else if(phase=="recovery")Recovery(candidate);
     else if(phase=="visual"||phase=="visual-candidate")Visual(candidate);
     else throw new ArgumentException("Unknown phase "+phase);
     File.WriteAllText(Path.Combine(Output,(candidate?"candidate":"baseline")+"-pass.txt"),phase+" PASS\n");
    }
    if(phase=="recovery")RecoveryGhostChecks.Run();
    SessionState.SetBool(Key+"failed",false);Debug.Log("BASIS_COMPAT_PLAY_OK phase="+phase);
   }catch(Exception e){Debug.LogException(e);File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());}
   finally{BasisContactBuffer.End();Physics.simulationMode=mode;Time.fixedDeltaTime=dt;BasisNativeExperiment.EditorOverride=null;EditorApplication.isPlaying=false;}
  }
  static NativeTrackCourse FlatCourse(bool candidate,Procedural.Definition definition){
   definition.version=12;definition.seed=definition.requestedSeed=77;definition.family="speed";return FixtureCourse(candidate,definition);
  }
  static NativeTrackCourse FixtureCourse(bool candidate,Procedural.Definition definition){
   var previous=BasisNativeExperiment.EditorOverride;try{
    BasisNativeExperiment.EditorOverride=candidate;
    return (NativeTrackCourse)Activator.CreateInstance(typeof(NativeTrackCourse),BindingFlags.Instance|BindingFlags.NonPublic,null,
     new object[]{definition,null,new Procedural.GraphCourseSettings("cloud-city","none",true)},null);
   }finally{BasisNativeExperiment.EditorOverride=previous;}
  }
  static MagneticVehicle Car(GameObject root,TrackBuilder track,int seat,float distance,float lane){
   var go=new GameObject("basis entrant "+seat);go.transform.SetParent(root.transform,false);var car=go.AddComponent<MagneticVehicle>();
   car.Initialize(track,seat,Color.cyan,ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text),true);car.enabled=false;car.ResetAt(distance,lane);
   var shapes=car.GetComponentsInChildren<Collider>();
   Check(shapes.Length==9&&shapes.All(c=>c.enabled&&c.hasModifiableContacts),"all nine car shapes preserve modifiable contact materials");return car;
  }
  static bool Finite(Vector3 p)=>float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z);
  static void Trace(MagneticVehicle car,int tick,string stage){
   if(stage=="after-native")BasisContactBuffer.Flush(tick);
   var body=car.Body;var forces=(VehicleForceFrame)typeof(MagneticVehicle).GetField("preparedForces",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(car);
   bool finite=Finite(body.position)&&Finite(body.linearVelocity)&&Finite(body.angularVelocity)&&Finite(car.Frame.tangent)&&Finite(forces.force)&&Finite(forces.acceleration)&&Finite(forces.torque)&&Finite(forces.angularAcceleration);
   if(Environment.GetEnvironmentVariable("STAR_RACING_BASIS_COMPAT_PHASE")=="seam-diagnostic")
    File.AppendAllText(Path.Combine(Output,"first-nonfinite-trace.txt"),$"tick={tick} native={RacePhysicsStepper.NativeSteps} stage={stage} finite={finite} pos={body.position:G9} vel={body.linearVelocity:G9} omega={body.angularVelocity:G9} inertia={body.inertiaTensor:G9} force={forces.force:G9} accel={forces.acceleration:G9} torque={forces.torque:G9} angularAccel={forces.angularAcceleration:G9}\n");
   if(stage=="after-native"&&Environment.GetEnvironmentVariable("STAR_RACING_ROAD_OBSERVER")=="1"){
    RoadNativeContactObserver.AfterStep();
    if(tick<=4||!finite)foreach(var collider in car.ContactColliders)File.AppendAllText(Path.Combine(Output,"native-contacts.txt"),$"tick={tick} native={RacePhysicsStepper.NativeSteps}\n"+RoadNativeContactObserver.For(collider)+"\n");
   }
   Check(finite,"finite body/frame/forces tick="+tick+" stage="+stage);
  }
  static void Step(MagneticVehicle[] cars,int number){
   foreach(var car in cars){Trace(car,number,"before-controller");car.SetRaceContext(true,1,cars.Length,number*.01);car.Hold(false);car.PrepareProjection();Tick.Invoke(car,null);Trace(car,number,"after-controller");}
   RacePhysicsStepper.SimulatePrepared(cars,.01f,RacePhysicsStepper.Substeps,()=>{foreach(var car in cars)Trace(car,number,"after-native");});
  }
  static void Presentation(MagneticVehicle car,TrackBuilder track,string label){
   car.ResetPresentation();Check(car.RenderPose.position.Equals(track.ToPresentation(car.Body.position)),label+" presentation pose reset");
   Check((car.RenderFrame.position-track.PresentationRoute.Project(car.RenderPose.position,car.Distance,30).position).sqrMagnitude==0,label+" canonical camera frame");
   Check(car.GetComponentsInChildren<Collider>().Count(c=>c.enabled)==9,label+" all9shapes");
  }
  static void Seams(bool candidate){
   bool diagnostic=Environment.GetEnvironmentVariable("STAR_RACING_BASIS_COMPAT_PHASE")=="seam-diagnostic";
   foreach(float side in diagnostic?new[]{1f}:new[]{-1f,1f}){
    var root=new GameObject("basis ramp seams");try{
     var definition=RoadVolumeChecks.FlatRoad();definition.jumps=new[]{new Procedural.Jump{id="partial",kind="optional",rampStartIndex=12,launchIndex=18,gapStartIndex=19,gapEndIndex=23,landingEndIndex=37,ballisticLandingEndIndex=60,lateralCenter=side*5.333333,lateralHalfWidth=2.666667}};
     var track=root.AddComponent<TrackBuilder>();track.BuildNativeCourse(FlatCourse(candidate,definition));
     foreach(float angle in diagnostic?new[]{0f}:new[]{0f,30f})foreach(float speed in diagnostic?new[]{138.8889f}:new[]{42f,138.8889f}){
      var car=Car(root,track,0,86,-side*2.5f);try{
       if(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_OBSERVER")=="1")RoadNativeContactObserver.Begin(new GameObject("empty contact observer"));
       car.Body.rotation=car.Frame.Rotation*Quaternion.Euler(0,side*-angle,0);car.Body.linearVelocity=car.Body.rotation*Vector3.forward*speed;car.transform.SetPositionAndRotation(car.Body.position,car.Body.rotation);car.SetInput(new DrivingInput{throttle=1});Physics.SyncTransforms();
       float minForward=999;bool crossed=false;for(int t=0;t<65;t++){Step(new[]{car},t);if(car.Distance<120)minForward=Mathf.Min(minForward,Vector3.Dot(car.Body.linearVelocity,car.Frame.tangent));crossed|=Mathf.Abs(Vector3.Dot(car.Body.position-car.Frame.position,car.Frame.right))>car.Frame.halfWidth+VehicleGeometry.HalfWidth;}
       File.AppendAllText(Path.Combine(Output,"seams.txt"),$"candidate={candidate} side={side} angle={angle} speed={speed} minForward={minForward:R} falling={car.IsFalling}\n");
       Check(minForward>12,"original partial-ramp forward bound");Check(!car.IsFalling||crossed||car.Distance>=120,"original inside ramp nofall bound");Presentation(car,track,"ramp");
      }finally{RoadNativeContactObserver.End();UnityEngine.Object.DestroyImmediate(car.gameObject);}
     }
    }finally{UnityEngine.Object.DestroyImmediate(root);}
   }
  }
  static void Recovery(bool candidate){
   var root=new GameObject("basis mass recovery");try{
    // Match the production fixture: cars are siblings of the road. Parenting
    // them below TrackBuilder makes CheckpointAdmission classify cars as road obstacles.
    var road=new GameObject("recovery road");road.transform.SetParent(root.transform,false);
    var track=road.AddComponent<TrackBuilder>();var source=Procedural.Generator.Generate(2273265884,"normal","cloud-city",true,true);
    // Exact geometry/settings of the existing recovery witness; only fixture revision selects the new representation boundary.
    source.version=12;track.BuildNativeCourse(FixtureCourse(candidate,source));
    var cars=Enumerable.Range(0,32).Select(i=>Car(root,track,i,80+(i/2)*6,i%2==0?-2.5f:2.5f)).ToArray();var events=new List<RecoveryEvent>();
    for(int repeat=0;repeat<3;repeat++)foreach(string scenario in new[]{"drive","edge","remote"}){
     foreach(var car in cars){car.ResetAt(80+(car.Seat/2)*6,car.Seat%2==0?-2.5f:2.5f);car.SetInput(default);Check(car.HasProvenCheckpoint,"recovery proven checkpoint entrant="+car.Seat);}
     Physics.SyncTransforms();for(int t=0;t<40;t++){Step(cars,t);foreach(var car in cars)car.DrainRecoveryEvents(events);events.Clear();}
     foreach(var car in cars){var f=car.Frame;float side=car.Seat%2==0?-1:1;
      if(scenario=="drive")car.Body.linearVelocity=f.tangent*car.Drive.BaseSpeed;
      else{car.Body.position=f.position+f.right*side*(f.halfWidth+(scenario=="edge"?.8f:30))+f.normal*.15f;car.transform.position=car.Body.position;car.ResetPresentation();car.Body.linearVelocity=scenario=="edge"?f.right*side*10:Vector3.zero;}
      car.SetInput(new DrivingInput{throttle=scenario=="drive"?1:0});
     }
     Physics.SyncTransforms();int falls=0,respawns=0;for(int t=0;t<300;t++){Step(cars,t);events.Clear();foreach(var car in cars)car.DrainRecoveryEvents(events);foreach(var e in events)if(e.Kind==RecoveryEventKind.Fall)falls++;else respawns++;}
     Check(scenario=="drive"||falls>=16&&respawns>=16,"original mass recovery >=16falls/respawns "+scenario);
     foreach(var car in cars)Presentation(car,track,"recovery");
     File.AppendAllText(Path.Combine(Output,"recovery.txt"),$"candidate={candidate} repeat={repeat} scenario={scenario} falls={falls} respawns={respawns}\n");
    }
   }finally{UnityEngine.Object.DestroyImmediate(root);}
  }
  static void Visual(bool candidate){
   foreach(string theme in new[]{"cloud-city","space-station"}){
    uint seed=theme=="cloud-city"?211933141:Enumerable.Range(0,256).Select(i=>(uint)i).First(i=>NativeTrackGenerator.FamilyFor(i,theme)=="vertical");
    var root=new GameObject("canonical visual witness");var decor=new TrackEnvironmentBuilder();try{
     var road=new GameObject("canonical road");road.transform.SetParent(root.transform,false);var track=road.AddComponent<TrackBuilder>();var course=BasisCompatibilityChecks.Course(candidate,seed,theme);track.BuildNativeCourse(course);Check(track.UsesBasis==candidate,"visual actually selects basis");
     decor.Build(track.PresentationRoute,track.PresentationRoot,seed);decor.Sky.Activate();
     var lamp=new GameObject("canonical sun");lamp.transform.SetParent(root.transform,false);var sun=lamp.AddComponent<Light>();sun.type=LightType.Directional;RaceLightingController.Apply(decor.Plan.lighting,sun,null,null);
     var cameraObject=new GameObject("canonical camera");cameraObject.transform.SetParent(root.transform,false);var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.fieldOfView=65;camera.nearClipPlane=.1f;camera.farClipPlane=ChaseCamera.MinimumFarClip;camera.clearFlags=CameraClearFlags.Skybox;
     string name=theme+"-"+(candidate?"candidate":"baseline");File.WriteAllText(Path.Combine(Output,name+"-inventory.json"),JsonUtility.ToJson(new Inventory{rows=BasisCompatibilityChecks.VisualInventory(track.PresentationRoot).Concat(BasisCompatibilityChecks.VisualInventory(decor.Root)).ToArray()},true));
     var car=Car(root,track,0,80,0);
     var chase=cameraObject.AddComponent<ChaseCamera>();chase.target=car;chase.SetSkyline(decor.Plan);chase.enabled=false;
     float vertical=track.PresentationRoute.Samples.Where(f=>f.distance>=track.PresentationRoute.LoopStart&&f.distance<=track.PresentationRoute.LoopEnd-10).OrderBy(f=>Mathf.Abs(Vector3.Dot(f.normal,Vector3.up))).First().distance;
     Check(Mathf.Abs(Vector3.Dot(track.PresentationRoute.Evaluate(vertical).normal,Vector3.up))<.3f,"actual vertical loop frame");
     foreach(float distance in new[]{80f,vertical}){
      // Establish a real safe road checkpoint before prescribed visual placement on the loop.
      // Arbitrary vertical ResetAt is not checkpoint admission; recovery must return to the proven point.
      car.ResetAt(80,0);Check(car.HasProvenCheckpoint,"safe visual fixture checkpoint");
      var placed=track.Route.Evaluate(distance);car.Distance=distance;car.Frame=placed;car.Body.position=placed.position+placed.normal*VehicleGeometry.AdheredBodyHeight;car.Body.rotation=placed.Rotation;car.transform.SetPositionAndRotation(car.Body.position,car.Body.rotation);car.ResetPresentation();
      Presentation(car,track,"visual");var f=track.PresentationRoute.Evaluate(distance);camera.transform.position=f.position+f.normal*5-f.tangent*10;camera.transform.LookAt(f.position+f.tangent*15,f.normal);Capture(camera,name+"-"+distance.ToString("0")+".png");
      VisualEffects(car,track,camera,chase,name+"-"+distance.ToString("0"),distance);
     }
     File.AppendAllText(Path.Combine(Output,"visual.txt"),$"candidate={candidate} theme={theme} seed={seed} geometry={course.GeometryIdentity} replay={course.ReplayIdentity(track)}\n");
    }finally{decor.Clear();UnityEngine.Object.DestroyImmediate(root);}
   }
  }
  static void VisualEffects(MagneticVehicle car,TrackBuilder track,Camera camera,ChaseCamera chase,string name,float distance){
   const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
   var position=car.Body.position;var velocity=car.Body.linearVelocity;var rotation=car.Body.rotation;
   typeof(MagneticVehicle).GetMethod("UpdatePresentation",flags).Invoke(car,null);
   var pivot=car.transform.Find("Vehicle presentation");
   float visualDelta=Vector3.Distance(pivot.position,car.RenderPose.position);
   File.AppendAllText(Path.Combine(Output,"visual-roundtrip.txt"),name+" delta="+visualDelta.ToString("R")+" colliders="+pivot.GetComponentsInChildren<Collider>().Length+"\n");
   // Existing VehiclePresentationChecks whole-tick visual tolerance; not bitwise transform equality.
   Check(visualDelta<.001f&&pivot.GetComponentsInChildren<Collider>().Length==0,"canonical collider-free vehicle visual (existing 1mm presentation bound)");
   chase.Snap();typeof(ChaseCamera).GetMethod("UpdateCamera",flags).Invoke(chase,new object[]{.016f});
   Check((camera.transform.position-car.RenderPose.position).magnitude<20,"production chase follows canonical car");
   Check(car.Body.position.Equals(position)&&car.Body.rotation.Equals(rotation)&&car.Body.linearVelocity.Equals(velocity),"presentation and camera do not write physics");
   car.SetRaceContext(true,1,1);car.SetPresentationInput(1,true);
   for(int i=0;i<30;i++)typeof(MagneticVehicle).GetMethod("UpdateExhaust",flags).Invoke(car,new object[]{.02f});
   Check(car.GetComponentInChildren<VehicleExhaust>().Emitting,"production exhaust emits on canonical visual");
   var marks=track.TireMarks;marks.Clear();float now=Time.time;
   for(int step=0;step<12;step++){
    float progress=distance-5+step*.4f;var frame=track.Route.Evaluate(progress);
    CheckpointAdmission.Support Contact(float side){var origin=frame.position+frame.right*side+frame.normal*.5f;Check(track.RaycastRoad(origin,-frame.normal,1,progress,out var hit),"real road hit for canonical marks");return new CheckpointAdmission.Support(origin,-frame.normal,1,hit);}
    marks.Observe(0,car.PositionRevision,frame.tangent*30+frame.right*10,frame.tangent,frame.normal,Contact(-.7f),Contact(.7f),track,true,now+step*.02f);
   }
   marks.Flush(now+.24f);Check(marks.LiveSegments>=20&&marks.RejectedSegments==0,"real validated contacts emit canonical marks");
   foreach(var vertex in marks.RenderMesh.vertices.Where(v=>v.sqrMagnitude>0)){
    var point=marks.transform.TransformPoint(vertex);var nearest=track.PresentationRoute.Project(point,distance,30);
    Check(Mathf.Abs(Vector3.Dot(point-nearest.position,nearest.normal))<.08f,"marks lie on canonical rendered road");
   }
   Capture(camera,name+"-effects.png");
   Check(car.HasProvenCheckpoint,"visual recovery has proven checkpoint");int revision=car.PositionRevision;
   car.Body.position+=car.Frame.right*30;car.transform.position=car.Body.position;car.ResetToCheckpoint();
   Check(car.PositionRevision==revision+1,"actual checkpoint restore advances revision");Presentation(car,track,"after checkpoint recovery");
   typeof(MagneticVehicle).GetMethod("UpdateExhaust",flags).Invoke(car,new object[]{0f});
   Check(!car.GetComponentInChildren<VehicleExhaust>().Emitting,"recovery resets stale exhaust");
   chase.Snap();typeof(ChaseCamera).GetMethod("UpdateCamera",flags).Invoke(chase,new object[]{.016f});Capture(camera,name+"-recovery.png");
   marks.Clear();File.AppendAllText(Path.Combine(Output,"visual-effects.txt"),name+" canonical camera/exhaust/validated road marks/checkpoint recovery PASS\n");
  }
  [Serializable]sealed class Inventory{public string[] rows;}
  static void Capture(Camera camera,string filename){
   var target=new RenderTexture(1600,900,24);target.Create();var previous=RenderTexture.active;var pixels=new Texture2D(1600,900,TextureFormat.RGB24,false);
   try{RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1600,900),0,0);pixels.Apply();Check(pixels.GetPixels32().Count(c=>c.r>45||c.g>45||c.b>45)>2000,"nonblank visual capture");File.WriteAllBytes(Path.Combine(Output,filename),pixels.EncodeToPNG());}
   finally{RenderTexture.active=previous;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixels);}
  }
 }
}

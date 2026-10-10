using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace StarRacingPrototype {
 [InitializeOnLoad] public static class LiveVehicleDriveChecks {
  const string Key="StarRacing.LiveDrive";static int startup;static double began;
  public static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_LIVE_DRIVE_DIR");
  public static bool Motion=>Environment.GetEnvironmentVariable("STAR_RACING_LIVE_MODE")=="motion";
  static LiveVehicleDriveChecks(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run(){if(string.IsNullOrEmpty(Output))throw new Exception("Output required");Directory.CreateDirectory(Output);SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",false);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);startup=0;EditorApplication.update+=Enter;}
  static void Enter(){if(++startup<15)return;EditorApplication.update-=Enter;EditorApplication.isPlaying=true;}
  static void Guard(){if(EditorApplication.timeSinceStartup-began>900)Fail(new Exception("Live PlayerLoop timeout"));}
  static void Changed(PlayModeStateChange state){if(!SessionState.GetBool(Key,false))return;
   if(state==PlayModeStateChange.EnteredPlayMode){began=EditorApplication.timeSinceStartup;EditorApplication.update+=Guard;AudioListener.volume=0;Time.fixedDeltaTime=.01f;Time.maximumDeltaTime=.1f;Time.captureDeltaTime=Motion?1f/144:.05f;new GameObject("live fixture").AddComponent<LiveVehicleDriver>();}
   if(state==PlayModeStateChange.EnteredEditMode){EditorApplication.update-=Guard;SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",false)?1:0);}
  }
  public static void Fail(Exception e){Debug.LogException(e);File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());SessionState.SetBool(Key+"Failed",true);EditorApplication.isPlaying=false;}
 }
 [Serializable] public sealed class LiveRampResult {public string theme,jump;public bool human,nitro;public float initialSpeed,takeoffSpeed,flightSeconds,peak,landingDistance,turnMargin,capImpulse;}
 [Serializable] public sealed class LiveRampReport {public List<LiveRampResult> cases=new List<LiveRampResult>();}
 [DefaultExecutionOrder(-100)] public sealed class LiveVehicleDriver:MonoBehaviour {
  public MagneticVehicle car;public LiveRampResult current;TrackBuilder track;GameObject trackRoot;Procedural.Jump jump;
  int scenario,tick,stable,flightTicks,revision;bool launched,boostSeen;float initialDistance,landing=-1,capImpulse;LiveRampReport report=new LiveRampReport();StringBuilder frames=new StringBuilder();float previousZ;double previousTime;int warmFrames,zeroFrames;float maxMotionError;
  public static void Check(bool ok,string why){if(!ok)throw new Exception("Live vehicle: "+why);}
  void Start(){try{gameObject.AddComponent<RacePhysicsStepper>();gameObject.AddComponent<LiveVehicleProbe>().driver=this;Setup();}catch(Exception e){LiveVehicleDriveChecks.Fail(e);}}
  void Setup(){
   int themeIndex=scenario/24,jumpIndex=scenario%4,speedIndex=(scenario/4)%3;bool human=(scenario/12)%2==0;
   string theme=themeIndex==0?"cloud-city":"space-station";
   if(track==null || (!LiveVehicleDriveChecks.Motion && track.Route.Definition.theme!=theme)){
    if(trackRoot!=null)DestroyImmediate(trackRoot);trackRoot=new GameObject("live road");track=trackRoot.AddComponent<TrackBuilder>();
    track.Build(new TrackRoute(LiveVehicleDriveChecks.Motion?RoadVolumeChecks.FlatRoad():Procedural.Generator.Generate(2273265884,"normal",theme,true,true)));
   }
   var balance=ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);var capability=new DrivingBalance(balance);
   float speed=LiveVehicleDriveChecks.Motion?capability.BaseSpeed:speedIndex==0?42:speedIndex==1?capability.BaseSpeed:capability.NitroSpeed;
   jump=LiveVehicleDriveChecks.Motion?null:track.Route.Definition.jumps[jumpIndex];
   initialDistance=jump==null?80:30+jump.rampStartIndex*5-10;
   var obj=new GameObject("live entrant");obj.transform.SetParent(trackRoot.transform);car=obj.AddComponent<MagneticVehicle>();car.Initialize(track,0,Color.cyan,balance,human);car.ResetAt(initialDistance,jump==null?0:-(float)jump.lateralCenter);car.Body.linearVelocity=car.Frame.tangent*speed;Physics.SyncTransforms();
   current=new LiveRampResult{theme=theme,jump=jump?.id??"straight",human=human,nitro=!LiveVehicleDriveChecks.Motion&&speedIndex==2,initialSpeed=speed};
   car.ContactObserved+=c=>{if(!launched&&c.collider.name==RoadVolumeMesh.SupportName)capImpulse=Mathf.Max(capImpulse,c.impulse.magnitude);};
   tick=stable=flightTicks=0;launched=boostSeen=false;landing=-1;capImpulse=0;revision=car.PositionRevision;
   if(LiveVehicleDriveChecks.Motion){frames.AppendLine("time,fixedTime,dt,bodyZ,renderZ,speed");var cam=new GameObject("probe camera");cam.transform.SetParent(trackRoot.transform);cam.AddComponent<Camera>().targetTexture=new RenderTexture(64,64,16);cam.AddComponent<ChaseCamera>().target=car;}
  }
  void FixedUpdate(){try{if(car==null)return;car.PrepareProjection();car.SetRaceContext(true,1,1,tick*.01);car.SetInput(new DrivingInput{throttle=1,nitro=current.nitro});car.Hold(false);tick++;
   if(LiveVehicleDriveChecks.Motion){if(tick>=80)FinishMotion();return;}
   Check(tick<500,"ramp failed to settle "+current.theme+"/"+current.jump);
  }catch(Exception e){LiveVehicleDriveChecks.Fail(e);}}
  public void ObservePrepared(){if(car==null||LiveVehicleDriveChecks.Motion)return;
   boostSeen|=car.Drive.Active;
   if(car.ActiveJumpId!=""){
    if(!launched){launched=true;current.takeoffSpeed=Vector3.ProjectOnPlane(car.Body.linearVelocity,car.Frame.normal).magnitude;Check(current.takeoffSpeed>=current.initialSpeed*.9f,"wrong takeoff speed");Check(!current.nitro||boostSeen,"nitro never activated");}
    flightTicks++;current.peak=Mathf.Max(current.peak,Vector3.Dot(car.Body.position-car.Frame.position,car.Frame.normal));
    if(car.HandlingSupportCount==0){var packet=(VehicleForceFrame)typeof(MagneticVehicle).GetField("preparedForces",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(car);Check(Vector3.Distance(packet.acceleration,car.GravityAcceleration)<.0001f && packet.force.sqrMagnitude<.0001f,"adhesion/drive modified ballistic flight");}
   }
   Check(car.PositionRevision==revision&&!car.IsFalling,"ramp triggered fall/recovery");
   if(launched&&car.ActiveJumpId==""&&car.HandlingSupportCount>=3&&car.Distance>30+jump.gapEndIndex*5){if(landing<0)landing=car.Distance;stable++;}else stable=0;
   if(stable<20)return;
   var run=Procedural.Generator.Runs(track.Route.Definition.samples,"jump-straight").Find(x=>jump.launchIndex>=x.start&&jump.launchIndex<x.start+x.length);
   current.flightSeconds=flightTicks*.01f;current.landingDistance=landing;current.turnMargin=30+(run.start+run.length)*5-car.Distance;current.capImpulse=capImpulse;
   Check(landing<=30+jump.ballisticLandingEndIndex*5,"landing exceeded certified envelope");Check(current.turnMargin>=50,"landing too close to turn");Check(capImpulse<5000,"hard exterior cap impact");Check(car.Body.linearVelocity.magnitude>=current.initialSpeed*.8f,"ramp lost tempo");
   report.cases.Add(current);File.WriteAllText(Path.Combine(LiveVehicleDriveChecks.Output,"ramps.json"),JsonUtility.ToJson(report,true));Debug.Log($"LIVE_RAMP_OK case={scenario+1} theme={current.theme} jump={current.jump} human={current.human} nitro={current.nitro} takeoff={current.takeoffSpeed:F2} flight={current.flightSeconds:F2} peak={current.peak:F2} margin={current.turnMargin:F1} cap={capImpulse:F1}");
   DestroyImmediate(car.gameObject);car=null;if(++scenario<48)Setup();else{File.WriteAllText(Path.Combine(LiveVehicleDriveChecks.Output,"result.txt"),"LIVE_RAMPS_OK cases=48\n");EditorApplication.isPlaying=false;}
  }
  void LateUpdate(){if(car==null||!LiveVehicleDriveChecks.Motion)return;var pose=car.RenderPose;float z=pose.position.z;double now=Time.timeAsDouble;frames.AppendLine($"{now:F6},{Time.fixedTimeAsDouble:F6},{Time.deltaTime:F6},{car.Body.position.z:F6},{z:F6},{car.Body.linearVelocity.magnitude:F4}");
   if(tick>20&&previousTime>0){float expected=car.Body.linearVelocity.z*(float)(now-previousTime),actual=z-previousZ;if(Mathf.Abs(actual)<.001f)zeroFrames++;maxMotionError=Mathf.Max(maxMotionError,Mathf.Abs(actual-expected));warmFrames++;}previousZ=z;previousTime=now;
  }
  void FinishMotion(){File.WriteAllText(Path.Combine(LiveVehicleDriveChecks.Output,"frames.csv"),frames.ToString());Check(warmFrames>30&&zeroFrames==0&&maxMotionError<.005f,"render motion not continuous");File.WriteAllText(Path.Combine(LiveVehicleDriveChecks.Output,"result.txt"),$"LIVE_MOTION_OK warmFrames={warmFrames} zeroFrames={zeroFrames} maxError={maxMotionError:F6}\n");car=null;EditorApplication.isPlaying=false;}
 }
 [DefaultExecutionOrder(999)] public sealed class LiveVehicleProbe:MonoBehaviour {public LiveVehicleDriver driver;void FixedUpdate(){try{driver.ObservePrepared();}catch(Exception e){LiveVehicleDriveChecks.Fail(e);}}}
}

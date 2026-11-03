using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
namespace StarRacingPrototype {
 [InitializeOnLoad] public static class AuthoredCoursePlayChecks {
  const string Key="StarRacing.AuthoredPlay";static double deadline;
  static AuthoredCoursePlayChecks(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run(){
   if(!Environment.GetCommandLineArgs().Contains("-noaudio")||!Environment.GetCommandLineArgs().Contains("-muted"))throw new Exception("Muted startup required");
   Directory.CreateDirectory(AuthoredCourseChecks.Output);SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",true);
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.isPlaying=true;
  }
  static void Changed(PlayModeStateChange state){if(!SessionState.GetBool(Key,false))return;
   if(state==PlayModeStateChange.EnteredPlayMode){deadline=EditorApplication.timeSinceStartup+1200;EditorApplication.update+=Guard;new GameObject("Authored ordinary race QA").AddComponent<AuthoredPlayDriver>();}
   if(state==PlayModeStateChange.EnteredEditMode){EditorApplication.update-=Guard;SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",true)?1:0);}
  }
  static void Guard(){EditorApplication.QueuePlayerLoopUpdate();if(EditorApplication.timeSinceStartup>deadline)Finish(new TimeoutException("Authored ordinary race deadline"));}
  public static void Finish(Exception failure=null){if(failure!=null){File.WriteAllText(Path.Combine(AuthoredCourseChecks.Output,"failure.txt"),failure.ToString());Debug.LogException(failure);}SessionState.SetBool(Key+"Failed",failure!=null);EditorApplication.isPlaying=false;}
 }
 [DefaultExecutionOrder(-200)] public sealed class AuthoredPlayDriver:MonoBehaviour {
  GameObject host;RaceDirector director;RaceMenu menu;bool had,restored;string saved,settingsPath;UserSettings settings;
  Gamepad pad;AiDriver pilot;AiWorldSnapshot pilotWorld;double nextDecision;DrivingInput command;
  int[] revisions;float[] stalled;int captured=-1,frames,orbitCapture;Result result=new Result();
  [Serializable] class Result {public bool passed;public string geometry,presentationNote="Ordinary 8 entrant race: seven stock AI and one local seat driven by stock Rookie commands through a virtual gamepad. Existing physics and ChaseCamera. Editor capture is not human/device acceptance.";public double preparationSeconds,elapsed;public int recoveries;public float maxStall;public Car[] cars;}
  [Serializable] class Car {public int entrant;public float distance,speed;public bool finished;public double finishTime;}
  IEnumerator Start(){
   had=PlayerPrefs.HasKey(LocalRaceConfig.PreferenceKey);saved=PlayerPrefs.GetString(LocalRaceConfig.PreferenceKey);settingsPath=UserSettingsStore.OverridePath;settings=UserSettingsStore.Current;
   UserSettingsStore.OverridePath=Path.Combine(AuthoredCourseChecks.Output,"user-settings.json");UserSettingsStore.Replace(new UserSettings{frameLimit=0,antiAliasing=2,stickDeadzone=0});
   pad=InputSystem.AddDevice<Gamepad>();
   var selected=AuthoredCourseChecks.SelectedConfig(8,1);selected.devices[0]=SessionControllers.Slot(pad);selected.Save();Application.runInBackground=true;
   Exception failure=null;var run=Run();while(true){bool more;try{more=run.MoveNext();}catch(Exception e){failure=e;break;}if(!more)break;yield return run.Current;}
   Save();Restore();yield return null;AuthoredCoursePlayChecks.Finish(failure);
  }
  void Event(string value)=>File.AppendAllText(Path.Combine(AuthoredCourseChecks.Output,"events.txt"),DateTime.UtcNow.ToString("O")+" "+value+"\n");
  IEnumerator Run(){
   Event("ordinary menu startup");host=new GameObject("Normal game startup");host.AddComponent<RaceStartup>();
   while(director==null||!director.Ready){director=host.GetComponent<RaceDirector>();yield return null;}
   menu=host.GetComponent<RaceMenu>();menu.Open(RaceMenuScreen.LocalSetup);menu.SelectCourse(AuthoredCourseChecks.SelectedConfig().courseId);menu.Selected.humans=1;menu.Selected.entrants=8;menu.Selected.devices[0]=SessionControllers.Slot(pad);
   Event("start selected authored course");double start=Time.realtimeSinceStartupAsDouble;menu.StartSelected();while(menu.Loading)yield return null;
   result.preparationSeconds=Time.realtimeSinceStartupAsDouble-start;
   if(!director.Started||!director.Course.IsAuthored||director.Cars.Length!=8)throw new Exception("Normal authored start failed: "+menu.Error);
   foreach(var collider in director.Track.GetComponentsInChildren<MeshCollider>())if(collider.sharedMesh!=null&&collider.sharedMesh.GetIndexCount(0)>2097152&&(collider.cookingOptions&MeshColliderCookingOptions.UseFastMidphase)!=0)throw new Exception("Oversized collider still uses unsupported Fast Midphase: "+collider.name);
   result.geometry=director.Course.GeometryIdentity;revisions=director.Cars.Select(c=>c.PositionRevision).ToArray();stalled=new float[8];
   var chase=director.CameraFor(0);chase.target=director.Cars[0];chase.enabled=true;chase.GetComponent<Camera>().enabled=true;chase.GetComponent<Camera>().rect=new Rect(0,0,1,1);chase.Snap();
   pilot=new AiDriver(0,DriverProfile.Rookie,77,director.Balance);pilotWorld=new AiWorldSnapshot(8);
   Time.captureDeltaTime=.02f;Event("race started");
   while(!director.Session.Racers.All(r=>r.Finished)&&director.Session.Phase!=RacePhase.Results&&director.Session.Elapsed<420){
    yield return null;
    if(director.Session.Phase==RacePhase.Countdown)continue;
    for(int i=0;i<8;i++){
     var car=director.Cars[i];if(car.PositionRevision!=revisions[i]){result.recoveries++;revisions[i]=car.PositionRevision;Save();throw new Exception("Ordinary race recovery at entrant "+i+" distance "+car.Distance);}
     if(!director.Session.Racers[i].Finished&&director.Session.Elapsed>5&&car.Body.linearVelocity.magnitude<3){stalled[i]+=Time.deltaTime;result.maxStall=Mathf.Max(result.maxStall,stalled[i]);}else stalled[i]=0;
     if(stalled[i]>5)throw new Exception("Ordinary race stalled at entrant "+i+" distance "+car.Distance);
    }
    int section=director.Track.Route.SourceAt(director.Cars[0].Distance).segmentIndex;
    if(section>captured){captured=section;AuthoredPreviewDriver.Capture(chase.GetComponent<Camera>(),"race-section-"+section);Event("section "+section+" elapsed="+director.Session.Elapsed);}
    if(AuthoredCourseChecks.Orbital){
     var orbit=director.Course.Single.composition[5];float fraction=(director.Cars[0].Distance-30-orbit.startIndex*5)/((orbit.endIndex-orbit.startIndex)*5f);
     if(fraction>=orbitCapture*.2f&&orbitCapture<=5){AuthoredPreviewDriver.Capture(chase.GetComponent<Camera>(),"orbit-"+orbitCapture);Event("orbit fraction "+fraction+" normal="+director.Cars[0].Frame.normal);orbitCapture++;}
    }
    if(++frames%100==0)Save();
   }
   if(!director.Session.Racers.All(r=>r.Finished))throw new Exception("Not all eight ordinary AI finished in 420 simulated seconds");
   Event("all eight entrants finished");Save();File.Copy(Path.Combine(AuthoredCourseChecks.Output,"race-result.json"),Path.Combine(AuthoredCourseChecks.Output,"finish-evidence.json"));
   // Same-world repeat must retain geometry while publishing a fresh race.
   var oldTrack=director.Track;string identity=director.Course.GeometryIdentity;
   menu.RepeatRace();while(menu.Loading)yield return null;
   if(!director.Started||director.Track!=oldTrack||director.Course.GeometryIdentity!=identity)throw new Exception("Authored repeat/cache mismatch");
   director.SetPaused(true);result.passed=true;Event("same world repeat passed");
  }
  void Update(){
   if(pad==null||pilot==null||director==null||!director.Started||director.Paused||director.Session==null)return;
   if(director.Session.Elapsed>=nextDecision){pilotWorld.Capture(director.Cars,director.Track.Route,director.Session.Elapsed);command=pilot.Step(pilotWorld,director.Track.Route,.1f,false);nextDecision=director.Session.Elapsed+.1;}
   float value=command.steer;float raw=Mathf.Abs(value)<.0001f?0:Mathf.Sign(value)*Mathf.Lerp(InputSystem.settings.defaultDeadzoneMin,InputSystem.settings.defaultDeadzoneMax,Mathf.Abs(value));
   var state=new GamepadState{leftStick=new Vector2(raw,0),rightTrigger=command.throttle};
   if(command.brake>.5f)state=state.WithButton(GamepadButton.South);if(command.drift)state=state.WithButton(GamepadButton.LeftShoulder);if(command.nitro)state=state.WithButton(GamepadButton.East);
   InputSystem.QueueStateEvent(pad,state);
  }
  void Save(){
   if(director?.Session!=null&&director.Cars!=null){result.elapsed=director.Session.Elapsed;result.cars=director.Cars.Select((c,i)=>new Car{entrant=i,distance=c.Distance,speed=c.Body.linearVelocity.magnitude,finished=director.Session.Racers[i].Finished,finishTime=director.Session.Racers[i].FinishTime}).ToArray();}
   File.WriteAllText(Path.Combine(AuthoredCourseChecks.Output,"race-result.json"),JsonUtility.ToJson(result,true));
  }
  void Restore(){if(restored)return;restored=true;Time.captureDeltaTime=0;Time.timeScale=1;
   if(had)PlayerPrefs.SetString(LocalRaceConfig.PreferenceKey,saved);else PlayerPrefs.DeleteKey(LocalRaceConfig.PreferenceKey);PlayerPrefs.Save();
   UserSettingsStore.OverridePath=settingsPath;UserSettingsStore.Replace(settings);if(host!=null)Destroy(host);if(pad!=null&&pad.added)InputSystem.RemoveDevice(pad);
  }
  void OnDestroy()=>Restore();
 }
}

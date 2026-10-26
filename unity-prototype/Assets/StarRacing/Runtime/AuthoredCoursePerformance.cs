#if STAR_RACING_TEST_CHANNEL && !UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace StarRacingPrototype {
 // Local diagnostic Player only; never installed without an explicit command argument.
 [DefaultExecutionOrder(-200)] public sealed class AuthoredCoursePerformance:MonoBehaviour {
  const string Argument="--authored-course-performance";
  [Serializable] public sealed class Sample {public string window;public int section;public double ms,cpuMs,gpuMs,physicsMs;public bool timingAvailable,focused;public long triangles,batches;}
  [Serializable] public sealed class Report {
   public string device,gpu,geometry,failure;public bool completed;public int width,height,entrants=16,recoveries;public double preparationSeconds;
   public string limitation="Local diagnostic Player. Live window: 15 standard AI + one virtual gamepad driven by stock Rookie commands. Frozen windows measure rendering only. Uncapped 1080p, default graphics and SMAA. Missing timing/counters are unavailable. No human/device acceptance or comparison with a procedural track.";
   public List<Sample> samples=new List<Sample>();
  }
  string output,saved;bool had,restored,collect;double deadline,previous,nextDecision;string window;
  UserSettings settings;Report report=new Report();RaceDirector director;RaceMenu menu;Gamepad[] pads;
  AiDriver pilot;AiWorldSnapshot world;DrivingInput command;int[] revisions;int captureSection=-1;
  ProfilerRecorder batches,triangles;FrameTiming[] timing=new FrameTiming[1];ulong lastTiming;
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)] static void Install(){
   var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,Argument);if(i<0)return;
   if(!SilentStartup.Enabled||i+1>=args.Length||!Path.IsPathRooted(args[i+1]))throw new ArgumentException("Silent absolute diagnostic output required");
   string path=args[i+1];if(Directory.Exists(path))throw new IOException("Fresh diagnostic directory required");Directory.CreateDirectory(path);
   var host=new GameObject("Authored course performance");DontDestroyOnLoad(host);var p=host.AddComponent<AuthoredCoursePerformance>();p.output=path;p.deadline=Time.realtimeSinceStartupAsDouble+600;
   p.had=PlayerPrefs.HasKey(LocalRaceConfig.PreferenceKey);p.saved=PlayerPrefs.GetString(LocalRaceConfig.PreferenceKey);p.settings=UserSettingsStore.Current;
   UserSettingsStore.Replace(new UserSettings{frameLimit=0,antiAliasing=2,stickDeadzone=0,display=new DisplaySettings{fullscreen=false,width=1920,height=1080,vSync=false}});
   p.pads=Enumerable.Range(0,4).Select(_=>InputSystem.AddDevice<Gamepad>()).ToArray();
   new LocalRaceConfig{humans=1,entrants=16,courseId=AuthoredCloudCityCourse.Id,courseRevision=AuthoredCloudCityCourse.Revision,theme="cloud-city",rails="full",seed="77",devices=new[]{SessionControllers.Slot(p.pads[0]),SessionControllers.Slot(p.pads[1]),SessionControllers.Slot(p.pads[2]),SessionControllers.Slot(p.pads[3])}}.Save();
  }
  IEnumerator Start(){var run=Run();Exception failure=null;while(true){bool more;try{more=run.MoveNext();}catch(Exception e){failure=e;break;}if(!more)break;yield return run.Current;}Finish(failure);}
  IEnumerator Run(){
   Application.runInBackground=true;
   while(director==null||!director.Ready){director=FindAnyObjectByType<RaceDirector>();yield return null;}
   menu=director.GetComponent<RaceMenu>();menu.Open(RaceMenuScreen.LocalSetup);menu.SelectCourse(true);
   // The normal menu validates display modes against the monitor catalogue and
   // can replace 1080p with native size. The diagnostic owns this window only.
   Screen.SetResolution(1920,1080,FullScreenMode.Windowed);
   double resizeDeadline=Time.realtimeSinceStartupAsDouble+3;
   while((Screen.width!=1920||Screen.height!=1080)&&Time.realtimeSinceStartupAsDouble<resizeDeadline)yield return null;
   // Capture the actual menu, including its selected course, outside measured windows.
   yield return new WaitForSecondsRealtime(1);ScreenCapture.CaptureScreenshot(Path.Combine(output,"menu.png"));yield return new WaitForSecondsRealtime(.3f);
   double began=Time.realtimeSinceStartupAsDouble;menu.StartSelected();while(menu.Loading)yield return null;
   report.preparationSeconds=Time.realtimeSinceStartupAsDouble-began;
   if(!director.Started||!director.Course.IsAuthored||director.Cars.Length!=16)throw new Exception("Normal authored start failed: "+menu.Error);
   report.device=SystemInfo.deviceModel;report.gpu=SystemInfo.graphicsDeviceName;report.width=Screen.width;report.height=Screen.height;report.geometry=director.Course.GeometryIdentity;
   if(Screen.width!=1920||Screen.height!=1080)throw new Exception("Expected 1920x1080 window");
   revisions=director.Cars.Select(c=>c.PositionRevision).ToArray();pilot=new AiDriver(0,DriverProfile.Rookie,77,director.Balance);world=new AiWorldSnapshot(16);
   batches=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Batches Count",1);triangles=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Triangles Count",1);
   while(director.Session.Phase==RacePhase.Countdown||director.Session.Elapsed<2)yield return null;
   RaceCpuDiagnostics.Start();Begin("live");
   while(director.Session.Phase!=RacePhase.Results&&!director.Session.Racers.All(r=>r.Finished)){
    yield return null;
    for(int i=0;i<16;i++)if(director.Cars[i].PositionRevision!=revisions[i]){report.recoveries++;throw new Exception("Recovery during measured race at seat "+i);}
    int section=director.Track.Route.SourceAt(director.Cars[0].Distance).segmentIndex;
    if(section>captureSection){collect=false;captureSection=section;ScreenCapture.CaptureScreenshot(Path.Combine(output,"race-"+section+".png"));yield return new WaitForSecondsRealtime(.3f);RaceCpuDiagnostics.Drain();Begin("live");}
   }
   collect=false;RaceCpuDiagnostics.Stop();pilot=null;director.SetPaused(true);director.GetComponent<RaceHud>().enabled=false;
   foreach(int section in new[]{2,4,6}){
    var part=director.Course.Single.composition[section];director.Cars[0].ResetAt(30+part.startIndex*5+35,0);director.CameraFor(0).Snap();
    yield return new WaitForSecondsRealtime(.5f);ScreenCapture.CaptureScreenshot(Path.Combine(output,"view-"+section+".png"));yield return new WaitForSecondsRealtime(.5f);
    Begin("frozen-"+section);yield return new WaitForSecondsRealtime(5);collect=false;
   }
   if(!director.ExitToMenu())throw new Exception("Exit to menu failed");menu.Selected.humans=4;for(int i=0;i<4;i++)menu.Selected.devices[i]=SessionControllers.Slot(pads[i]);
   menu.StartSelected();while(menu.Loading)yield return null;if(!director.Started||director.HumanCount!=4)throw new Exception("Four viewport preparation failed");director.SetPaused(true);
   for(int i=0;i<4;i++){director.Cars[i].ResetAt(30+director.Course.Single.composition[6].startIndex*5+40+i*12,i%2==0?-3:3);director.CameraFor(i).Snap();}
   yield return new WaitForSecondsRealtime(.5f);ScreenCapture.CaptureScreenshot(Path.Combine(output,"four-views.png"));yield return new WaitForSecondsRealtime(.5f);
   Begin("frozen-four-views");yield return new WaitForSecondsRealtime(5);collect=false;report.completed=true;
  }
  void Begin(string name){window=name;previous=Time.realtimeSinceStartupAsDouble;collect=true;}
  void Update(){
   if(Time.realtimeSinceStartupAsDouble>deadline){Finish(new TimeoutException("Authored performance deadline"));return;}
   if(pilot==null||director==null||!director.Started||director.Paused)return;
   if(director.Session.Elapsed>=nextDecision){world.Capture(director.Cars,director.Track.Route,director.Session.Elapsed);command=pilot.Step(world,director.Track.Route,.1f,false);nextDecision=director.Session.Elapsed+.1;}
   float v=command.steer,raw=Mathf.Abs(v)<.0001f?0:Mathf.Sign(v)*Mathf.Lerp(InputSystem.settings.defaultDeadzoneMin,InputSystem.settings.defaultDeadzoneMax,Mathf.Abs(v));
   var s=new GamepadState{leftStick=new Vector2(raw,0),rightTrigger=command.throttle};if(command.brake>.5f)s=s.WithButton(GamepadButton.South);if(command.drift)s=s.WithButton(GamepadButton.LeftShoulder);if(command.nitro)s=s.WithButton(GamepadButton.East);InputSystem.QueueStateEvent(pads[0],s);
  }
  void LateUpdate(){
   if(!collect)return;double now=Time.realtimeSinceStartupAsDouble;var cpu=RaceCpuDiagnostics.Enabled?RaceCpuDiagnostics.Drain():default;
   var s=new Sample{window=window,section=director.Track.Route.SourceAt(director.Cars[0].Distance).segmentIndex,ms=(now-previous)*1000,focused=Application.isFocused,physicsMs=cpu.stepper*1000d/RaceCpuDiagnostics.Frequency,batches=batches.Valid?batches.LastValue:-1,triangles=triangles.Valid?triangles.LastValue:-1};previous=now;
   if(FrameTimingManager.IsFeatureEnabled()){FrameTimingManager.CaptureFrameTimings();if(FrameTimingManager.GetLatestTimings(1,timing)>0&&timing[0].frameStartTimestamp>lastTiming){lastTiming=timing[0].frameStartTimestamp;s.timingAvailable=true;s.cpuMs=timing[0].cpuFrameTime;s.gpuMs=timing[0].gpuFrameTime;}}
   report.samples.Add(s);
  }
  void Restore(){if(restored)return;restored=true;collect=false;if(RaceCpuDiagnostics.Enabled)RaceCpuDiagnostics.Stop();batches.Dispose();triangles.Dispose();
   if(had)PlayerPrefs.SetString(LocalRaceConfig.PreferenceKey,saved);else PlayerPrefs.DeleteKey(LocalRaceConfig.PreferenceKey);PlayerPrefs.Save();UserSettingsStore.Replace(settings);foreach(var pad in pads)if(pad.added)InputSystem.RemoveDevice(pad);
  }
  void Finish(Exception failure){if(restored)return;report.failure=failure?.ToString();Restore();File.WriteAllText(Path.Combine(output,"performance.json"),JsonUtility.ToJson(report,true));if(failure!=null)Debug.LogException(failure);Application.Quit(failure==null?0:1);}
  void OnApplicationQuit(){if(!restored)Finish(new Exception("Interrupted diagnostic"));}
 }
}
#endif

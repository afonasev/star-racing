#if STAR_RACING_TEST_CHANNEL && !UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering.Universal;

namespace StarRacingPrototype {
 // Opt-in local benchmark. Four virtual pads exercise the ordinary human input path.
 [DefaultExecutionOrder(-200)] public sealed class Split64Performance : MonoBehaviour {
  public const string Identity="local.starracing.split64";
  [Serializable] public sealed class Sample {
   public string window; public double wall,ms,simulation,session,cpuMs,gpuMs;
   public bool focused,paused,timingAvailable,gpuAvailable; public float timeScale;public long steps,triangles,batches;
   public int gc0; public RaceCpuDiagnostics.Frame cpu;
  }
  [Serializable] public sealed class Report {
   public string device,gpu,course,geometry,config,settings,failure,liveStartUtc,liveEndUtc;
   public string limitation="Exclusive local Player, four stock Rookie virtual-pad drivers and 60 ordinary AI. Frozen windows are render-only. Missing GPU data is unavailable. No human acceptance. Native physical pixels, maximum menu effects, SMAA; uncapped timing.";
   public bool completed,usesBasis;public int width,height,cameras,entrants,recoveries;public double preparationSeconds;
   public long stopwatchFrequency;public List<Sample> samples=new List<Sample>(8192);
  }
  readonly Stopwatch wall=Stopwatch.StartNew();readonly FrameTiming[] timings=new FrameTiming[1];
  Report report=new Report();string output,window;RaceDirector director;Gamepad[] pads;
  AiDriver[] pilots;AiWorldSnapshot world;DrivingInput[] commands=new DrivingInput[4];
  int[] revisions;double nextDecision,previous;bool collect,finished;ulong lastTiming;
  ProfilerRecorder batches,triangles;
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)] static void Install(){
   var args=Environment.GetCommandLineArgs();if(Array.IndexOf(args,"--split64-output")<0)return;
   if(!SilentStartup.Enabled||Application.identifier!=Identity)throw new InvalidOperationException("Dedicated silent split64 Player required");
   string path=RaceComparisonDiagnostics.Value(args,"--split64-output"),course=RaceComparisonDiagnostics.Value(args,"--split64-course");
   if(!Path.IsPathRooted(path)||Directory.Exists(path))throw new IOException("Fresh absolute output required");
   if(!AuthoredCourses.Known(course,1))throw new ArgumentException("Known authored course required");
   Directory.CreateDirectory(path);var host=new GameObject("Opt-in split64 benchmark");DontDestroyOnLoad(host);
   var p=host.AddComponent<Split64Performance>();p.output=path;p.report.course=course;
   p.pads=Enumerable.Range(0,4).Select(_=>InputSystem.AddDevice<Gamepad>()).ToArray();
   UserSettingsStore.Replace(new UserSettings{frameLimit=0,antiAliasing=2,stickDeadzone=0,display=new DisplaySettings{fullscreen=true,width=3456,height=2234,vSync=false}});
   new LocalRaceConfig{humans=4,entrants=64,courseId=course,courseRevision=1,theme=AuthoredCourses.Theme(course),rails="full",seed="77",devices=p.pads.Select(SessionControllers.Slot).ToArray()}.Save();
  }
  IEnumerator Start(){var run=Run();Exception failure=null;while(true){bool more;try{more=run.MoveNext();}catch(Exception e){failure=e;break;}if(!more)break;yield return run.Current;}Finish(failure);}
  IEnumerator Run(){
   Application.runInBackground=true;
   while(director==null||!director.Ready){director=FindAnyObjectByType<RaceDirector>();yield return null;}
   var menu=director.GetComponent<RaceMenu>();menu.Open(RaceMenuScreen.LocalSetup);
   Screen.SetResolution(3456,2234,FullScreenMode.FullScreenWindow);
   while(!Application.isFocused)yield return null;
   double start=wall.Elapsed.TotalSeconds;menu.StartSelected();while(menu.Loading)yield return null;
   report.preparationSeconds=wall.Elapsed.TotalSeconds-start;
   if(!director.Started||director.Cars.Length!=64||director.HumanCount!=4||!director.Course.IsAuthored)throw new Exception("Exact scenario failed: "+menu.Error);
   report.device=SystemInfo.deviceModel;report.gpu=SystemInfo.graphicsDeviceName;report.width=Screen.width;report.height=Screen.height;
   report.entrants=director.Cars.Length;report.usesBasis=director.Course.UsesBasis;report.geometry=director.Course.GeometryIdentity;report.config=JsonUtility.ToJson(director.AppliedConfig);report.settings=JsonUtility.ToJson(UserSettingsStore.Current);report.stopwatchFrequency=RaceCpuDiagnostics.Frequency;
   report.cameras=Enumerable.Range(0,4).Count(i=>director.CameraFor(i).GetComponent<Camera>().enabled);
   // Loading may cross a macOS focus transition. Never include it in a timing window.
   UnityEngine.Debug.Log("SPLIT64_READY_WAIT_FOCUS");
   while(!Application.isFocused)yield return null;
   if(director.Paused)director.SetPaused(false);
   CheckConditions();Save();
   pilots=Enumerable.Range(0,4).Select(i=>new AiDriver(i,DriverProfile.Rookie,(uint)(77+i*31),director.Balance)).ToArray();world=new AiWorldSnapshot(64);
   revisions=director.Cars.Select(c=>c.PositionRevision).ToArray();
   batches=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Batches Count",1);triangles=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Triangles Count",1);
   while(director.Session.Phase==RacePhase.Countdown||director.Session.Elapsed<2)yield return null;
   report.liveStartUtc=DateTime.UtcNow.ToString("O");UnityEngine.Debug.Log("SPLIT64_LIVE_BEGIN "+report.liveStartUtc);RaceCpuDiagnostics.Start();Begin("live");double sim=RacePhysicsStepper.SimulatedSeconds;long ticks=RacePhysicsStepper.NativeSteps;start=wall.Elapsed.TotalSeconds;
   while(director.Session.Elapsed<32){
    CheckConditions();if(director.Paused)throw new Exception("Unexpected pause in live window");
    if(wall.Elapsed.TotalSeconds-start>120)throw new Exception("Live simulation failed to advance 30 seconds within 120 wall seconds");
    for(int i=0;i<64;i++)if(director.Cars[i].PositionRevision!=revisions[i]){report.recoveries++;revisions[i]=director.Cars[i].PositionRevision;}
    yield return null;
   }
   collect=false;RaceCpuDiagnostics.Stop();report.liveEndUtc=DateTime.UtcNow.ToString("O");
   if(RacePhysicsStepper.SimulatedSeconds-sim<29||RacePhysicsStepper.NativeSteps<=ticks)throw new Exception("Simulation clock/ticks invalid");
   ScreenCapture.CaptureScreenshot(Path.Combine(output,"live-end.png"));yield return new WaitForSecondsRealtime(.3f);
   // Release virtual driving buttons before showing the pause menu: nitro uses
   // the same East button that resumes that menu. Drain its edge outside timing.
   pilots=null;foreach(var pad in pads)InputSystem.QueueStateEvent(pad,new GamepadState());
   yield return null;yield return null;director.SetPaused(true);
   long frozenSteps=RacePhysicsStepper.NativeSteps;
   // Fixed positions separate fill/geometry cost from the live simulation, across the course.
   for(int pose=0;pose<3;pose++){
    for(int i=0;i<4;i++){
     var parts=director.Course.Single.composition;int section=Mathf.Min(parts.Length-1,pose==0?0:pose==1?parts.Length/2:parts.Length-2);
     director.Cars[i].ResetAt(30+parts[section].startIndex*5+40+i*12,i%2==0?-3:3);director.CameraFor(i).Snap();
    }
    yield return new WaitForSecondsRealtime(1);
    ScreenCapture.CaptureScreenshot(Path.Combine(output,"pose-"+pose+".png"));yield return new WaitForSecondsRealtime(.5f);
    Begin("frozen-"+pose);double until=wall.Elapsed.TotalSeconds+5;while(wall.Elapsed.TotalSeconds<until){CheckConditions();if(!director.Paused||Time.timeScale!=0||RacePhysicsStepper.NativeSteps!=frozenSteps)throw new Exception("Frozen render window resumed simulation");yield return null;}collect=false;
   }
   report.completed=true;
  }
  void CheckConditions(){
   var prefs=UserSettingsStore.Current;var g=prefs.graphics;
   if(Screen.width!=3456||Screen.height!=2234||!Application.isFocused||director.HumanCount!=4||director.Cars.Length!=64)throw new Exception($"Native conditions invalid: {Screen.width}x{Screen.height}, focused={Application.isFocused}, humans={director.HumanCount}, entrants={director.Cars.Length}");
   if(!g.contactShadows||!g.shadows||!g.bloom||!g.mist||!prefs.nitroBlur||prefs.antiAliasing!=2||QualitySettings.vSyncCount!=0||Application.targetFrameRate>0)throw new Exception("Maximum uncapped graphics invalid");
   if(Time.captureDeltaTime!=0||Mathf.Abs(Time.fixedDeltaTime-.01f)>1e-8f||RacePhysicsStepper.Substeps!=8)throw new Exception("Simulation cadence changed");
   for(int i=0;i<4;i++){var c=director.CameraFor(i).GetComponent<Camera>();if(!c.enabled||c.rect!=RaceViewports.For(4,i)||c.GetUniversalAdditionalCameraData().antialiasing!=AntialiasingMode.SubpixelMorphologicalAntiAliasing)throw new Exception("Four native SMAA viewports required");}
  }
  void Begin(string value){window=value;previous=wall.Elapsed.TotalSeconds;collect=true;}
  void Update(){
   if(finished)return;if(wall.Elapsed.TotalSeconds>600){Finish(new TimeoutException("Split64 wall deadline"));return;}
   if(pilots==null||director==null||director.Paused)return;
   if(director.Session.Elapsed>=nextDecision){world.Capture(director.Cars,director.Track.Route,director.Session.Elapsed);for(int i=0;i<4;i++)commands[i]=pilots[i].Step(world,director.Track.Route,.1f,false);nextDecision=director.Session.Elapsed+.1;}
   for(int i=0;i<4;i++){var c=commands[i];float v=c.steer,raw=Mathf.Abs(v)<.0001f?0:Mathf.Sign(v)*Mathf.Lerp(InputSystem.settings.defaultDeadzoneMin,InputSystem.settings.defaultDeadzoneMax,Mathf.Abs(v));
    var s=new GamepadState{leftStick=new Vector2(raw,0),rightTrigger=c.throttle};if(c.brake>.5f)s=s.WithButton(GamepadButton.South);if(c.drift)s=s.WithButton(GamepadButton.LeftShoulder);if(c.nitro)s=s.WithButton(GamepadButton.East);InputSystem.QueueStateEvent(pads[i],s);
   }
  }
  void LateUpdate(){
   if(!collect||finished)return;double now=wall.Elapsed.TotalSeconds;
   var s=new Sample{window=window,wall=now,ms=(now-previous)*1000,simulation=RacePhysicsStepper.SimulatedSeconds,session=director.Session.Elapsed,steps=RacePhysicsStepper.NativeSteps,focused=Application.isFocused,paused=director.Paused,timeScale=Time.timeScale,gc0=GC.CollectionCount(0),cpu=RaceCpuDiagnostics.Enabled?RaceCpuDiagnostics.Drain():default,batches=batches.Valid?batches.LastValue:-1,triangles=triangles.Valid?triangles.LastValue:-1};previous=now;
   if(FrameTimingManager.IsFeatureEnabled()){FrameTimingManager.CaptureFrameTimings();if(FrameTimingManager.GetLatestTimings(1,timings)>0&&timings[0].frameStartTimestamp>lastTiming){lastTiming=timings[0].frameStartTimestamp;s.timingAvailable=true;s.cpuMs=timings[0].cpuFrameTime;s.gpuMs=timings[0].gpuFrameTime;s.gpuAvailable=s.gpuMs>0;}}
   report.samples.Add(s);
  }
  void Save()=>File.WriteAllText(Path.Combine(output,"performance.json"),JsonUtility.ToJson(report,true));
  void Finish(Exception error){if(finished)return;finished=true;collect=false;report.failure=error?.ToString();if(RaceCpuDiagnostics.Enabled)RaceCpuDiagnostics.Stop();batches.Dispose();triangles.Dispose();Save();if(error!=null)UnityEngine.Debug.LogException(error);Application.Quit(error==null?0:1);}
  void OnApplicationQuit(){if(!finished&&output!=null)Finish(new Exception("Interrupted benchmark"));}
 }
}
#endif

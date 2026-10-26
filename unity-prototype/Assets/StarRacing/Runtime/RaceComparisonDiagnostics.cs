using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace StarRacingPrototype {
 // Explicit diagnostic only: ordinary 16-car lifecycle, no physics/AI overrides.
 public sealed class RaceComparisonDiagnostics : MonoBehaviour {
  public const string Argument="--race-comparison16-dir";
  [Serializable] public sealed class Report {
   public string version,theme,config,settings,geometryIdentity,replayIdentity,failure;
   public string limitation="16 total: 1 human seat with no driving input, 15 ordinary AI; camera follows AI seat1. Theme changes track geometry. Scenario-specific comparison, not manual driving or human acceptance.";
   public bool completed,focusedAtStart,basisEnabled;
   public int entrants,colliders,width,height,vSync,frameLimit,antiAliasing;
   public double wall,sim;
  }
  readonly Stopwatch watch=new Stopwatch();
  string output;Report report;RaceDirector director;RaceFrameDiagnostics probe;
  public static string Value(string[] args,string key){int at=Array.IndexOf(args,key);if(at<0||at+1>=args.Length||args[at+1].StartsWith("--"))throw new ArgumentException("Missing "+key);return args[at+1];}
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
  static void Install(){
   var args=Environment.GetCommandLineArgs();if(Array.IndexOf(args,Argument)<0)return;
   if(!SilentStartup.Enabled)throw new InvalidOperationException("Silent startup required");
   // The QA builder uses a dedicated application identity; never write real game preferences.
   if(Application.identifier!="local.starracing.comparison16")throw new InvalidOperationException("Dedicated comparison16 QA identity required");
   string path=Value(args,Argument);if(!Path.IsPathRooted(path)||Directory.Exists(path))throw new IOException("Fresh absolute comparison output required");
   var config=JsonUtility.FromJson<LocalRaceConfig>(File.ReadAllText(Value(args,"--race-comparison-config")));
   string theme=Value(args,"--race-comparison-theme");
   if(theme!="cloud-city"&&theme!="space-station")throw new ArgumentException("Comparison theme required");
   if(config==null||config.entrants!=16||config.humans!=1)throw new ArgumentException("Exact16/1 configuration required");
   config.theme=theme;config.replay=null;config.Save();
   UserSettingsStore.Replace(UserSettings.Parse(File.ReadAllText(Value(args,"--race-comparison-settings"))));
   Directory.CreateDirectory(path);var host=new GameObject("Opt-in comparison16");DontDestroyOnLoad(host);
   var driver=host.AddComponent<RaceComparisonDiagnostics>();driver.output=path;driver.report=new Report{version=Application.version,theme=theme,settings=JsonUtility.ToJson(UserSettingsStore.Current),basisEnabled=BasisNativeExperiment.Enabled};driver.watch.Start();driver.Save();
  }
  IEnumerator Start(){
   var run=Run();while(true){bool more;try{more=run.MoveNext();}catch(Exception e){Fail(e);yield break;}if(!more)break;yield return run.Current;}
  }
  IEnumerator Run(){
   Application.runInBackground=true;
   while(director==null||!director.Ready){director=FindAnyObjectByType<RaceDirector>();yield return null;}
   UnityEngine.Debug.Log("COMPARISON16_FOCUS_WAIT "+report.theme);
   while(!Application.isFocused)yield return null;
   var menu=director.GetComponent<RaceMenu>();menu.StartSelected();while(menu.Loading)yield return null;
   if(!director.Started||director.Cars.Length!=16)throw new InvalidOperationException("16-car start failed "+menu.Error);
   director.SetPaused(true);
   report.entrants=director.Cars.Length;report.config=JsonUtility.ToJson(director.AppliedConfig);
   report.geometryIdentity=director.Course.GeometryIdentity;report.replayIdentity=director.Course.ReplayIdentity(director.Track);
   report.colliders=director.Track.GetComponentsInChildren<Collider>(true).Length;
   var camera=director.CameraFor(0);camera.target=director.Cars[1];camera.Snap();
   while(!Application.isFocused)yield return null;
   ScreenCapture.CaptureScreenshot(Path.Combine(output,"prepared.png"));yield return null;
   report.width=Screen.width;report.height=Screen.height;report.vSync=QualitySettings.vSyncCount;report.frameLimit=Application.targetFrameRate;report.antiAliasing=QualitySettings.antiAliasing;report.focusedAtStart=Application.isFocused;
   if(report.width!=1920||report.height!=1200||!report.focusedAtStart)throw new InvalidOperationException("Expected focused1920x1200 comparison");
   Save();probe=gameObject.AddComponent<RaceFrameDiagnostics>();probe.BeginComparison16(Path.Combine(output,"frames"),director);
   UnityEngine.Debug.Log("COMPARISON16_MEASUREMENT "+report.theme);director.SetPaused(false);
   while(!probe.Completed)yield return null;
  }
  void Update(){if(output!=null&&watch.Elapsed.TotalSeconds>600)Fail(new TimeoutException("Comparison16 wall deadline"));}
  void Fail(Exception e){if(report==null)return;report.failure=e.ToString();Save();UnityEngine.Debug.LogException(e);Application.Quit(1);}
  void Save(){report.wall=watch.Elapsed.TotalSeconds;report.sim=RacePhysicsStepper.SimulatedSeconds;File.WriteAllText(Path.Combine(output,"comparison.json"),JsonUtility.ToJson(report,true));}
  void OnApplicationQuit(){if(report==null)return;report.completed=probe!=null&&probe.Completed&&probe.Failure==null&&report.failure==null;if(probe?.Failure!=null)report.failure=probe.Failure;Save();}
 }
}

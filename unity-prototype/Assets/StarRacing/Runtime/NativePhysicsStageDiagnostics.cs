using System;
using System.Collections.Generic;
using System.IO;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

namespace StarRacingPrototype {
 // Opt-in observation only. Never changes physics settings or profiler global state.
 public sealed class NativePhysicsStageDiagnostics : MonoBehaviour {
  public const string Argument="--native-physics-stages-dir";
  const int FrameBound=8192,MarkerBound=256;
  [Serializable] public struct Sample {public long value,count;}
  [Serializable] public sealed class Marker {public string name,category,unit,flags;public bool valid,wrapped;public Sample[] samples;}
  [Serializable] public sealed class Frame {public int unityFrame;public bool focused,paused;public double real,sim;public long nativeSteps;}
  [Serializable] public sealed class Report {
   public string limitation="Passive all-thread built-in recorder samples. Nested/parallel marker sums are not exclusive CPU cost. Raw per-marker recorder streams have their own completed-frame boundaries; observed LateUpdate frame IDs are NOT exact attribution to those samples. No repeated LastValue samples are summed. Zero/unavailable markers are not zero physics cost.";
   public string failure;public bool completed,editor,development;public int width,height;public Marker[] markers;public List<Frame> frames=new List<Frame>(2048);
  }
  sealed class Recording {public Marker marker;public ProfilerRecorder recorder;}
  readonly List<Recording> recordings=new List<Recording>();Report report;RaceDirector director;string directory;double raceBegin=-1;bool saved,active;
  static bool Selected(ProfilerRecorderDescription d)=>d.Category.Name==ProfilerCategory.Physics.Name || d.Name=="StarRacing.Graph.NativePhysics";
  static List<ProfilerRecorderHandle> Handles(){var handles=new List<ProfilerRecorderHandle>();ProfilerRecorderHandle.GetAvailable(handles);return handles;}
  static Marker Describe(ProfilerRecorderDescription d)=>new Marker{name=d.Name,category=d.Category.Name,unit=d.UnitType.ToString(),flags=d.Flags.ToString()};
  public static Marker[] Inventory(){var result=new List<Marker>();foreach(var h in Handles())if(h.Valid){var d=ProfilerRecorderHandle.GetDescription(h);if(Selected(d))result.Add(Describe(d));}return result.ToArray();}
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Install(){var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,Argument);if(at<0)return;if(at+1>=args.Length)throw new ArgumentException("Native stages output required");new GameObject("Opt-in native physics stages").AddComponent<NativePhysicsStageDiagnostics>().Begin(args[at+1]);}
  public void Begin(string path){if(!SilentStartup.Enabled||!Path.IsPathRooted(path)||directory!=null)throw new InvalidOperationException("Fresh silent absolute native-stage diagnostic required");if(Directory.Exists(path))throw new IOException("Fresh native-stage output required");directory=path;Directory.CreateDirectory(path);report=new Report{editor=Application.isEditor,development=Debug.isDebugBuild,width=Screen.width,height=Screen.height};}
  void StartRecorders(){
   foreach(var h in Handles())if(h.Valid){var d=ProfilerRecorderHandle.GetDescription(h);if(!Selected(d))continue;if(recordings.Count>=MarkerBound)throw new InvalidOperationException("Physics marker bound exceeded");var item=new Recording{marker=Describe(d)};item.recorder=ProfilerRecorder.StartNew(d.Category,d.Name,FrameBound,ProfilerRecorderOptions.Default);item.marker.valid=item.recorder.Valid;recordings.Add(item);}
   report.markers=recordings.ConvertAll(r=>r.marker).ToArray();if(recordings.Count==0)throw new InvalidOperationException("No Physics markers available");
   File.WriteAllText(Path.Combine(directory,"inventory.json"),JsonUtility.ToJson(report,true));active=true;
  }
  void LateUpdate(){if(directory==null||saved)return;try{
   if(director==null)director=FindAnyObjectByType<RaceDirector>();if(director==null||!director.Started||director.Session==null||director.Session.Phase!=RacePhase.Racing)return;
   if(raceBegin<0)raceBegin=RacePhysicsStepper.SimulatedSeconds;if(RacePhysicsStepper.SimulatedSeconds-raceBegin<2)return;if(!active){StartRecorders();return;}
   if(report.frames.Count>=FrameBound)throw new InvalidOperationException("Native-stage frame bound exceeded");
   var frame=new Frame{unityFrame=Time.frameCount,focused=Application.isFocused,paused=director.Paused,real=Time.realtimeSinceStartupAsDouble,sim=RacePhysicsStepper.SimulatedSeconds,nativeSteps=RacePhysicsStepper.NativeSteps};
   report.frames.Add(frame);
  }catch(Exception e){report.failure=e.ToString();Save();}}
  void Save(){if(saved||directory==null)return;saved=true;foreach(var item in recordings){var r=item.recorder;if(r.Valid){r.Stop();item.marker.wrapped=r.WrappedAround;var raw=r.ToArray();item.marker.samples=new Sample[raw.Length];for(int i=0;i<raw.Length;i++)item.marker.samples[i]=new Sample{value=raw[i].Value,count=raw[i].Count};r.Dispose();}}File.WriteAllText(Path.Combine(directory,"stages.json"),JsonUtility.ToJson(report,true));}
  void OnApplicationQuit(){if(report==null)return;var probe=FindAnyObjectByType<RaceFrameDiagnostics>();report.completed=active&&report.frames.Count>0&&probe!=null&&probe.Completed&&string.IsNullOrEmpty(probe.Failure)&&string.IsNullOrEmpty(report.failure);Save();}
  void OnDestroy(){Save();}
 }
}

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using Unity.Profiling;
namespace StarRacingPrototype {
 [InitializeOnLoad] public static class GraphGcProbeChecks {
  const string Key="StarRacing.GraphGCProbe";static int startup;
  public static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_GC_PROBE_DIR");
  static GraphGcProbeChecks(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run(){Directory.CreateDirectory(Output);SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",false);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);startup=0;EditorApplication.update+=Enter;}
  static void Enter(){if(++startup<15)return;EditorApplication.update-=Enter;EditorApplication.isPlaying=true;}
  static void Changed(PlayModeStateChange state){if(!SessionState.GetBool(Key,false))return;if(state==PlayModeStateChange.EnteredPlayMode){Time.captureDeltaTime=.01f;new GameObject("Known GC allocation probe").AddComponent<GraphGcProbeDriver>();}if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",false)?1:0);}}
  public static void Fail(Exception e){Debug.LogException(e);File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());SessionState.SetBool(Key+"Failed",true);EditorApplication.isPlaying=false;}
 }
 public sealed class GraphGcProbeDriver:MonoBehaviour {
  ProfilerRecorder memory,cpu;readonly ProfilerMarker marker=new ProfilerMarker(ProfilerCategory.Scripts,"StarRacing.GCProbe");bool prior,priorBinary,priorDeep,priorStacks,cpuEnabled;ProfilerArea[] areas;bool[] areaStates;ProfilerMemoryRecordMode priorRecord;string priorFile;int tick;byte[] data;StreamWriter writer;readonly IReadOnlyCollection<int> membership=new[]{0};bool membershipProbe;int consumed;
  void Start(){try{membershipProbe=Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_PROBE_MEMBERSHIP")=="yes";cpuEnabled=Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_CPU_PROFILE")=="yes";areas=(ProfilerArea[])Enum.GetValues(typeof(ProfilerArea));areaStates=Array.ConvertAll(areas,Profiler.GetAreaEnabled);priorDeep=ProfilerDriver.deepProfiling;priorStacks=Profiler.enableAllocationCallstacks;priorRecord=ProfilerDriver.memoryRecordMode;File.WriteAllText(Path.Combine(GraphGcProbeChecks.Output,"profiler-modes.txt"),$"deep={priorDeep} allocationStacks={priorStacks} memoryRecord={priorRecord}\n");ProfilerDriver.deepProfiling=false;Profiler.enableAllocationCallstacks=false;ProfilerDriver.memoryRecordMode=ProfilerMemoryRecordMode.None;prior=Profiler.enabled;priorBinary=Profiler.enableBinaryLog;priorFile=Profiler.logFile;Profiler.logFile=Path.Combine(GraphGcProbeChecks.Output,"probe.raw");Profiler.enableBinaryLog=true;Profiler.enabled=true;new GameObject("Profiler frame camera").AddComponent<Camera>();foreach(var area in areas)Profiler.SetAreaEnabled(area,area==ProfilerArea.Memory||area==ProfilerArea.CPU&&cpuEnabled);memory=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame",60);cpu=ProfilerRecorder.StartNew(ProfilerCategory.Scripts,"StarRacing.GCProbe",60);writer=new StreamWriter(Path.Combine(GraphGcProbeChecks.Output,"probe.csv"));writer.WriteLine("tick,profiler,frameBytes,currentBytes,memoryCount,cpuCount,cpuValue,currentCpu,threadDelta,monoUsed");}catch(Exception e){GraphGcProbeChecks.Fail(e);}}
  void Update(){try{tick++;if(tick==15){memory.Reset();memory.Start();cpu.Reset();cpu.Start();}long before=GC.GetAllocatedBytesForCurrentThread();using(marker.Auto()){if(membershipProbe){if(tick%2==0){for(int i=0;i<100000;i++)if(membership.Contains(0))consumed++;}else{var values=(int[])membership;for(int i=0;i<100000;i++)for(int j=0;j<values.Length;j++)if(values[j]==0){consumed++;break;}}}data=new byte[65536];data[0]=1;}long delta=GC.GetAllocatedBytesForCurrentThread()-before;
   writer.WriteLine($"{tick},{Profiler.enabled},{memory.LastValue},{memory.CurrentValue},{memory.Count},{cpu.Count},{cpu.LastValue},{cpu.CurrentValue},{delta},{Profiler.GetMonoUsedSizeLong()}");writer.Flush();
   if(tick==60){bool valid=memory.Valid&&memory.Count>0&&memory.LastValue>=65536&&(!cpuEnabled||cpu.Valid&&cpu.Count>0&&cpu.LastValue>0);File.WriteAllText(Path.Combine(GraphGcProbeChecks.Output,"result.txt"),$"GC_PROBE available={valid} frameBytes={memory.LastValue} samples={memory.Count} cpuProfiling={cpuEnabled} cpuSamples={cpu.Count} threadApiDelta={delta} knownBytes=65536\n");if(!valid)throw new Exception("Known allocation/cpu marker not captured");EditorApplication.isPlaying=false;}
  }catch(Exception e){GraphGcProbeChecks.Fail(e);}}
  void OnDestroy(){writer?.Dispose();memory.Dispose();cpu.Dispose();Profiler.enabled=false;if(areas!=null)for(int i=0;i<areas.Length;i++)Profiler.SetAreaEnabled(areas[i],areaStates[i]);Profiler.enableBinaryLog=priorBinary;Profiler.logFile=priorFile;Profiler.enabled=prior;Profiler.enableAllocationCallstacks=priorStacks;ProfilerDriver.memoryRecordMode=priorRecord;ProfilerDriver.deepProfiling=priorDeep;}
 }
}

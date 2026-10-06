using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using UnityEngine;

namespace StarRacingPrototype {
 // Source PCM and time-stretch state have one owner per generation/shot.
 // SafeHandle marshalling retains the native state through an in-flight callback.
 public sealed class NativeTempoPlayback : IDisposable {
  sealed class Handle : SafeHandleZeroOrMinusOneIsInvalid {
   public Handle(IntPtr value):base(true){SetHandle(value);}
   protected override bool ReleaseHandle(){NativeDestroy(handle);return true;}
  }
  [Serializable,StructLayout(LayoutKind.Sequential)] public struct Stats {
   public long outputFrames,consumedFrames,wraps,starvations,nonFinite,callbacks;
   public double cpuTotalUs,cpuMaxUs,cpuP50Us,cpuP95Us,cpuP99Us;
   public int channels,rate,inputLatency,outputLatency;
  }
  const string Library="starracing_tempo";
  [DllImport(Library,EntryPoint="sr_tempo_file",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr NativeFile([MarshalAs(UnmanagedType.LPUTF8Str)] string path,int frames,int channels,int rate,int startFrame);
  [DllImport(Library,EntryPoint="sr_tempo_memory",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr NativeMemory(float[] pcm,int frames,int channels,int rate);
  [DllImport(Library,EntryPoint="sr_tempo_countdown",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr NativeCountdown(float[] pcm,int frames,int channels,int rate);
  [DllImport(Library,EntryPoint="sr_tempo_render",CallingConvention=CallingConvention.Cdecl)] static extern int NativeRender(Handle handle,[In,Out] float[] data,int frames,int channels,int outputRate,int prefix,int allowedFrames,float rate,float gain);
  [DllImport(Library,EntryPoint="sr_tempo_stats",CallingConvention=CallingConvention.Cdecl)] static extern int NativeStats(Handle handle,out Stats stats);
  [DllImport(Library,EntryPoint="sr_tempo_destroy",CallingConvention=CallingConvention.Cdecl)] static extern void NativeDestroy(IntPtr handle);
  readonly Handle state;readonly int channels,outputRate;readonly long frameLimit;float rate=1,gain;int disposed,armed,entries,finiteEnded;long errors,managedAllocations,rendered;double startsAt,rateChangedAt,maxRateResponseMs;float appliedRate=float.NaN;
  public double MaxRateResponseMs=>Volatile.Read(ref maxRateResponseMs);
  public bool Ended=>Volatile.Read(ref finiteEnded)!=0||frameLimit>0&&Interlocked.Read(ref rendered)>=frameLimit;
  public long RendersBeforeStart {get;private set;}
  public double FirstRenderDsp {get;private set;}=-1;
  public int CallbackChannels {get;private set;}
  static readonly System.Collections.Generic.List<NativeTempoPlayback> retired=new System.Collections.Generic.List<NativeTempoPlayback>();
  static NativeTempoRetirement retirement;static int liveStates;
  public static int LiveStateCount=>Volatile.Read(ref liveStates);
  void EnsureRetirement(){if(retirement==null){var owner=new GameObject("Native tempo retirement");UnityEngine.Object.DontDestroyOnLoad(owner);retirement=owner.AddComponent<NativeTempoRetirement>();}}
  internal static void DrainRetired(){for(int i=retired.Count-1;i>=0;i--)if(Volatile.Read(ref retired[i].entries)==0){retired[i].state.Dispose();Interlocked.Decrement(ref liveStates);retired.RemoveAt(i);}}
  public AudioClip Clip {get;private set;}
  public long CallbackErrors=>Interlocked.Read(ref errors);
  public long ManagedCallbackAllocatedBytes=>Interlocked.Read(ref managedAllocations);
  public float Rate {get=>Volatile.Read(ref rate);set{if(value==Volatile.Read(ref rate))return;Volatile.Write(ref rateChangedAt,AudioSettings.dspTime);Volatile.Write(ref rate,value);}}
  public float Gain {get=>Volatile.Read(ref gain);set=>Volatile.Write(ref gain,value);}
  public void Arm(double dspStart=0){startsAt=dspStart;Volatile.Write(ref armed,1);}
  public void Bind(AudioSource source){var filter=source.GetComponent<NativeTempoFilter>()??source.gameObject.AddComponent<NativeTempoFilter>();filter.Bind(this);source.clip=Clip;source.loop=true;}
  public NativeTempoPlayback(string path,int frames,int channels,int sampleRate,int startFrame=0){
   this.channels=channels;outputRate=AudioSettings.outputSampleRate;EnsureRetirement();state=new Handle(NativeFile(path,frames,channels,sampleRate,startFrame));RequireState();Interlocked.Increment(ref liveStates);
   Clip=AudioClip.Create("Tempo music carrier",sampleRate,channels,sampleRate,false);
  }
  public NativeTempoPlayback(float[] pcm,int frames,int channels,int sampleRate,float speed,bool finiteCountdown=false){
   this.channels=channels;outputRate=AudioSettings.outputSampleRate;EnsureRetirement();rate=speed;frameLimit=(long)Math.Ceiling((frames/(double)speed+2048)*outputRate/sampleRate);state=new Handle(finiteCountdown?NativeCountdown(pcm,frames,channels,sampleRate):NativeMemory(pcm,frames,channels,sampleRate));RequireState();Interlocked.Increment(ref liveStates);
   // The bound is a safety ceiling. Countdown EOF comes from finite source WSOLA;
   // only calibration retains Signalsmith zero drain. Assets/events are unchanged.
   
   Clip=AudioClip.Create("Tempo countdown carrier",sampleRate,channels,sampleRate,false);
  }
  void RequireState(){if(state.IsInvalid){state.Dispose();throw new InvalidOperationException("AUDIO_TEMPO native create failed");}}
  internal void Render(float[] data,int callbackChannels){
   Interlocked.Increment(ref entries);
   try{
    if(Volatile.Read(ref disposed)!=0||Volatile.Read(ref armed)==0||Ended){Array.Clear(data,0,data.Length);return;}
    double now=AudioSettings.dspTime;int frames=data.Length/callbackChannels;
    if(now+frames/(double)outputRate<=startsAt){Array.Clear(data,0,data.Length);return;}
    int prefix=(int)Math.Max(0,Math.Ceiling((startsAt-now)*outputRate));
    int allowed=frames-prefix;if(frameLimit>0)allowed=(int)Math.Min(allowed,frameLimit-Interlocked.Read(ref rendered));
    // Native output API handles scheduled prefix and finite drain without a PCM queue.
    long before=GC.GetAllocatedBytesForCurrentThread();
    CallbackChannels=callbackChannels;if(FirstRenderDsp<0)FirstRenderDsp=now+prefix/(double)outputRate;
    float currentRate=Volatile.Read(ref rate);if(currentRate!=appliedRate){if(!float.IsNaN(appliedRate))Volatile.Write(ref maxRateResponseMs,Math.Max(Volatile.Read(ref maxRateResponseMs),(now+prefix/(double)outputRate-Volatile.Read(ref rateChangedAt))*1000));appliedRate=currentRate;}
    try{int result=NativeRender(state,data,frames,callbackChannels,outputRate,prefix,allowed,currentRate,Volatile.Read(ref gain));if(result<0){Array.Clear(data,0,data.Length);Interlocked.Increment(ref errors);}else if(result==1)Volatile.Write(ref finiteEnded,1);}
    catch(ObjectDisposedException){Array.Clear(data,0,data.Length);}
    Interlocked.Add(ref rendered,allowed);
    Interlocked.Add(ref managedAllocations,GC.GetAllocatedBytesForCurrentThread()-before);
   }finally{Interlocked.Decrement(ref entries);}
  }
  public Stats Snapshot(){if(NativeStats(state,out var result)!=0)throw new InvalidOperationException("AUDIO_TEMPO stats failed");return result;}
  public void Dispose(){if(Interlocked.Exchange(ref disposed,1)!=0)return;if(Volatile.Read(ref entries)==0){state.Dispose();Interlocked.Decrement(ref liveStates);}else retired.Add(this);if(Clip!=null)UnityEngine.Object.Destroy(Clip);Clip=null;}
 }
}

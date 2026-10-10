using System;
namespace StarRacingPrototype {
 public struct EngineMixVoice {
  public float Speed,Throttle,Boost,Presence;
  public bool Local,Active;
  public EngineMixVoice(float speed,float throttle,float boost,bool local,float presence=1,bool active=true){Speed=speed;Throttle=throttle;Boost=boost;Local=local;Presence=presence;Active=active;}
 }
 // Control-thread only; no scene API, PCM phase changes or hot-path allocation.
 public static class EngineMixPolicy {
  public const float LocalBudget=.32f,RivalBudget=.02f,TotalBudget=LocalBudget+RivalBudget;
  static float Raw(EngineMixVoice v)=>v.Active?Math.Min(.34f,RaceAudioPolicy.EngineGain(v.Speed,v.Throttle,v.Boost>.001f)*RaceAudioPolicy.Clamp(v.Presence)):0;
  static float Frequency(EngineMixVoice v)=>RaceAudioPolicy.EngineFrequency(v.Speed,v.Throttle);
  public static void Targets(EngineMixVoice[] voices,float[] gains){
   if(voices.Length!=gains.Length)throw new ArgumentException("Engine mix buffers differ");
   for(int i=0;i<voices.Length;i++){
    var v=voices[i];float raw=Raw(v),weight=1;
    if(v.Local&&raw>0)for(int j=0;j<voices.Length;j++){
     var other=voices[j];
     // Stable seat order prevents tiny throttle differences from swapping leaders.
     if(j>=i||!other.Local||Raw(other)==0)continue;
     // Blend out redundant copies; distinct gas/boost remains a separate voice.
     float difference=Math.Max(Math.Abs(Frequency(v)-Frequency(other))/6,
      Math.Max(Math.Abs(v.Throttle-other.Throttle)/.25f,Math.Abs(v.Boost-other.Boost)/.25f));
     float t=RaceAudioPolicy.Clamp(difference);weight=Math.Min(weight,t*t*(3-2*t));
    }
    gains[i]=raw*weight;
   }
   Limit(voices,gains);
  }
  // Also bound smoothed gains during a handoff between redundant voices.
  public static void Limit(EngineMixVoice[] voices,float[] gains){
   float local=0,rivals=0;
   for(int i=0;i<gains.Length;i++){if(voices[i].Local)local+=gains[i];else rivals+=gains[i];}
   float localScale=local>LocalBudget?LocalBudget/local:1,rivalScale=rivals>RivalBudget?RivalBudget/rivals:1;
   for(int i=0;i<gains.Length;i++)gains[i]*=voices[i].Local?localScale:rivalScale;
  }
 }
}

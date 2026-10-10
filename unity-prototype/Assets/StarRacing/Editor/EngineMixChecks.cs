using System;
namespace StarRacingPrototype {
 public static class EngineMixChecks {
  static int assertions;
  static void Require(bool value,string message){assertions++;if(!value)throw new Exception("ENGINE_MIX: "+message);}
  static EngineMixVoice[] Voices(int humans,int rivals,bool one=false,bool different=false,bool boost=false){
   var v=new EngineMixVoice[humans+rivals];
   for(int i=0;i<v.Length;i++)v[i]=new EngineMixVoice(different?12+i*9:30,one&&i>0?0:1,boost?1:0,i<humans,i<humans?1:.42f);
   return v;
  }
  sealed class Stats {public double Rms,Min=double.PositiveInfinity,Max,Peak;}
  static Stats Measure(int rate,EngineMixVoice[] voices,int variant){
   const int seconds=30;int block=rate/10;var gain=new float[voices.Length];var data=new float[voices.Length][];var dsp=new EngineAudioDsp[voices.Length];
   EngineMixPolicy.Targets(voices,gain);
   int humans=0;foreach(var v in voices)if(v.Local)humans++;
   if(variant!=2){
    float scale=variant==0?1f/(humans+(voices.Length-humans)*.42f):(float)(1/Math.Sqrt(humans+(voices.Length-humans)*.42*.42));
    for(int i=0;i<gain.Length;i++)gain[i]=RaceAudioPolicy.EngineGain(voices[i].Speed,voices[i].Throttle,voices[i].Boost>.001f)*voices[i].Presence*scale;
   }
   for(int i=0;i<voices.Length;i++){var v=voices[i];dsp[i]=new EngineAudioDsp(rate,i);data[i]=new float[block];dsp[i].SetPresentation(v.Speed,v.Throttle,v.Boost,gain[i]);}
   var result=new Stats();double total=0;
   for(int b=0;b<seconds*10;b++){
    for(int i=0;i<dsp.Length;i++)dsp[i].Render(data[i]);
    double energy=0;for(int n=0;n<block;n++){double sum=0;for(int i=0;i<data.Length;i++)sum+=data[i][n];RequireFinite(sum);energy+=sum*sum;result.Peak=Math.Max(result.Peak,Math.Abs(sum));}
    if(b<10)continue;double rms=Math.Sqrt(energy/block);total+=energy;result.Min=Math.Min(result.Min,rms);result.Max=Math.Max(result.Max,rms);
   }
   result.Rms=Math.Sqrt(total/((seconds*10-10)*block));return result;
  }
  static void RequireFinite(double value){if(double.IsNaN(value)||double.IsInfinity(value))throw new Exception("Nonfinite mix PCM");}
  static double Db(double value)=>20*Math.Log10(value);
  static void Log(string value){
#if UNITY_EDITOR
   UnityEngine.Debug.Log(value);
#else
   Console.WriteLine(value);
#endif
  }
  public static void Run(){
   assertions=0;
   foreach(int humans in new[]{1,2,3,4}){
    var v=Voices(humans,0,true);var local=new float[v.Length];EngineMixPolicy.Targets(v,local);
    var bots=Voices(humans,8-humans,true);var full=new float[bots.Length];EngineMixPolicy.Targets(bots,full);
    Require(local[0]==full[0],"bots attenuate human "+humans);
    Require(local[0]>=RaceAudioPolicy.EngineGain(30,1)*.8f,"one gas divided by idle seats "+humans);
    float sum=0;foreach(float g in full)sum+=g;Require(sum<=EngineMixPolicy.TotalBudget+.000001f,"target headroom "+humans);
    var idle=Voices(humans,8-humans);for(int i=0;i<idle.Length;i++){idle[i].Speed=0;idle[i].Throttle=0;}
    EngineMixPolicy.Targets(idle,full);Require(full[0]>RaceAudioPolicy.EngineVoiceThreshold,"full roster idle human lost "+humans);
   }
   var distinct=Voices(4,4,false,true,true);var targets=new float[8];EngineMixPolicy.Targets(distinct,targets);
   for(int i=0;i<4;i++)Require(targets[i]>0,"different human action lost "+i);
   for(int i=0;i<8;i++)targets[i]=1;EngineMixPolicy.Limit(distinct,targets);
   float bounded=0;foreach(float g in targets)bounded+=g;Require(bounded<=EngineMixPolicy.TotalBudget+.000001f,"handoff headroom");
   var falling=Voices(4,4);for(int i=0;i<falling.Length;i++)falling[i].Active=false;
   EngineMixPolicy.Targets(falling,targets);foreach(float g in targets)Require(g==0,"inactive/falling voice survives");
   EngineMixPolicy.Targets(distinct,targets);long before=GC.GetAllocatedBytesForCurrentThread();for(int n=0;n<100;n++)EngineMixPolicy.Targets(distinct,targets);
   Require(GC.GetAllocatedBytesForCurrentThread()==before,"policy hot path allocates");
   // Continuous similarity weights must not introduce a boundary step.
   var boundary=Voices(2,0);boundary[1].Throttle=.999f;var pair=new float[2];EngineMixPolicy.Targets(boundary,pair);float previous=pair[1];
   for(int n=1;n<=250;n++){boundary[1].Throttle=.999f-n*.001f;EngineMixPolicy.Targets(boundary,pair);Require(Math.Abs(pair[1]-previous)<.005f,"similarity boundary jumps");previous=pair[1];}
   var jitter=Voices(2,0);jitter[0].Throttle=.7f;
   for(int n=0;n<=20;n++){jitter[1].Throttle=.69f+n*.001f;EngineMixPolicy.Targets(jitter,pair);Require(pair[0]>.09f&&pair[1]<.005f,"near-equal throttle swaps stable leader");}
   foreach(int rate in new[]{44100,48000}){
    var solo=Measure(rate,Voices(1,0),2);
    foreach(int humans in new[]{2,3,4}){
     var equal=Measure(rate,Voices(humans,8-humans),2);
     Require(Math.Abs(Db(equal.Min/solo.Min))<1,"equal-rpm local engines have sustained dip "+rate+"/"+humans);
     Require(equal.Peak<.34,"equal-rpm PCM headroom");
    }
    foreach(bool boost in new[]{false,true})foreach(bool one in new[]{false,true}){
     var different=Measure(rate,Voices(4,4,one,true,boost),2);Require(different.Peak<.34,"distinct engines PCM headroom "+rate+"/"+boost+"/"+one);
    }
   }
   foreach(int variant in new[]{0,1,2})foreach(int humans in new[]{1,2,4})foreach(bool one in new[]{false,true}){
    var stats=Measure(48000,Voices(humans,8-humans,one),variant);
    Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,"ENGINE_MIX_COMPARISON variant={0} humans={1} oneGas={2} rmsDb={3:F2} min100msDb={4:F2} max100msDb={5:F2} peak={6:F4}",variant,humans,one,Db(stats.Rms),Db(stats.Min),Db(stats.Max),stats.Peak));
   }
   Log("ENGINE_MIX_CHECKS_OK assertions="+assertions+" rates=44100,48000 originalPCM budgets coalescing distinctGas nitro allocationFree");
  }
 }
}

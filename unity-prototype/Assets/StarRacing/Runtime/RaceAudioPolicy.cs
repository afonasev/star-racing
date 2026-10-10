using System;

namespace StarRacingPrototype {
 public enum RaceSound { Countdown, Go, VehicleImpact, BarrierImpact, Fall, Respawn, Finish, Results }
 public readonly struct RaceSoundPolicy {
  public readonly string Clip; public readonly float Gain, Cooldown; public readonly int Priority;
  public RaceSoundPolicy(string clip,float gain,float cooldown,int priority){Clip=clip;Gain=gain;Cooldown=cooldown;Priority=priority;}
 }
 // Native racing mix; speed is metres/second, load and contact strength are normalized.
 public static class RaceAudioPolicy {
  public const int MaxEngines=8, MaxOneShots=4;
  public const float EngineVoiceThreshold=.0005f;
  public const float AudibleRivalDistance=480, MusicGain=.13f, CountdownDuck=.22f;
  public static readonly string[] Music={"city-loop","technological-messup","zenostar","revelation","electronic-loop"};
  public static float Clamp(float value,float lo=0,float hi=1)=>Math.Max(lo,Math.Min(hi,value));
  public static float EngineFrequency(float speed,float throttle){
   // Soft speed saturation avoids a flat pitch ceiling during normal racing.
   float velocity=Math.Max(0,speed),rpmSpeed=velocity/(1+velocity/180);
   return 44+rpmSpeed*.65f+Clamp(throttle)*(18+rpmSpeed*2.1f);
  }
  public static float EngineGain(float speed,float throttle,bool nitro=false){float normalized=Clamp(speed/75);return (.035f+normalized*.18f+normalized*normalized*.07f)*(.3f+Clamp(throttle)*.7f)*(nitro?1.12f:1);}
  public static float SkidEngineDuck(float intensity)=>1-Clamp(intensity)*.3f;
  public static float Smooth(float current,float target,float delta,float attack,float release)=>current+(target-current)*(1-(float)Math.Exp(-Math.Max(0,delta)/(target>current?attack:release)));
  public static float ImpactGain(float strength)=>.22f+.5f*(float)Math.Sqrt(Clamp(strength));
  public static float ImpactCooldown(float strength)=>.32f-Clamp(strength)*.16f;
  public static bool ImpactReady(double now,double last,float strength)=>now-last>=Math.Max(.12f,ImpactCooldown(strength));
  public static float RivalGain(float distance)=>.2f+.8f/(1+Math.Max(0,distance)/160);
  public static float SkidGain(float intensity){intensity=Clamp(intensity);return .32f*(float)Math.Sqrt(intensity);}
  public static float MusicRate(float progress)=>1+Clamp(progress)*.12f;
  public static float CountdownRate(int beat)=>beat==3?.9f:beat==1?1.12f:1;
  public static int MusicIndex(int index)=>((index%Music.Length)+Music.Length)%Music.Length;
  public static RaceSoundPolicy Policy(RaceSound sound){
   switch(sound){
    case RaceSound.Countdown:return new RaceSoundPolicy("countdown-starter",.95f,.42f,1);
    case RaceSound.Go:return new RaceSoundPolicy("engine-high",.45f,.42f,2);
    case RaceSound.VehicleImpact:return new RaceSoundPolicy("vehicle-impact",.72f,.24f,0);
    case RaceSound.BarrierImpact:return new RaceSoundPolicy("barrier-impact",.72f,.24f,0);
    case RaceSound.Fall:return new RaceSoundPolicy("fall-thud",.7f,.3f,2);
    case RaceSound.Respawn:return new RaceSoundPolicy("respawn-road",.68f,.3f,2);
    case RaceSound.Finish:return new RaceSoundPolicy("finish-surge",.82f,.5f,3);
    default:return new RaceSoundPolicy("results-settle",.78f,.6f,3);
   }
  }
 }
}

using System;

namespace StarRacingPrototype {
 public enum RaceSound { Countdown, Go, VehicleImpact, BarrierImpact, Fall, Respawn, Finish, Results }
 public readonly struct RaceSoundPolicy {
  public readonly string Clip; public readonly float Gain, Cooldown; public readonly int Priority;
  public RaceSoundPolicy(string clip,float gain,float cooldown,int priority){Clip=clip;Gain=gain;Cooldown=cooldown;Priority=priority;}
 }
 // Ported from browser soundEngine.ts at 0690da4; speed is metres/second.
 public static class RaceAudioPolicy {
  public const int MaxEngines=8, MaxOneShots=4;
  public const float AudibleRivalDistance=480, MusicGain=.26f, CountdownDuck=.22f;
  public static readonly string[] Music={"city-loop","technological-messup","zenostar","revelation","electronic-loop"};
  public static float Clamp(float value,float lo=0,float hi=1)=>Math.Max(lo,Math.Min(hi,value));
  public static float EngineFrequency(float speed,float throttle)=>46+Math.Min(104,Math.Max(0,speed)*.4f+Math.Max(0,speed)*1.8f*Clamp(throttle));
  public static float EngineGain(float speed,float throttle,bool nitro=false){float normalized=Math.Min(1,Math.Max(0,speed)/60);return (.03f+Math.Max(0,speed)*.004f+normalized*normalized*.07f)*(.16f+Clamp(throttle)*.84f)*(nitro?1.42f:1);}
  public static float RivalGain(float distance)=>.2f+.8f/(1+Math.Max(0,distance)/160);
  public static float SkidGain(float intensity){intensity=Clamp(intensity);return intensity>0?.04f+intensity*.14f:0;}
  public static float MusicRate(float progress)=>1+Clamp(progress)*.12f;
  public static float CountdownRate(int beat)=>beat==3?.9f:beat==1?1.12f:1;
  public static int MusicIndex(int index)=>((index%Music.Length)+Music.Length)%Music.Length;
  public static RaceSoundPolicy Policy(RaceSound sound){
   switch(sound){
    case RaceSound.Countdown:return new RaceSoundPolicy("countdown-starter",.95f,.42f,1);
    case RaceSound.Go:return new RaceSoundPolicy("engine-high",.45f,.42f,2);
    case RaceSound.VehicleImpact:return new RaceSoundPolicy("vehicle-impact",.95f,.17f,0);
    case RaceSound.BarrierImpact:return new RaceSoundPolicy("barrier-impact",.95f,.17f,0);
    case RaceSound.Fall:return new RaceSoundPolicy("fall-thud",.7f,.3f,2);
    case RaceSound.Respawn:return new RaceSoundPolicy("respawn-road",.68f,.3f,2);
    case RaceSound.Finish:return new RaceSoundPolicy("finish-surge",.82f,.5f,3);
    default:return new RaceSoundPolicy("results-settle",.78f,.6f,3);
   }
  }
 }
}

using System;

namespace StarRacingPrototype {
 // Pure longitudinal capability/resource model. No pose, Rigidbody or driver type.
 public sealed class DrivingBalance {
  public readonly ReleaseBalance Snapshot;
  public float Charge {get;private set;}
  public bool Ready {get;private set;}=true;
  public bool Active {get;private set;}
  float accelerationMultiplier=1,recoveryMultiplier=1;
  public void ConfigureHandicap(DriverProfile profile,bool enabled) {
   accelerationMultiplier=enabled && profile==DriverProfile.Racer?Snapshot["racerAccelerationMultiplier"]:enabled && profile==DriverProfile.Ace?Snapshot["aceAccelerationMultiplier"]:1;
   recoveryMultiplier=enabled && profile==DriverProfile.Racer?Snapshot["racerNitroRecoveryMultiplier"]:enabled && profile==DriverProfile.Ace?Snapshot["aceNitroRecoveryMultiplier"]:1;
  }
  public float Capacity => Snapshot["nitroCapacity"];
  public float BaseSpeed => Snapshot["baseSpeedKmh"]/3.6f;
  public float NitroSpeed => Snapshot["nitroMaxSpeedKmh"]/3.6f;
  public DrivingBalance(ReleaseBalance snapshot){Snapshot=snapshot??throw new ArgumentNullException(nameof(snapshot));Reset();}
  public void Reset(){Charge=Capacity;Ready=true;Active=false;}
  public void StopBoost(){Active=false;}
  public float Step(DrivingInput input,float speed,float dt,bool raceActive,bool boostAllowed,int place,int count) {
   Active=false;
   if(!raceActive||dt<=0)return 0;
   if(!Ready && Charge>=Capacity*.1f-0.00001f)Ready=true;
   Active=input.nitro && Ready && Charge>0 && speed>0 && boostAllowed;
   if(Active){Charge=Math.Max(0,Charge-Snapshot["nitroDrainPerSecond"]*dt);if(Charge==0)Ready=false;}
   else {
    float behind=count<=1?0:Clamp((place-1f)/(count-1f),0,1);
    float recovery=Snapshot["nitroRecoveryPerSecond"]*recoveryMultiplier*(1+behind*(Snapshot["nitroRecoveryBehindMultiplier"]-1));
    Charge=Math.Min(Capacity,Charge+recovery*dt);
   }
   float gas=Clamp(input.throttle,0,1),brake=Clamp(input.brake,0,1);
   float drive=gas*Snapshot["acceleration"]*accelerationMultiplier+(Active?Snapshot["nitroAcceleration"]:0);
   float limit=Active?NitroSpeed:BaseSpeed;
   drive=Math.Min(drive,Math.Max(0,(limit-speed)/dt));
   if(brake>0) {
    // Brake owns longitudinal deceleration; throttle cannot cancel it or cross zero in one step.
    drive=speed>.08f ? -Math.Min(Snapshot["brakeDeceleration"]*brake,speed/dt)
                    : -Math.Min(11f*brake,Math.Max(0,(12f+speed)/dt));
   } else if(!Active && speed>BaseSpeed) drive-=speed*.11f;
   return drive;
  }
  // Native brake precedence and resource latch remain canonical. Source slip factors
  // modify this single longitudinal owner; callers must not apply Step again.
  public float StepHuman(DrivingInput input,float speed,float dt,bool raceActive,bool boostAllowed,int place,int count,HumanHandlingState handling) {
   var efficient=input;efficient.throttle*= (float)HumanHandling.ThrottleEfficiency(handling,Snapshot);
   float acceleration=Step(efficient,speed,dt,raceActive,boostAllowed,place,count);
   if(!raceActive || dt<=0)return 0;
   float next=(speed+acceleration*dt)*(float)HumanHandling.SlipDecay(handling,Snapshot,dt);
   return (next-speed)/dt;
  }
  static float Clamp(float x,float lo,float hi)=>Math.Max(lo,Math.Min(hi,x));
 }
}

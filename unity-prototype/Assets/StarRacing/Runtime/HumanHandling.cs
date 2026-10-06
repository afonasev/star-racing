using System;

namespace StarRacingPrototype
{
    // Source controller.ts policy. No Unity, pose, rendering or resource ownership.
    [Serializable]
    public sealed class HumanHandlingState
    {
        public double steeringInput, yawRate, gripBlend, driftIntentRemaining;
        public double heading, motionYaw, slipAngle, lateralVelocity, speed, slipIntensity;
        public bool driftWasPressed, driftActive, skidActive;
        public void Reset() { steeringInput=yawRate=gripBlend=driftIntentRemaining=0; heading=motionYaw=slipAngle=lateralVelocity=speed=slipIntensity=0; driftWasPressed=driftActive=skidActive=false; }
    }
    public readonly struct HandlingObservation
    {
        public readonly double Speed, Heading, MotionYaw, YawRate, LateralVelocity;
        public HandlingObservation(double speed,double heading,double motionYaw,double yawRate,double lateral)
        { Speed=speed; Heading=heading; MotionYaw=motionYaw; YawRate=yawRate; LateralVelocity=lateral; }
    }
    [Flags] public enum DrivingSuppression { None=0, Paused=1, Falling=2, Jumping=4, Ghost=8, Finished=16, Inactive=32 }
    public readonly struct DrivingIntentSnapshot
    {
        public readonly int EntrantId, Generation, PositionRevision;
        public readonly long FixedTick;
        public readonly bool SkidActive, DriftActive;
        public readonly float SlipIntensity, IntentRemaining;
        public readonly DrivingSuppression Suppression;
        public DrivingIntentSnapshot(int id,int generation,int revision,long tick,HumanHandlingState state,DrivingSuppression suppression)
        { EntrantId=id;Generation=generation;PositionRevision=revision;FixedTick=tick;Suppression=suppression;
          SkidActive=suppression==DrivingSuppression.None && state.skidActive;DriftActive=state.driftActive;
          SlipIntensity=SkidActive?(float)state.slipIntensity:0;IntentRemaining=(float)state.driftIntentRemaining; }
    }
    public readonly struct HandlingStepTargets
    {
        public readonly double RawLateralVelocity, BeforeStabilizationMotionYaw, BeforeStabilizationLateralVelocity;
        public readonly bool NeutralNitroFinalTarget;
        public HandlingStepTargets(double raw,double motion,double lateral,bool neutral)
        { RawLateralVelocity=raw;BeforeStabilizationMotionYaw=motion;BeforeStabilizationLateralVelocity=lateral;NeutralNitroFinalTarget=neutral; }
    }
    public static class HumanHandling
    {
        public const double MaxSlip=35*Math.PI/180;
        public const double EnterSlip=8*Math.PI/180, ExitSlip=5*Math.PI/180;
        public const double MinSkidSpeed=75/3.6, MinLateral=1.5;
        public static double Clamp(double x,double lo,double hi)=>Math.Max(lo,Math.Min(hi,x));
        public static double Wrap(double a)=>Math.Atan2(Math.Sin(a),Math.Cos(a));
        static double Approach(double a,double b,double amount)=>Wrap(a+Wrap(b-a)*Clamp(amount,0,1));
        public static double ThrottleEfficiency(HumanHandlingState s,ReleaseBalance b)
        { var slip=Clamp(Math.Abs(s.slipAngle)/MaxSlip,0,1);return 1-slip*b["throttleDriftInfluence"]*s.gripBlend*.28; }
        public static double SlipDecay(HumanHandlingState s,ReleaseBalance b,double dt)
        {var slip=Clamp(Math.Abs(s.slipAngle)/MaxSlip,0,1);return Math.Exp(-b["slipDrag"]*slip*slip*dt);}
        public static HandlingStepTargets Step(HumanHandlingState s,DrivingInput input,HandlingObservation observed,ReleaseBalance b,double dt,bool nitro,bool longStraight)
        {
            if(dt<=0)return default;
            double steer=Clamp(input.steer,-1,1),brake=Clamp(input.brake,0,1),throttle=Clamp(input.throttle,0,1);
            // Kinetic inputs are native observations; yawRate is reconciled source memory.
            s.speed=observed.Speed;s.heading=observed.Heading;s.motionYaw=observed.MotionYaw;
            s.slipAngle=Wrap(s.heading-s.motionYaw);
            double ratio=Math.Min(1,Math.Abs(s.speed)/(b["baseSpeedKmh"]/3.6)),direction=s.speed>=0?1:-.65;
            s.steeringInput+=(steer-s.steeringInput)*(1-Math.Exp(-dt/b["steeringResponseSeconds"]));
            double retention=1-(1-b["highSpeedSteeringRetention"])*Clamp((ratio-.45)/.55,0,1);
            bool requested=input.drift && Math.Abs(s.steeringInput)>.12 && ratio>.25;
            bool brakeTurn=brake>0 && Math.Abs(s.steeringInput)>.12 && ratio>.25;
            if(input.drift && !s.driftWasPressed && requested)s.yawRate+=Math.Sign(s.steeringInput)*b["driftEntryYawImpulse"]*direction;
            s.driftWasPressed=input.drift;
            s.driftIntentRemaining=requested?Math.Max(s.driftIntentRemaining,.24):Math.Max(0,s.driftIntentRemaining-dt);
            s.gripBlend+=((requested?1:0)-s.gripBlend)*(1-Math.Exp(-dt/(requested?.16:b["gripRecoverySeconds"])));
            double baseYaw=(1.18+ratio*1.62)*s.steeringInput*retention*direction;
            double brakeYaw=brakeTurn?s.steeringInput*brake*b["brakeYawInfluence"]*ratio*direction:0;
            bool counter=Math.Abs(s.steeringInput)>.12 && Math.Sign(s.slipAngle)!=0 && Math.Sign(s.steeringInput)!=Math.Sign(s.slipAngle);
            double counterYaw=counter?s.steeringInput*b["counterSteerStrength"]*ratio:0;
            double target=baseYaw*(1+.35*s.gripBlend)+brakeYaw+counterYaw;
            s.yawRate+=(target-s.yawRate)*(1-Math.Exp(-dt/b["yawResponseSeconds"]));
            double delta=s.yawRate*dt;
            s.heading=Wrap(s.heading+delta);s.motionYaw=Wrap(s.motionYaw+delta*(.8+(.65-.8)*s.gripBlend));
            double loosen=throttle*b["throttleDriftInfluence"]*s.gripBlend*Clamp(Math.Abs(Wrap(s.heading-s.motionYaw))/MaxSlip,0,1);
            double grip=Math.Max(.05,b["regularGrip"]+(b["driftGrip"]-b["regularGrip"])*s.gripBlend-loosen);
            bool stabilizing=nitro && brake==0 && Math.Abs(steer)<=.12;
            s.motionYaw=Approach(s.motionYaw,stabilizing?0:s.heading,1-Math.Exp(-grip*dt));
            if(Math.Abs(steer)<=.02 && brake==0 && !input.drift && longStraight)
            {double assist=1-Math.Exp(-1.8*dt);s.heading=Approach(s.heading,0,assist);s.motionYaw=Approach(s.motionYaw,0,assist);s.yawRate*=Math.Exp(-4*dt);}
            double rawLateral=s.speed*Math.Sin(s.motionYaw);
            s.lateralVelocity=Clamp(rawLateral,-18,18);
            var targets=new HandlingStepTargets(rawLateral,s.motionYaw,s.lateralVelocity,stabilizing);
            // Source's neutral-nitro sweep stabilization, expressed as a requested increment only.
            if(stabilizing){s.motionYaw=0;s.lateralVelocity=0;}
            s.slipAngle=Wrap(s.heading-s.motionYaw);s.driftActive=requested || s.driftIntentRemaining>0;
            return targets;
        }
        public static void ObserveSkid(HumanHandlingState s,HandlingObservation observed,DrivingSuppression suppression)
        {
            if((suppression & DrivingSuppression.Paused)!=0)return; // pause freezes policy, including timers/latch.
            if(suppression!=DrivingSuppression.None){s.skidActive=false;s.slipIntensity=0;return;}
            s.slipAngle=Wrap(observed.Heading-observed.MotionYaw);
            double slip=Math.Abs(s.slipAngle),speed=Math.Abs(observed.Speed),lateral=Math.Abs(observed.LateralVelocity);
            if(s.skidActive){if(slip<=ExitSlip || speed<MinSkidSpeed || lateral<MinLateral)s.skidActive=false;}
            else if(slip>=EnterSlip && speed>=MinSkidSpeed && lateral>=MinLateral && (s.driftActive || s.driftIntentRemaining>0))s.skidActive=true;
            double angle=Clamp((slip-ExitSlip)/(MaxSlip-ExitSlip),0,1),speedIntensity=Clamp((speed-MinSkidSpeed)/(45/3.6),0,1);
            s.slipIntensity=s.skidActive?angle*(.55+speedIntensity*.45):0;
        }
    }
}

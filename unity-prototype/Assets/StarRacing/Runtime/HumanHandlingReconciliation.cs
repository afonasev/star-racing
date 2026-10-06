using UnityEngine;

namespace StarRacingPrototype
{
    // Only controller memory and an own-command reference. Never a virtual pose or speed.
    public sealed class HumanHandlingReconciliation
    {
        bool valid;
        long interval;
        int generation, revision;
        Vector3 previousNormal, angularReference;
        double finalYaw;
        public bool UsedResidual { get; private set; }
        public double YawResidual { get; private set; }
        public void Invalidate(string reason="explicit") { valid=false; UsedResidual=false; YawResidual=0; }

        public void Observe(HumanHandlingState state, HandlingObservation observed,
            Vector3 angularVelocity, TrackFrame frame, long currentInterval,
            int currentGeneration, int currentRevision, bool continuous, bool contacted)
        {
            Vector3 normal=frame.normal.normalized;
            UsedResidual=valid && continuous && !contacted && currentInterval==interval+1
                && generation==currentGeneration && revision==currentRevision;
            YawResidual=UsedResidual?-Vector3.Dot(angularVelocity-angularReference,normal):0;
            state.yawRate=UsedResidual
                ? -Vector3.Dot(-previousNormal*(float)finalYaw,normal)+YawResidual
                : observed.YawRate;
            state.speed=observed.Speed; state.heading=observed.Heading;
            state.motionYaw=observed.MotionYaw; state.lateralVelocity=observed.LateralVelocity;
            state.slipAngle=HumanHandling.Wrap(state.heading-state.motionYaw);
            // Consume once; another observation cannot apply the same residual again.
            valid=false;
        }

        public void Record(HumanHandlingState state, Vector3 beforeAngularVelocity,
            Vector3 handlingAngularAcceleration, TrackFrame frame, float dt,
            long currentInterval, int currentGeneration, int currentRevision)
        {
            finalYaw=state.yawRate; previousNormal=frame.normal.normalized;
            angularReference=beforeAngularVelocity+handlingAngularAcceleration*dt;
            interval=currentInterval; generation=currentGeneration; revision=currentRevision;
            valid=true;
        }
    }
}

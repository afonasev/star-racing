using UnityEngine;

namespace StarRacingPrototype
{
    // Explicit browser-left/native-right boundary. All state is measured, never a virtual pose.
    public static class HumanHandlingForces
    {
        // Public/native positive steer is right; the source controller's positive yaw is left.
        public static DrivingInput NormalizeInput(DrivingInput input)
        { input.steer=-input.steer; return input; }
        public static HandlingObservation Observe(Vector3 velocity,Vector3 forward,Vector3 angularVelocity,TrackFrame frame)
        {
            Vector3 normal=frame.normal.normalized,tangent=Vector3.ProjectOnPlane(frame.tangent,normal).normalized;
            Vector3 right=Vector3.Cross(normal,tangent).normalized;
            Vector3 planar=Vector3.ProjectOnPlane(velocity,normal),body=Vector3.ProjectOnPlane(forward,normal).normalized;
            double heading=System.Math.Atan2(-Vector3.Dot(body,right),Vector3.Dot(body,tangent));
            double speed=planar.magnitude*(Vector3.Dot(planar,body)<0?-1:1);
            Vector3 travel=speed<0?-planar:planar;
            double motion=planar.sqrMagnitude<1e-12?heading:System.Math.Atan2(-Vector3.Dot(travel,right),Vector3.Dot(travel,tangent));
            return new HandlingObservation(speed,heading,motion,-Vector3.Dot(angularVelocity,normal),-Vector3.Dot(planar,right));
        }
        public static void Increments(HumanHandlingState next,HandlingObservation before,Vector3 velocity,TrackFrame frame,float dt,out Vector3 acceleration,out Vector3 angularAcceleration)
        { IncrementsWithLateral(next,before,velocity,frame,dt,next.lateralVelocity,out acceleration,out angularAcceleration); }
        public static double LateralTarget(HandlingStepTargets targets,double observedLateral)
        { return targets.NeutralNitroFinalTarget?0:HumanHandling.Clamp(targets.RawLateralVelocity,System.Math.Min(-18,observedLateral),System.Math.Max(18,observedLateral)); }
        public static void Increments(HumanHandlingState next,HandlingObservation before,Vector3 velocity,TrackFrame frame,float dt,HandlingStepTargets targets,out Vector3 acceleration,out Vector3 angularAcceleration)
        { IncrementsWithLateral(next,before,velocity,frame,dt,LateralTarget(targets,before.LateralVelocity),out acceleration,out angularAcceleration); }
        static void IncrementsWithLateral(HumanHandlingState next,HandlingObservation before,Vector3 velocity,TrackFrame frame,float dt,double lateral,out Vector3 acceleration,out Vector3 angularAcceleration)
        {
            Vector3 normal=frame.normal.normalized,tangent=Vector3.ProjectOnPlane(frame.tangent,normal).normalized;
            Vector3 right=Vector3.Cross(normal,tangent).normalized;
            Vector3 desired=tangent*(float)(next.speed*System.Math.Cos(next.motionYaw))-right*(float)lateral;
            acceleration=(desired-Vector3.ProjectOnPlane(velocity,normal))/dt;
            double requestedYaw=HumanHandling.Wrap(next.heading-before.Heading)/dt;
            angularAcceleration=-normal*(float)((requestedYaw-before.YawRate)/dt);
        }
    }
}

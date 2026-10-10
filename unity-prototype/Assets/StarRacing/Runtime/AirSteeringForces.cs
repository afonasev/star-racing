using UnityEngine;
namespace StarRacingPrototype {
 // Opt-in horizontal steering for confirmed human ramp flight. No route target or stored speed.
 public static class AirSteeringForces {
  public const float MaxYawRate=10f*Mathf.Deg2Rad,MaxAcceleration=16f,Deadzone=.01f;
  static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
  static bool Finite(Vector3 v)=>Finite(v.x)&&Finite(v.y)&&Finite(v.z);
  public static Vector3 VelocityChange(Vector3 velocity,Vector3 planeNormal,float steer,float step){
   if(!Finite(velocity)||!Finite(planeNormal)||!Finite(steer)||!Finite(step)||step<=0||planeNormal.sqrMagnitude<.5f||Mathf.Abs(steer)<Deadzone)return Vector3.zero;
   var normal=planeNormal.normalized;var horizontal=Vector3.ProjectOnPlane(velocity,normal);float speed=horizontal.magnitude;if(speed<.1f)return Vector3.zero;
   float rate=Mathf.Clamp(steer,-1,1)*Mathf.Min(MaxYawRate,MaxAcceleration/speed);
   var rotated=Quaternion.AngleAxis(rate*step*Mathf.Rad2Deg,normal)*horizontal;
   return Vector3.ProjectOnPlane(rotated-horizontal,normal);
  }
  public static void Apply(Rigidbody body,Vector3 normal,float steer,float step){
   if(body==null||body.isKinematic||!body.gameObject.activeInHierarchy||!Finite(normal)||!Finite(steer)||!Finite(step)||step<=0||normal.sqrMagnitude<.5f||Mathf.Abs(steer)<Deadzone)return;
   normal=normal.normalized;var change=VelocityChange(body.linearVelocity,normal,steer,step);body.AddForce(change,ForceMode.VelocityChange);
   var travel=Vector3.ProjectOnPlane(body.linearVelocity+change,normal);var forward=Vector3.ProjectOnPlane(body.rotation*Vector3.forward,normal);
   if(travel.sqrMagnitude<.01f||forward.sqrMagnitude<.01f)return;
   float error=Vector3.SignedAngle(forward,travel,normal)*Mathf.Deg2Rad;
   float target=Mathf.Clamp(error*4,-20*Mathf.Deg2Rad,20*Mathf.Deg2Rad);
   float acceleration=Mathf.Clamp(6*(target-Vector3.Dot(body.angularVelocity,normal)),-1.5f,1.5f);
   body.AddTorque(normal*(acceleration*step),ForceMode.VelocityChange);
  }
 }
}

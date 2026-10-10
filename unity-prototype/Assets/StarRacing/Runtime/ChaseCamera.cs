using UnityEngine;
namespace StarRacingPrototype {
 [DefaultExecutionOrder(100)]
 public sealed class ChaseCamera : MonoBehaviour {
  public MagneticVehicle target;
  Vector3 up=Vector3.up;
  bool fresh=true;
  float jumpHeight;int epoch=-1;MagneticVehicle previousTarget;
  public bool FinishLocked { get; private set; }
  public void Snap() { fresh=true; FinishLocked=false; }
  public void LockAtFinish() { FinishLocked=true; }
  void LateUpdate() {
   if(target==null || FinishLocked) return;
   if(previousTarget!=target||epoch!=target.PresentationEpoch){fresh=true;previousTarget=target;epoch=target.PresentationEpoch;}
   var pose=target.RenderPose;var frame=target.RenderFrame;
   float blend=1-Mathf.Exp(-7*Time.unscaledDeltaTime);
   up=fresh?frame.normal:Vector3.Slerp(up,frame.normal,blend).normalized;
   var forward=Vector3.ProjectOnPlane(pose.rotation*Vector3.forward,up).normalized;
   if(forward.sqrMagnitude<.1f) forward=frame.tangent;
   var focus=pose.position+up*1.1f;
   // Keep the road close enough to retain motion cues while a ramp jump rises.
   // Framing still follows the real car; only the camera's vertical travel is reduced.
   float height=target.ActiveJumpId!=""?Mathf.Max(0,Vector3.Dot(pose.position-frame.position,frame.normal)-1.1f):0;
   jumpHeight=fresh?height:Mathf.Lerp(jumpHeight,height,blend);
   var desired=focus-forward*10+up*(4.8f-jumpHeight*.75f);
   transform.position=fresh?desired:Vector3.Lerp(transform.position,desired,1-Mathf.Exp(-9*Time.unscaledDeltaTime));
   transform.rotation=Quaternion.LookRotation(focus+forward*9-transform.position,up);fresh=false;
  }
 }
}

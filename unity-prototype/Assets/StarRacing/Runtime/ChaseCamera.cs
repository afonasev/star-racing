using UnityEngine;
namespace StarRacingPrototype {
 [DefaultExecutionOrder(100)]
 public sealed class ChaseCamera : MonoBehaviour {
  public MagneticVehicle target;
  Vector3 up=Vector3.up;
  bool fresh=true;
  Vector3 lastFocus;
  Camera view;
  float jumpHeight;int epoch=-1;MagneticVehicle previousTarget;
  public bool FinishLocked { get; private set; }
  public void Snap() { fresh=true; FinishLocked=false; }
  public void LockAtFinish() { FinishLocked=true; }
  void LateUpdate() { UpdateCamera(Time.unscaledDeltaTime); }
  void UpdateCamera(float deltaTime) {
   if(target==null || FinishLocked) return;
   if(previousTarget!=target||epoch!=target.PresentationEpoch){fresh=true;previousTarget=target;epoch=target.PresentationEpoch;}
   var pose=target.RenderPose;var frame=target.RenderFrame;
   float blend=1-Mathf.Exp(-7*deltaTime);
   up=fresh?frame.normal:Vector3.Slerp(up,frame.normal,blend).normalized;
   var forward=Vector3.ProjectOnPlane(pose.rotation*Vector3.forward,up).normalized;
   if(forward.sqrMagnitude<.1f) forward=frame.tangent;
   var focus=pose.position+up*1.1f;
   if(view==null)view=GetComponent<Camera>();
   bool split=view!=null&&(view.rect.width<.99f||view.rect.height<.99f);
   float distance=split?7f:10f;
   // Keep the road close enough to retain motion cues while a ramp jump rises.
   // Framing still follows the real car; only the camera's vertical travel is reduced.
   float height=target.ActiveJumpId!=""?Mathf.Max(0,Vector3.Dot(pose.position-frame.position,frame.normal)-1.1f):0;
   jumpHeight=fresh?height:Mathf.Lerp(jumpHeight,height,blend);
   var desired=focus-forward*distance+up*((split?3.6f:4.8f)-jumpHeight*.75f);
   // Translate with the rendered car, smoothing only the relative offset.
   // World-space smoothing adds speed-dependent trailing distance.
   var position=fresh?desired:Vector3.Lerp(transform.position+focus-lastFocus,desired,1-Mathf.Exp(-9*deltaTime));
   // The road behind the car can have a different height and bank. Correct
   // the final smoothed position, including recovery from a below-road pose.
   for(int i=0;i<3;i++){
    var road=target.ProjectPresentationFrame(position);
    float clearance=Vector3.Dot(position-road.position,road.normal);
    if(clearance>=2f)break;
    position+=road.normal*(2f-clearance);
   }
   transform.position=position;
   transform.rotation=Quaternion.LookRotation(focus+forward*(split?6f:9f)-position,up);lastFocus=focus;fresh=false;
  }
 }
}

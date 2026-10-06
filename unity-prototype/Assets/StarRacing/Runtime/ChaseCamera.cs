using UnityEngine;
namespace StarRacingPrototype {
 public sealed class ChaseCamera : MonoBehaviour {
  public MagneticVehicle target;
  Vector3 up=Vector3.up;
  bool fresh=true;
  public bool FinishLocked { get; private set; }
  public void Snap() { fresh=true; FinishLocked=false; }
  public void LockAtFinish() { FinishLocked=true; }
  void LateUpdate() {
   if(target==null || FinishLocked) return;
   float blend=1-Mathf.Exp(-7*Time.unscaledDeltaTime);
   up=Vector3.Slerp(up,target.Frame.normal,blend).normalized;
   var forward=Vector3.ProjectOnPlane(target.transform.forward,up).normalized;
   if(forward.sqrMagnitude<.1f) forward=target.Frame.tangent;
   var focus=target.transform.position+up*1.1f;
   var desired=focus-forward*10+up*4.8f;
   transform.position=fresh?desired:Vector3.Lerp(transform.position,desired,1-Mathf.Exp(-9*Time.unscaledDeltaTime));
   transform.rotation=Quaternion.LookRotation(focus+forward*9-transform.position,up);fresh=false;
  }
 }
}

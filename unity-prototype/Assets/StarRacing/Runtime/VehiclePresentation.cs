using UnityEngine;
namespace StarRacingPrototype {
 // Rendering owns this history; no sampled pose is written to the Rigidbody.
 public sealed class VehiclePresentation {
  Pose previous,current; double time;float duration;bool ready,history;
  public int Epoch {get;private set;}
  public void Reset(Pose pose){previous=current=pose;ready=true;history=false;Epoch++;}
  public void Capture(Pose pose,double tickTime,float step){
   if(!ready){Reset(pose);return;}
   previous=current;current=pose;time=tickTime;duration=step;history=step>0;
  }
  public Pose Sample(double renderTime)=>history?new Pose(Vector3.Lerp(previous.position,current.position,Mathf.Clamp01((float)((renderTime-time)/duration))),Quaternion.Slerp(previous.rotation,current.rotation,Mathf.Clamp01((float)((renderTime-time)/duration)))):current;
 }
}

using UnityEngine;
using UnityEngine.InputSystem;
namespace StarRacingPrototype {
 public sealed class LocalInputRouter {
  readonly Gamepad[] pads = new Gamepad[2];
  bool blocked = true;
  public string DeviceName(int seat) => pads[seat] != null && pads[seat].added ? pads[seat].displayName : "KEYBOARD";
  public void Block() { blocked = true; }
  // A new race accepts prepared throttle at GO; a pause/resume still requires release.
  public void PrepareStart() { blocked = false; }
  public void Refresh() {
   for(int i=0;i<2;i++) if(pads[i]!=null && !pads[i].added) pads[i]=null;
   foreach(var pad in Gamepad.all) {
    if(pads[0]==pad || pads[1]==pad) continue;
    for(int i=0;i<2;i++) if(pads[i]==null) { pads[i]=pad; break; }
   }
   if(blocked && Neutral(ReadRaw(0)) && Neutral(ReadRaw(1))) blocked=false;
  }
  static bool Neutral(DrivingInput x) => x.throttle==0 && x.brake==0 && Mathf.Abs(x.steer)<.05f && !x.drift && !x.nitro;
  public DrivingInput Read(int seat) => blocked ? default : ReadRaw(seat);
  public DrivingInput ReadRaw(int seat) {
   var k=Keyboard.current; DrivingInput x=default;
   if(k!=null) {
    if(seat==0) x=new DrivingInput {throttle=k.wKey.isPressed?1:0,brake=k.sKey.isPressed?1:0,steer=(k.dKey.isPressed?1:0)-(k.aKey.isPressed?1:0),drift=k.spaceKey.isPressed,nitro=k.leftShiftKey.isPressed};
    else x=new DrivingInput {throttle=k.upArrowKey.isPressed?1:0,brake=k.downArrowKey.isPressed?1:0,steer=(k.rightArrowKey.isPressed?1:0)-(k.leftArrowKey.isPressed?1:0),drift=k.rightAltKey.isPressed,nitro=k.rightShiftKey.isPressed};
   }
   var p=pads[seat]; if(p==null || !p.added) return x;
   float stick=p.leftStick.x.ReadValue(); if(Mathf.Abs(stick)<.18f) stick=0;
   float d=p.dpad.x.ReadValue(); float steer=d!=0?d:stick;
   x.throttle=Mathf.Max(x.throttle,p.rightTrigger.ReadValue(),p.rightShoulder.isPressed?1:0);
   x.brake=Mathf.Max(x.brake,p.buttonSouth.isPressed?1:0);
   if(Mathf.Abs(steer)>Mathf.Abs(x.steer)) x.steer=steer;
   x.drift |= p.leftShoulder.isPressed || p.leftTrigger.ReadValue()>.5f;
   x.nitro |= p.buttonEast.isPressed;
   return x;
  }
 }
}

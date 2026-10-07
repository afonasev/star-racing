using UnityEngine;
using UnityEngine.InputSystem;
namespace StarRacingPrototype {
 public sealed class LocalInputRouter {
  readonly Gamepad[] pads=new Gamepad[4];
  readonly int[] devices={-2,-1,0,1};
  int humans=2;
  Keyboard keyboard=Keyboard.current;
  bool blocked=true,sessionBound;
  public static readonly string[] DeviceLabels={"Клавиатура · WASD","Клавиатура · стрелки","Геймпад 1","Геймпад 2","Геймпад 3","Геймпад 4"};
  public static string Label(int device)=>SessionControllers.Label(device);
  public string DeviceName(int seat)=>Label(devices[seat]);
  public static bool Available(int device)=>device<0?Keyboard.current!=null:SessionControllers.Resolve(device)!=null;
  public static bool CanBind(LocalRaceConfig config,out string error){
   if(!config.Validate(out error))return false;
   if(config.humans==0){error="Добавьте хотя бы одного игрока";return false;}
   for(int i=0;i<config.humans;i++)if(!Available(config.devices[i])){error="Игрок "+(i+1)+": подключите "+Label(config.devices[i]).ToLowerInvariant()+" или выберите другое устройство";return false;}
   return true;
  }
  public bool TryBind(LocalRaceConfig config,out string error){
   bool result=TryBind(config,Keyboard.current,SessionControllers.Snapshot(),out error);sessionBound=result;return result;
  }
  // Explicit catalog also lets isolated input checks use only their own virtual devices.
  public bool TryBind(LocalRaceConfig config,Keyboard sourceKeyboard,Gamepad[] candidates,out string error){
   if(!config.Validate(out error))return false;
   if(config.humans==0){error="Добавьте хотя бы одного игрока";return false;}
   for(int i=0;i<config.humans;i++)if(config.devices[i]<0?sourceKeyboard==null:config.devices[i]>=candidates.Length||candidates[config.devices[i]]==null){error="Недоступно устройство игрока "+(i+1);return false;}
   sessionBound=false;humans=config.humans;keyboard=sourceKeyboard;
   for(int i=0;i<4;i++){devices[i]=config.devices[i];pads[i]=i<humans&&devices[i]>=0?candidates[devices[i]]:null;}
   Block();return true;
  }
  public bool PausePressed {get{for(int i=0;i<humans;i++)if(pads[i]!=null&&pads[i].added&&pads[i].startButton.wasPressedThisFrame)return true;return false;}}
  public bool MissingDevice {get{for(int i=0;i<humans;i++)if(devices[i]<0?keyboard==null||!keyboard.added:pads[i]==null||!pads[i].added)return true;return false;}}
  public void Block(){blocked=true;}
  public void PrepareStart(){blocked=false;}
  public void Refresh(){
   // Resolve only the original session slot, never the current Gamepad.all position.
   if(sessionBound){keyboard=Keyboard.current;for(int i=0;i<humans;i++)if(devices[i]>=0)pads[i]=SessionControllers.Resolve(devices[i]);}
   if(blocked&&!MissingDevice){bool neutral=true;for(int i=0;i<humans;i++)neutral&=Neutral(ReadRaw(i));if(neutral)blocked=false;}
  }
  static bool Neutral(DrivingInput x)=>x.throttle==0&&x.brake==0&&Mathf.Abs(x.steer)<.05f&&!x.drift&&!x.nitro;
  public DrivingInput Read(int seat)=>blocked?default:ReadRaw(seat);
  public DrivingInput ReadRaw(int seat){
   if(seat<0||seat>=humans)return default;
   var k=keyboard;DrivingInput x=default;
   if(k!=null&&devices[seat]==-2)x=new DrivingInput{throttle=k.wKey.isPressed?1:0,brake=k.sKey.isPressed?1:0,steer=(k.dKey.isPressed?1:0)-(k.aKey.isPressed?1:0),drift=k.spaceKey.isPressed,nitro=k.leftShiftKey.isPressed};
   else if(k!=null&&devices[seat]==-1)x=new DrivingInput{throttle=k.upArrowKey.isPressed?1:0,brake=k.downArrowKey.isPressed?1:0,steer=(k.rightArrowKey.isPressed?1:0)-(k.leftArrowKey.isPressed?1:0),drift=k.rightAltKey.isPressed,nitro=k.rightShiftKey.isPressed};
   var p=pads[seat];if(p==null||!p.added)return x;
   float stick=p.leftStick.x.ReadValue();if(Mathf.Abs(stick)<.18f)stick=0;float d=p.dpad.x.ReadValue();
   return new DrivingInput{throttle=Mathf.Max(p.rightTrigger.ReadValue(),p.rightShoulder.isPressed?1:0),brake=p.buttonSouth.isPressed?1:0,steer=d!=0?d:stick,drift=p.leftShoulder.isPressed||p.leftTrigger.ReadValue()>.5f,nitro=p.buttonEast.isPressed};
  }
 }
 public static class RaceViewports {
  public static Rect For(int humans,int seat){
   if(humans<1||humans>4||seat<0||seat>=humans)throw new System.ArgumentOutOfRangeException();
   if(humans==1)return new Rect(0,0,1,1);
   if(humans==2)return new Rect(seat*.5f,0,.5f,1);
   return new Rect((seat%2)*.5f,seat<2?.5f:0,.5f,.5f);
  }
 }
}

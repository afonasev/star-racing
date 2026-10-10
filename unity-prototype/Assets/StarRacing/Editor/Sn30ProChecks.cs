using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;

namespace StarRacingPrototype {
 public static class Sn30ProChecks {
  static int assertions;
  static void Check(bool ok,string name){assertions++;if(!ok)throw new Exception("SN30: "+name);}
  static void Send(Gamepad pad,Sn30ProState state){InputSystem.QueueStateEvent(pad,state);InputSystem.Update();}
  public static void Run(){
   assertions=0;Sn30ProGamepad.Register();
   var description=new InputDeviceDescription{interfaceName="HID",product="8Bitdo SN30 Pro",capabilities="{\"vendorId\":1118,\"productId\":736}"};
   Check(InputSystem.TryFindMatchingLayout(description)==nameof(Sn30ProGamepad),"observed HID description does not match");
   var other=description;other.product="Unrelated Controller";
   Check(InputSystem.TryFindMatchingLayout(other)!=nameof(Sn30ProGamepad),"unrelated controller captured");
   other=description;other.capabilities="{\"vendorId\":1118,\"productId\":737}";
   Check(InputSystem.TryFindMatchingLayout(other)!=nameof(Sn30ProGamepad),"unobserved protocol captured");
   Gamepad pad=null,second=null;GameObject root=null;
   bool had=PlayerPrefs.HasKey(LocalRaceConfig.PreferenceKey);string saved=PlayerPrefs.GetString(LocalRaceConfig.PreferenceKey);
   try{
    pad=(Gamepad)InputSystem.AddDevice(description);second=InputSystem.AddDevice<Gamepad>();
    Check(pad is Sn30ProGamepad,"HID falls back to Joystick");
    var neutral=Sn30ProState.Neutral;Send(pad,neutral);
    Check(pad.leftStick.ReadValue().sqrMagnitude<.0001f&&pad.rightStick.ReadValue().sqrMagnitude<.0001f&&pad.dpad.ReadValue()==Vector2.zero,"neutral has phantom input");
    ButtonControl[] buttons={pad.buttonSouth,pad.buttonEast,pad.buttonWest,pad.buttonNorth,pad.leftShoulder,pad.rightShoulder,pad.selectButton,pad.startButton,pad.leftStickButton,pad.rightStickButton};
    for(int i=0;i<buttons.Length;i++){
     var state=neutral;state.buttons=(ushort)(1<<i);Send(pad,state);
     for(int j=0;j<buttons.Length;j++)Check(buttons[j].isPressed==(i==j),"button isolation "+i+"/"+j);
     Send(pad,neutral);
    }
    for(byte hat=1;hat<=8;hat++){
     var state=neutral;state.hat=hat;Send(pad,state);
     Check(pad.dpad.up.isPressed==(hat==1||hat==2||hat==8),"hat up "+hat);
     Check(pad.dpad.right.isPressed==(hat>=2&&hat<=4),"hat right "+hat);
     Check(pad.dpad.down.isPressed==(hat>=4&&hat<=6),"hat down "+hat);
     Check(pad.dpad.left.isPressed==(hat>=6&&hat<=8),"hat left "+hat);
    }
    var full=neutral;full.leftX=65535;full.leftY=0;full.rightX=0;full.rightY=65535;full.leftTrigger=1023;full.rightTrigger=1023;Send(pad,full);
    Check(pad.leftStick.x.ReadValue()>.9f&&pad.leftStick.y.ReadValue()>.9f&&pad.rightStick.x.ReadValue()<-.9f&&pad.rightStick.y.ReadValue()<-.9f,"axis orientation/range");
    Check(pad.leftTrigger.ReadValue()>.99f&&pad.rightTrigger.ReadValue()>.99f,"10-bit trigger normalization");
    var unrelated=neutral;unrelated.reportId=2;Send(pad,unrelated);
    Check(pad.leftTrigger.ReadValue()>.99f&&pad.leftStick.x.ReadValue()>.9f,"guide report overwrites controls");
    Send(pad,neutral);
    int first=SessionControllers.Slot(pad),next=SessionControllers.Slot(second);
    new LocalRaceConfig{humans=2,devices=new[]{-2,next,-1,first},names=new[]{"Первый","Второй","Игрок 3","Игрок 4"}}.Save();
    root=new GameObject("SN30 menu regression");var menu=root.AddComponent<RaceMenu>();root.SendMessage("Awake");
    menu.Open(RaceMenuScreen.LocalSetup,pad);
    Check(menu.Selected.humans==2&&menu.Selected.devices[0]==first&&menu.Selected.devices[1]==next,"entry does not replace first keyboard seat");
    Check(LocalRaceConfig.Load().devices[0]==first,"entry controller selection not saved");
    Check(!menu.JoinDevice(first),"Y duplicates initiating controller");
    menu.RenameSeat(0,"Лидер");menu.RenameSeat(1,"Другой");
    menu.Open(RaceMenuScreen.Main);menu.Open(RaceMenuScreen.LocalSetup,second);
    Check(menu.Selected.devices[0]==next&&menu.Selected.devices[1]==first&&menu.Selected.names[0]=="Другой"&&menu.Selected.names[1]=="Лидер","existing seat/name pair not swapped");
    menu.Open(RaceMenuScreen.Main);menu.Open(RaceMenuScreen.LocalSetup);
    Check(menu.Selected.devices[0]==next&&menu.Selected.devices[1]==first,"mouse/keyboard entry changed devices");
    menu.RemoveSeat(1);Check(menu.JoinDevice(first)&&!menu.JoinDevice(first),"Y join after entry broken");
    menu.RemoveSeat(1);menu.RemoveSeat(0);menu.Open(RaceMenuScreen.LocalSetup,pad);
    Check(menu.Selected.humans==1&&menu.Selected.devices[0]==first,"empty lobby entry missing first pad");
    InputSystem.RemoveDevice(pad);Check(SessionControllers.Resolve(first)==null&&SessionControllers.Resolve(next)==second,"disconnect shifted devices");
    InputSystem.AddDevice(pad);Check(SessionControllers.Resolve(first)==pad,"reconnect lost first player identity");
   }finally{
    if(root!=null)UnityEngine.Object.DestroyImmediate(root);
    if(pad!=null&&pad.added)InputSystem.RemoveDevice(pad);if(second!=null&&second.added)InputSystem.RemoveDevice(second);
    if(had)PlayerPrefs.SetString(LocalRaceConfig.PreferenceKey,saved);else PlayerPrefs.DeleteKey(LocalRaceConfig.PreferenceKey);PlayerPrefs.Save();
   }
   GamepadStartChecks.Run();
   Debug.Log("SN30_CHECKS_OK assertions="+assertions);
  }
 }
}

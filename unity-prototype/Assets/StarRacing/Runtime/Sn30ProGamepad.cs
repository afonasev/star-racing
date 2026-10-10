using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.InputSystem.Controls;

namespace StarRacingPrototype {
 // Bluetooth X-mode descriptor captured from the connected SN30 Pro on macOS.
 // It resembles Xbox HID, but its ten buttons are contiguous rather than sparse.
 [StructLayout(LayoutKind.Explicit, Size=16)]
 public struct Sn30ProState : IInputStateTypeInfo {
  public FourCC format=>new FourCC('H','I','D');
  [InputControl(name="reportId", layout="Integer", format="BYTE", noisy=true)]
  [FieldOffset(0)] public byte reportId;
  [InputControl(name="leftStick", layout="Stick", format="VC2S")]
  [InputControl(name="leftStick/x", offset=0, format="USHT", parameters="normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5", defaultState=32767)]
  [InputControl(name="leftStick/left", offset=0, format="USHT", parameters="normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5,clamp=1,clampMin=0,clampMax=0.5,invert")]
  [InputControl(name="leftStick/right", offset=0, format="USHT", parameters="normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5,clamp=1,clampMin=0.5,clampMax=1")]
  [InputControl(name="leftStick/y", offset=2, format="USHT", parameters="invert,normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5", defaultState=32767)]
  [InputControl(name="leftStick/up", offset=2, format="USHT", parameters="normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5,clamp=1,clampMin=0,clampMax=0.5,invert")]
  [InputControl(name="leftStick/down", offset=2, format="USHT", parameters="normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5,clamp=1,clampMin=0.5,clampMax=1")]
  [FieldOffset(1)] public ushort leftX;
  [FieldOffset(3)] public ushort leftY;
  [InputControl(name="rightStick", layout="Stick", format="VC2S")]
  [InputControl(name="rightStick/x", offset=0, format="USHT", parameters="normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5", defaultState=32767)]
  [InputControl(name="rightStick/left", offset=0, format="USHT", parameters="normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5,clamp=1,clampMin=0,clampMax=0.5,invert")]
  [InputControl(name="rightStick/right", offset=0, format="USHT", parameters="normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5,clamp=1,clampMin=0.5,clampMax=1")]
  [InputControl(name="rightStick/y", offset=2, format="USHT", parameters="invert,normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5", defaultState=32767)]
  [InputControl(name="rightStick/up", offset=2, format="USHT", parameters="normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5,clamp=1,clampMin=0,clampMax=0.5,invert")]
  [InputControl(name="rightStick/down", offset=2, format="USHT", parameters="normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5,clamp=1,clampMin=0.5,clampMax=1")]
  [FieldOffset(5)] public ushort rightX;
  [FieldOffset(7)] public ushort rightY;
  [InputControl(name="leftTrigger", format="USHT", parameters="normalize,normalizeMin=0,normalizeMax=0.01560998")]
  [FieldOffset(9)] public ushort leftTrigger;
  [InputControl(name="rightTrigger", format="USHT", parameters="normalize,normalizeMin=0,normalizeMax=0.01560998")]
  [FieldOffset(11)] public ushort rightTrigger;
  [InputControl(name="dpad", layout="Dpad", format="BIT", sizeInBits=4)]
  [InputControl(name="dpad/up", layout="DiscreteButton", format="BIT", bit=0, sizeInBits=4, parameters="minValue=8,maxValue=2,nullValue=0,wrapAtValue=9")]
  [InputControl(name="dpad/right", layout="DiscreteButton", format="BIT", bit=0, sizeInBits=4, parameters="minValue=2,maxValue=4")]
  [InputControl(name="dpad/down", layout="DiscreteButton", format="BIT", bit=0, sizeInBits=4, parameters="minValue=4,maxValue=6")]
  [InputControl(name="dpad/left", layout="DiscreteButton", format="BIT", bit=0, sizeInBits=4, parameters="minValue=6,maxValue=8")]
  [FieldOffset(13)] public byte hat;
  [InputControl(name="buttonSouth", bit=0)]
  [InputControl(name="buttonEast", bit=1)]
  [InputControl(name="buttonWest", bit=2)]
  [InputControl(name="buttonNorth", bit=3)]
  [InputControl(name="leftShoulder", bit=4)]
  [InputControl(name="rightShoulder", bit=5)]
  [InputControl(name="select", bit=6)]
  [InputControl(name="start", bit=7)]
  [InputControl(name="leftStickPress", bit=8)]
  [InputControl(name="rightStickPress", bit=9)]
  [FieldOffset(14)] public ushort buttons;
  public static Sn30ProState Neutral=>new Sn30ProState{reportId=1,leftX=32767,leftY=32767,rightX=32767,rightY=32767};
 }
 [InputControlLayout(displayName="8Bitdo SN30 Pro", stateType=typeof(Sn30ProState))]
 public sealed class Sn30ProGamepad : Gamepad, IInputStateCallbackReceiver {
  IntegerControl reportId;
  protected override void FinishSetup(){base.FinishSetup();reportId=GetChildControl<IntegerControl>("reportId");}
  public void OnNextUpdate(){}
  public void OnStateEvent(InputEventPtr input){
   // Guide/battery reports have their own IDs and must not overwrite axes/buttons.
   if(input.IsA<StateEvent>()&&input.sizeInBytes>=StateEvent.GetEventSizeWithPayload<Sn30ProState>()&&reportId.ReadValueFromEvent(input)==1)
    InputState.Change(this,input);
  }
  public bool GetStateOffsetForEvent(InputControl control,InputEventPtr input,ref uint offset)=>false;
  #if UNITY_EDITOR
  [UnityEditor.InitializeOnLoadMethod]
  #endif
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
  public static void Register(){
   #if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
   InputSystem.RegisterLayout<Sn30ProGamepad>(matches:new InputDeviceMatcher().WithInterface("HID")
    .WithProduct("^8Bitdo SN30 Pro$").WithCapability("vendorId",0x045e).WithCapability("productId",0x02e0));
   #endif
  }
 }
}

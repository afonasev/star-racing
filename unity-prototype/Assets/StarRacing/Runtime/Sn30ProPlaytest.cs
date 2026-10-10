using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;

namespace StarRacingPrototype {
 // Opt-in native regression: actual HID discovery plus isolated menu input reports.
 public sealed class Sn30ProPlaytest : MonoBehaviour {
  string output,previous;bool hadPrevious,saved;Gamepad pad,second;int assertions;
  [Serializable] sealed class DeviceEvidence {public string product,layout,type;public bool sn30;}
  [Serializable] sealed class Evidence {public bool success,physical_device_present,physical_buttons_verified;public int assertions;public DeviceEvidence[] devices;}
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Install(){var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,"--sn30-proof-dir");if(i>=0&&i+1<a.Length)new GameObject("SN30 native proof").AddComponent<Sn30ProPlaytest>().output=Path.GetFullPath(a[i+1]);}
  void Check(bool ok,string name){assertions++;if(!ok)throw new Exception("SN30_PLAYER "+name);Debug.Log("SN30_PLAYER_CHECK "+name);}
  IEnumerator Start(){
   Directory.CreateDirectory(output);Application.runInBackground=true;
   hadPrevious=PlayerPrefs.HasKey(LocalRaceConfig.PreferenceKey);previous=PlayerPrefs.GetString(LocalRaceConfig.PreferenceKey);saved=true;
   var stack=new Stack<IEnumerator>();stack.Push(Run());
   while(stack.Count>0){object next=null;bool move=false;Exception error=null;try{var routine=stack.Peek();move=routine.MoveNext();if(move)next=routine.Current;else stack.Pop();}catch(Exception e){error=e;}
    if(error!=null){File.WriteAllText(Path.Combine(output,"failure.txt"),error.ToString());Debug.LogException(error);Restore();Application.Quit(1);yield break;}
    if(!move)continue;if(next is IEnumerator nested){stack.Push(nested);continue;}yield return next;
   }
   Restore();Application.Quit();
  }
  IEnumerator Report(Sn30ProState state){InputSystem.QueueStateEvent(pad,state);yield return null;yield return null;}
  IEnumerator Shot(string name){yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));yield return new WaitForSecondsRealtime(.25f);}
  IEnumerator Run(){
   yield return new WaitForSecondsRealtime(1);
   var evidence=new Evidence();var devices=new List<DeviceEvidence>();
   foreach(var device in InputSystem.devices){
    bool sn30=device.description.product=="8Bitdo SN30 Pro";
    devices.Add(new DeviceEvidence{product=device.description.product,layout=device.layout,type=device.GetType().Name,sn30=sn30});
    if(sn30){evidence.physical_device_present=true;Check(device is Sn30ProGamepad,"actual SN30 resolves as Gamepad");}
   }
   evidence.devices=devices.ToArray();File.WriteAllText(Path.Combine(output,"discovery.json"),JsonUtility.ToJson(evidence,true));
   var director=FindAnyObjectByType<RaceDirector>();var menu=director.GetComponent<RaceMenu>();
   Check(!director.Started,"starts in menu");
   pad=(Gamepad)InputSystem.AddDevice(new InputDeviceDescription{interfaceName="HID",product="8Bitdo SN30 Pro",serial="sn30-qa-fixture",capabilities="{\"vendorId\":1118,\"productId\":736}"});second=InputSystem.AddDevice<Gamepad>();
   menu.Selected.humans=1;menu.Selected.devices[0]=-2;menu.Selected.entrants=8;menu.Selected.theme="cloud-city";menu.Selected.rails="normal";menu.Selected.jumps=false;menu.Selected.handicap=false;
   menu.Selected.Save();menu.Open(RaceMenuScreen.Main);
   yield return Report(Sn30ProState.Neutral);yield return Shot("01-main");
   var state=Sn30ProState.Neutral;state.hat=5;yield return Report(state);yield return Report(Sn30ProState.Neutral);
   state.buttons=1;state.hat=0;yield return Report(state);yield return Report(Sn30ProState.Neutral);
   Check(menu.ScreenState==RaceMenuScreen.Settings,"D-pad and A open settings");
   state.buttons=2;yield return Report(state);yield return Report(Sn30ProState.Neutral);
   Check(menu.ScreenState==RaceMenuScreen.Main,"B returns from settings");
   state.buttons=1;yield return Report(state);yield return Report(Sn30ProState.Neutral);
   int slot=SessionControllers.Slot(pad),next=SessionControllers.Slot(second);
   Check(menu.ScreenState==RaceMenuScreen.LocalSetup&&menu.Selected.humans==1&&menu.Selected.devices[0]==slot,"A entry chooses first controller");
   state.buttons=8;yield return Report(state);yield return Report(Sn30ProState.Neutral);
   Check(menu.Selected.humans==1,"Y does not duplicate first controller");
   InputSystem.QueueStateEvent(second,new GamepadState().WithButton(GamepadButton.North));yield return null;yield return null;
   InputSystem.QueueStateEvent(second,new GamepadState());yield return null;yield return null;
   Check(menu.Selected.humans==2&&menu.Selected.devices[1]==next,"Y on another gamepad joins its own seat");yield return Shot("02-joined");
   menu.RemoveSeat(1);menu.Selected.seed="77";
   typeof(RaceMenu).GetField("seedText",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(menu,"77");menu.StartSelected();
   Check(director.Started&&director.Input.DeviceName(0)==SessionControllers.Label(slot),"race binds chosen controller");
   while(director.Session.Phase==RacePhase.Countdown)yield return null;
   state=Sn30ProState.Neutral;state.rightTrigger=1023;yield return Report(state);
   Check(director.Input.Read(0).throttle>.99f,"HID trigger drives first player");yield return Shot("03-race-hud");
   yield return Report(Sn30ProState.Neutral);
   director.SetPaused(true);Check(director.ExitToMenu(),"race return to menu");
   evidence.success=true;evidence.assertions=assertions;File.WriteAllText(Path.Combine(output,"success.json"),JsonUtility.ToJson(evidence,true));
   Debug.Log("SN30_PLAYER_OK assertions="+assertions);
  }
  void OnApplicationQuit()=>Restore();
  void Restore(){if(!saved)return;saved=false;if(pad!=null&&pad.added)InputSystem.RemoveDevice(pad);if(second!=null&&second.added)InputSystem.RemoveDevice(second);if(hadPrevious)PlayerPrefs.SetString(LocalRaceConfig.PreferenceKey,previous);else PlayerPrefs.DeleteKey(LocalRaceConfig.PreferenceKey);PlayerPrefs.Save();}
 }
}

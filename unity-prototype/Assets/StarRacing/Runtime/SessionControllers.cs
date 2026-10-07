using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace StarRacingPrototype {
 // App-owned session slots never move when Gamepad.all is reordered by hotplug.
 public static class SessionControllers {
  static readonly List<Gamepad> pads=new List<Gamepad>();
  static readonly Dictionary<int,string> names=new Dictionary<int,string>();
  static bool initialized;
  [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
  static void Reset(){if(initialized)InputSystem.onDeviceChange-=Changed;initialized=false;pads.Clear();names.Clear();}
  public static void Ensure(){
   if(!initialized){initialized=true;InputSystem.onDeviceChange+=Changed;}
   foreach(var pad in Gamepad.all)Slot(pad);
  }
  static void Changed(InputDevice device,InputDeviceChange change){if(device is Gamepad pad&&(change==InputDeviceChange.Added||change==InputDeviceChange.Reconnected))Slot(pad);}
  public static int Slot(Gamepad pad){
   int existing=pads.IndexOf(pad);if(existing>=0)return existing;
   // A serial can identify a replacement object. Identical anonymous devices must
   // rely on Input System's retained reference; never guess by display name.
   if(!string.IsNullOrEmpty(pad.description.serial))for(int i=0;i<pads.Count;i++)
    if(!pads[i].added&&pads[i].description.serial==pad.description.serial&&pads[i].description.interfaceName==pad.description.interfaceName&&pads[i].description.product==pad.description.product){pads[i]=pad;return i;}
   pads.Add(pad);return pads.Count-1;
  }
  public static Gamepad Resolve(int slot){Ensure();return slot>=0&&slot<pads.Count&&pads[slot].added?pads[slot]:null;}
  public static Gamepad[] Snapshot(){Ensure();var result=new Gamepad[pads.Count];for(int i=0;i<result.Length;i++)result[i]=pads[i].added?pads[i]:null;return result;}
  public static int[] AvailableDevices(){Ensure();var result=new List<int>();if(Keyboard.current!=null){result.Add(-2);result.Add(-1);}for(int i=0;i<pads.Count;i++)if(pads[i].added)result.Add(i);return result.ToArray();}
  public static string Label(int device){
   if(device<0)return device==-2?"Клавиатура · WASD":"Клавиатура · стрелки";
   Ensure();return device<pads.Count?pads[device].displayName:"Геймпад "+(device+1);
  }
  public static void Remember(int device,string name){names[device]=LocalRaceConfig.CleanName(name);}
  public static string Name(int device,string fallback)=>names.TryGetValue(device,out var name)?name:fallback;
  public static void SeedNames(LocalRaceConfig config){Ensure();for(int i=0;i<config.humans;i++)if(!names.ContainsKey(config.devices[i]))Remember(config.devices[i],config.names[i]);}
 }
}

using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
namespace StarRacingPrototype {
 // Opt-in visual probe, uses real chase cameras. Teleports are recorded; this is not race completion evidence.
 public sealed class SkyboxPlaytest:MonoBehaviour {
  string output;readonly Gamepad[] pads=new Gamepad[4];RaceDirector director;float oldVolume;
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Install(){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--skybox-proof-dir");if(i>=0&&i+1<args.Length){var p=new GameObject("Skybox visual proof").AddComponent<SkyboxPlaytest>();p.output=Path.GetFullPath(args[i+1]);}}
  IEnumerator Start(){
   var stack=new System.Collections.Generic.Stack<IEnumerator>();stack.Push(Run());
   while(stack.Count>0){object next=null;bool move=false;Exception error=null;try{var routine=stack.Peek();move=routine.MoveNext();if(move)next=routine.Current;else stack.Pop();}catch(Exception e){error=e;}
    if(error!=null){Debug.LogException(error);Fail(error.ToString());yield break;}if(!move)continue;if(next is IEnumerator nested)stack.Push(nested);else yield return next;
   }
  }
  IEnumerator Run(){
   Application.runInBackground=true;oldVolume=AudioListener.volume;AudioListener.volume=0;Directory.CreateDirectory(output);
   yield return new WaitForSecondsRealtime(1);director=FindAnyObjectByType<RaceDirector>();
   for(int i=0;i<4;i++)pads[i]=InputSystem.AddDevice<Gamepad>();
   int pass=0;
   foreach(string theme in new[]{"cloud-city","space-station","cloud-city"}) {
    int seats=1;
    yield return Race(theme,seats,pass==2?"city-return":theme=="space-station"?"space":"city");pass++;
   }
   for(int seats=2;seats<=4;seats++)yield return Race(seats%2==0?"space-station":"cloud-city",seats,"split-"+seats);
   for(uint seed=78;seed<=80;seed++)yield return Race("space-station",1,"planet-"+seed,seed);
   File.WriteAllText(Path.Combine(output,"success.json"),"{\"success\":true,\"seed\":77,\"probe_teleports\":true,\"physical_devices\":false}");Application.Quit();
  }
  IEnumerator Race(string theme,int seats,string prefix,uint seed=77){
   if(director.Started){director.SetPaused(true);if(!director.ExitToMenu())throw new Exception("return");}
   var config=new LocalRaceConfig{humans=seats,entrants=8,seed=seed.ToString(),theme=theme,rails="normal",jumps=false};
   for(int i=0;i<4;i++)config.devices[i]=SessionControllers.Slot(pads[i]);
   if(!director.TryStartSelected(config,out string error))throw new Exception(error);
   yield return new WaitForSecondsRealtime(4.2f);if(director.Paused)director.StartRace();
   Time.timeScale=0;foreach(var car in director.Cars)car.Hold(true);
   yield return Shot(prefix+"-start");
   if(seats==1){
    var route=director.Track.Route;float middle=route.Length*.30f;
    director.Cars[0].ResetAt(middle);director.CameraFor(0).Snap();yield return new WaitForSecondsRealtime(.8f);yield return Shot(prefix+"-mid");
    float inverted=0;float minimum=1;foreach(var frame in route.Samples)if(frame.normal.y<minimum){minimum=frame.normal.y;inverted=frame.distance;}
    director.Cars[0].ResetAt(inverted);director.CameraFor(0).Snap();yield return new WaitForSecondsRealtime(1.2f);yield return Shot(prefix+"-inverted");
   }
   Time.timeScale=1;
  }
  IEnumerator Shot(string name){
   if(director.Paused)director.StartRace();Time.timeScale=0;
   yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));
   var camera=director.CameraFor(0).GetComponent<Camera>();
   File.WriteAllText(Path.Combine(output,name+".json"),JsonUtility.ToJson(new Frame{seed=director.TrackSeed,theme=director.TrackThemePreference,seats=director.HumanCount,position=camera.transform.position,rotation=camera.transform.eulerAngles,distance=director.Cars[0].Distance,state=director.Session.Phase.ToString()},true));
   yield return new WaitForSecondsRealtime(.4f);
  }
  [Serializable] sealed class Frame{public uint seed;public string theme,state;public int seats;public Vector3 position,rotation;public float distance;}
  void Fail(string reason){File.WriteAllText(Path.Combine(output,"failure.txt"),reason);Debug.LogError(reason);Application.Quit(1);}
  void OnDestroy(){Time.timeScale=1;AudioListener.volume=oldVolume;foreach(var pad in pads)if(pad!=null&&pad.added)InputSystem.RemoveDevice(pad);}
 }
}

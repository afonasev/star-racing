using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
namespace StarRacingPrototype {
 // Opt-in bounded native witness; inert without the explicit command-line flag.
 public sealed class VectorPlaytest:MonoBehaviour {
  string output,previous;bool hadPrevious,saved;readonly List<Gamepad> pads=new List<Gamepad>();RaceDirector director;int assertions;float deadline;
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Install(){var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"--vector-proof-dir");if(index>=0&&index+1<args.Length)new GameObject("Vector native witness").AddComponent<VectorPlaytest>().output=Path.GetFullPath(args[index+1]);}
  IEnumerator Start(){
   Application.runInBackground=true;Directory.CreateDirectory(output);deadline=Time.realtimeSinceStartup+120;
   hadPrevious=PlayerPrefs.HasKey(LocalRaceConfig.PreferenceKey);previous=PlayerPrefs.GetString(LocalRaceConfig.PreferenceKey);saved=true;
   var stack=new Stack<IEnumerator>();stack.Push(Run());
   while(stack.Count>0){object next=null;bool moved=false;Exception error=null;try{if(Time.realtimeSinceStartup>deadline)throw new Exception("Vector witness deadline");var r=stack.Peek();moved=r.MoveNext();if(moved)next=r.Current;else{stack.Pop();(r as IDisposable)?.Dispose();}}catch(Exception e){error=e;}
    if(error!=null){File.WriteAllText(Path.Combine(output,"failure.txt"),error.ToString());Debug.LogException(error);Restore();Application.Quit(1);yield break;}
    if(!moved)continue;if(next is IEnumerator nested)stack.Push(nested);else yield return next;
   }
   File.WriteAllText(Path.Combine(output,"success.json"),"{\"success\":true,\"assertions\":"+assertions+",\"humans\":[1,4],\"entrants\":8,\"physicalDevices\":false,\"naturalFinish\":\"NotRun - bounded visual witness\",\"visualRevision\":\""+StaticVehicleVisual.VisualRevision+"\"}");Debug.Log("VECTOR_PLAYER_OK assertions="+assertions);Restore();Application.Quit();
  }
  void Check(bool ok,string why){assertions++;if(!ok)throw new Exception("VECTOR_PLAYER "+why);}
  void Input(float gas,bool nitro=false){var s=new GamepadState{rightTrigger=gas};if(nitro)s=s.WithButton(GamepadButton.East);InputSystem.QueueStateEvent(pads[0],s);}
  void StartRace(int humans){
   var config=LocalRaceConfig.Load();config.humans=humans;config.entrants=8;config.devices=new int[4];for(int i=0;i<4;i++)config.devices[i]=SessionControllers.Slot(pads[i]);config.theme="cloud-city";config.seed="77";config.rails="normal";config.jumps=false;
   Check(director.TryStartSelected(config,out var error),"start "+humans+" "+error);
   Mesh shared=null;Material material=null;
   foreach(var car in director.Cars){var visual=car.GetComponentInChildren<StaticVehicleVisual>();Check(visual!=null,"Vector visual");var renderers=visual.CanonicalRoot.GetComponentsInChildren<MeshRenderer>();Check(renderers.Length==1,"one body renderer");var mesh=renderers[0].GetComponent<MeshFilter>().sharedMesh;Check(!mesh.isReadable&&mesh.GetIndexCount(0)==79318*3,"full geometry with no CPU duplicate");if(shared==null){shared=mesh;material=renderers[0].sharedMaterial;}else Check(shared==mesh&&material==renderers[0].sharedMaterial,"all entrants share mesh/material");Check(visual.Exhaust!=null,"exhaust");}
  }
  IEnumerator Shot(string name,bool detail=false){
   var chase=director.CameraFor(0);var cam=chase.GetComponent<Camera>();var pos=cam.transform.position;var rot=cam.transform.rotation;
   if(detail){chase.enabled=false;var car=director.Cars[0].transform;cam.transform.position=car.TransformPoint(new Vector3(4,2,-5));cam.transform.LookAt(car.TransformPoint(new Vector3(0,.4f,0)));}
   yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));yield return new WaitForSecondsRealtime(.3f);
   if(detail){cam.transform.SetPositionAndRotation(pos,rot);chase.enabled=true;}
  }
  IEnumerator Run(){
   yield return new WaitForSecondsRealtime(1);director=FindAnyObjectByType<RaceDirector>();Check(director!=null,"director");for(int i=0;i<4;i++)pads.Add(InputSystem.AddDevice<Gamepad>());
   StartRace(1);var car=director.Cars[0];var exhaust=car.GetComponentInChildren<VehicleExhaust>();var start=car.Body.position;float charge=car.Drive.Charge;
   Input(1,true);yield return new WaitForSecondsRealtime(.65f);Check(car.Body.isKinematic&&(car.Body.position-start).sqrMagnitude<.000001f,"countdown locked");Check(exhaust.Emitting&&car.Drive.Charge==charge&&!car.Drive.Active,"countdown gas without nitro");yield return Shot("01-vector-detail",true);yield return Shot("02-countdown");Input(1);
   while(director.Session.Phase==RacePhase.Countdown)yield return null;
   yield return new WaitForSecondsRealtime(1);Check(!car.Body.isKinematic&&car.Body.linearVelocity.magnitude>1,"normal movement");Input(1,true);yield return new WaitForSecondsRealtime(.75f);Check(car.Drive.Active&&exhaust.Emitting,"nitro and exhaust");yield return Shot("03-race-nitro");
   director.SetPaused(true);yield return null;Check(!exhaust.Emitting,"pause clears exhaust");Input(0);Check(director.ExitToMenu(),"return menu");yield return null;
   StartRace(4);Check(director.HumanCount==4,"four local seats");for(int i=0;i<4;i++)Check(director.CameraFor(i).GetComponent<Camera>().enabled,"camera enabled "+i);yield return Shot("04-four-player");director.SetPaused(true);Check(director.ExitToMenu(),"four-player menu");
  }
  void OnApplicationQuit()=>Restore();void OnDestroy()=>Restore();
  void Restore(){if(!saved)return;saved=false;if(hadPrevious)PlayerPrefs.SetString(LocalRaceConfig.PreferenceKey,previous);else PlayerPrefs.DeleteKey(LocalRaceConfig.PreferenceKey);PlayerPrefs.Save();foreach(var pad in pads)if(pad!=null&&pad.added)InputSystem.RemoveDevice(pad);}
 }
}

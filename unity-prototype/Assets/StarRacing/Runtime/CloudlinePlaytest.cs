using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
namespace StarRacingPrototype {
 // Opt-in native evidence runner. Inert in normal play; never supplies a fixture finish.
 public sealed class CloudlinePlaytest : MonoBehaviour {
  string output,previous;bool hadPrevious,backupCaptured;int assertions;
  readonly Gamepad[] owned=new Gamepad[4];
  RaceDirector director;RaceMenu menu;
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Install(){var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"--cloudline-proof-dir");if(index>=0&&index+1<args.Length){var test=new GameObject("Cloudline native proof").AddComponent<CloudlinePlaytest>();test.output=Path.GetFullPath(args[index+1]);}}
  IEnumerator Start(){
   Application.runInBackground=true;Directory.CreateDirectory(output);hadPrevious=PlayerPrefs.HasKey(LocalRaceConfig.PreferenceKey);previous=PlayerPrefs.GetString(LocalRaceConfig.PreferenceKey);backupCaptured=true;
   File.WriteAllText(Path.Combine(output,"preferences-before.json"),JsonUtility.ToJson(new PreferenceBackup{existed=hadPrevious,value=previous}));
   var routines=new Stack<IEnumerator>();routines.Push(Run());
   while(routines.Count>0){object next=null;bool move=false;Exception error=null;try{var routine=routines.Peek();move=routine.MoveNext();if(move)next=routine.Current;else{routines.Pop();(routine as IDisposable)?.Dispose();}}catch(Exception e){error=e;}
    if(error!=null){Debug.LogException(error);File.WriteAllText(Path.Combine(output,"failure.txt"),error.ToString());Restore();Application.Quit(1);yield break;}
    if(!move)continue;if(next is IEnumerator nested){routines.Push(nested);continue;}yield return next;
   }
   File.WriteAllText(Path.Combine(output,"success.json"),"{\"success\":true,\"assertions\":"+assertions+",\"physical_devices\":false,\"natural_ai_finish\":true}");
   Debug.Log("CLOUDLINE_PLAYER_OK assertions="+assertions);Restore();Application.Quit();
  }
  [Serializable] sealed class PreferenceBackup{public bool existed;public string value;}
  void Check(bool condition,string name){assertions++;if(!condition)throw new Exception("CLOUDLINE_PLAYER "+name);Debug.Log("CLOUDLINE_PLAYER_CHECK "+name);}
  IEnumerator Shot(string name){yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));yield return new WaitForSecondsRealtime(.35f);}
  void MenuField(string name,object value){typeof(RaceMenu).GetField(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(menu,value);}
  IEnumerator Run(){
   yield return new WaitForSecondsRealtime(1);
   director=FindAnyObjectByType<RaceDirector>();menu=director.GetComponent<RaceMenu>();Check(menu.ScreenState==RaceMenuScreen.Main&&!director.Started,"main-first");
   yield return Shot("01-main");menu.Open(RaceMenuScreen.Settings);yield return Shot("02-settings");MenuField("controlPage",1);yield return Shot("02-settings-gamepad");MenuField("controlPage",0);
   for(int i=0;i<4;i++)owned[i]=InputSystem.AddDevice<Gamepad>();
   var proofDevices=new int[4];for(int i=0;i<4;i++)proofDevices[i]=SessionControllers.Slot(owned[i]);
   menu.Open(RaceMenuScreen.LocalSetup);uint firstSeed=menu.Selected.Seed;menu.RollSeed();Check(menu.Selected.Seed!=firstSeed,"dice-changes-seed");
   while(menu.Selected.humans>0)menu.RemoveSeat(0);
   // Allow IMGUI to retire the removed name field before delivering a join press.
   yield return null;yield return null;
   var joining=owned[0];InputSystem.QueueStateEvent(joining,new GamepadState().WithButton(GamepadButton.North));yield return null;yield return null;
   Check(menu.Selected.humans==1&&menu.Selected.devices[0]==SessionControllers.Slot(joining),"Y-joins-pressing-pad");
   InputSystem.QueueStateEvent(joining,new GamepadState());yield return null;
   menu.RenameSeat(0,"ШШШШШШШШШШШШ");Check(menu.Selected.names[0].Length<=10&&menu.Skin.NameWidth(menu.Selected.names[0])<=190,"wide-name-fits-HUD");
   string playerName=menu.Selected.names[0];int token=menu.Selected.devices[0];InputSystem.RemoveDevice(joining);Check(!LocalInputRouter.CanBind(menu.Selected,out _),"disconnect-blocks-start");
   InputSystem.AddDevice(joining);Check(menu.Selected.names[0]==playerName&&SessionControllers.Resolve(token)==joining,"reconnect-retains-name");
   InputSystem.QueueStateEvent(joining,new GamepadState().WithButton(GamepadButton.North));yield return null;yield return null;
   Check(menu.Selected.humans==1,"Y-no-duplicate");InputSystem.QueueStateEvent(joining,new GamepadState());yield return null;
   for(int humans=1;humans<=4;humans++){
    menu.Open(RaceMenuScreen.LocalSetup);var chosen=menu.Selected;chosen.humans=humans;chosen.entrants=8;chosen.devices=(int[])proofDevices.Clone();chosen.theme=humans%2==0?"space-station":"cloud-city";chosen.rails="normal";chosen.jumps=false;chosen.seed="77";
    typeof(RaceMenu).GetField("seedText",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(menu,"77");
    chosen.Save();Check(LocalRaceConfig.Load().humans==humans,"saved-human-count-"+humans);
    if(humans==4){
     yield return Shot("03-four-player-setup");MenuField("focusRequest","PlayerName0");yield return Shot("03-name-focus");MenuField("focusRequest","");
     typeof(RaceMenu).GetMethod("ChooseDevice",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(menu,new object[]{0});yield return Shot("03-device-modal");
     typeof(RaceMenu).GetMethod("ClosePopup",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(menu,null);
    }
    int generation=director.RaceGeneration;menu.StartSelected();Check(director.Started&&director.RaceGeneration==generation+1,"single-start-"+humans);
    Check(director.HumanCount==humans&&director.Cars.Length==8,"applied-roster-"+humans);
    Check(director.Track.NearestRoad!=null&&director.Track.NearestRoad.TriangleCount>0,"staged-road-inventory-"+humans);
    foreach(var surface in director.Track.GetComponentsInChildren<TrackSurface>())Check(surface.owner==director.Track&&surface.buildRevision==director.Track.BuildRevision,"surface-ownership-"+humans);
    Check(director.Cars[0].HasProvenCheckpoint,"initial-checkpoint-"+humans);
    for(int seat=0;seat<4;seat++)Check(director.CameraFor(seat).GetComponent<Camera>().enabled==(seat<humans),"camera-enabled-"+humans+"-"+seat);
    yield return Shot("countdown-"+humans);yield return new WaitForSecondsRealtime(3.6f);
    Check(director.Session.Phase==RacePhase.Racing,"countdown-to-racing-"+humans);yield return Shot("race-"+humans);
    if(humans==1){
     var car=director.Cars[0];int revision=car.PositionRevision;car.Body.position=car.Frame.position-car.Frame.normal*40+car.Frame.right*(car.Frame.halfWidth+40);
     float recoveryDeadline=Time.realtimeSinceStartup+15;while(car.PositionRevision==revision&&Time.realtimeSinceStartup<recoveryDeadline)yield return null;
     Check(car.PositionRevision>revision,"real-fall-recovery");
    }
    director.SetPaused(true);double clock=director.Session.Elapsed;yield return new WaitForSecondsRealtime(.2f);Check(director.Session.Elapsed==clock,"pause-clock-"+humans);
    if(humans==4){
     yield return Shot("04-pause");menu.OpenPauseSettings();yield return Shot("04-pause-settings-keyboard");MenuField("controlPage",1);yield return Shot("04-pause-settings-gamepad");
     InputSystem.QueueStateEvent(owned[0],new GamepadState().WithButton(GamepadButton.East));yield return null;yield return null;
     Check(director.Paused&&!menu.PauseSettings,"B-returns-to-paused-root");InputSystem.QueueStateEvent(owned[0],new GamepadState());yield return null;MenuField("controlPage",0);
    }director.StartRace();Check(!director.Paused,"resume-"+humans);
    director.Restart();Check(director.Session.Phase==RacePhase.Countdown,"repeat-"+humans);director.SetPaused(true);Check(director.ExitToMenu(),"pause-return-"+humans);
    Check(!director.Started&&menu.ScreenState==RaceMenuScreen.LocalSetup&&director.TrackSeed==77&&menu.Selected.Seed!=77,"return-refreshes-draft-seed-"+humans);
   }
   // Run the unmodified AI and race rules to an actual crossing/finish window.
   var last=menu.Selected;last.humans=1;last.devices=new[]{-2,-1,0,1};last.theme="cloud-city";last.entrants=8;last.seed="77";typeof(RaceMenu).GetField("seedText",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(menu,"77");last.Save();menu.StartSelected();
   Check(director.Started,"natural-race-start");float deadline=Time.realtimeSinceStartup+600,nextProgress=Time.realtimeSinceStartup;
   while(director.Session.Phase!=RacePhase.Results&&Time.realtimeSinceStartup<deadline){
    if(Time.realtimeSinceStartup>=nextProgress){float leader=0;foreach(var racer in director.Session.Racers)leader=Mathf.Max(leader,racer.Progress);Debug.Log("CLOUDLINE_NATURAL_PROGRESS elapsed="+director.Session.Elapsed+" leader="+leader+" length="+director.Session.RaceLength);nextProgress=Time.realtimeSinceStartup+15;}
    // The opt-in automation can lose desktop focus while evidence is inspected.
    // Resume through the production path; do not synthesize finish/progress.
    if(director.Paused&&!director.Input.MissingDevice){Debug.Log("CLOUDLINE_PROOF_RESUME_AFTER_FOCUS_LOSS");director.StartRace();}
    yield return null;
   }
   Check(director.Session.Phase==RacePhase.Results,"natural-results");bool aiFinished=false;
   for(int i=1;i<director.Cars.Length;i++)aiFinished|=director.Session.Racers[i].Finished;
   Check(aiFinished,"natural-ai-finish");yield return Shot("05-natural-results");
   Check(director.ExitToMenu(),"results-return");yield return Shot("06-return-setup");
  }
  void OnApplicationQuit(){Restore();}
  void OnDestroy(){Restore();}
  void Restore(){if(!backupCaptured)return;backupCaptured=false;if(hadPrevious)PlayerPrefs.SetString(LocalRaceConfig.PreferenceKey,previous);else PlayerPrefs.DeleteKey(LocalRaceConfig.PreferenceKey);PlayerPrefs.Save();foreach(var pad in owned)if(pad!=null&&pad.added)InputSystem.RemoveDevice(pad);}
 }
}

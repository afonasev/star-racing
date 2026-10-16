using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
namespace StarRacingPrototype {
 [InitializeOnLoad] public static class DesignLabPlayModeChecks {
  const string Key="StarRacing.DesignLabQA";const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
  static InputDevice[] disabled;
  static int tick,lastFrame=-1,assertions;static double start;static GameObject fixture;static EditorWindow view;static Gamepad pad;static RaceMenu menu;static RaceDirector director;static LabLibrary lab;static string own,hash,previousPath,previousRace;static UserSettings previousPrefs;static int vsync,target;static bool hadRace;
  static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_LAB_QA_DIR");
  static DesignLabPlayModeChecks(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run(){if(string.IsNullOrEmpty(Output))throw new Exception("STAR_RACING_LAB_QA_DIR required");Directory.CreateDirectory(Output);SessionState.SetBool(Key+"LongNames",false);SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",false);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);view=EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"),false,"Game",false);view.position=new Rect(0,0,1600,940);view.Show();EditorApplication.isPlaying=true;}
  public static void RunLongNames(){Run();SessionState.SetBool(Key+"LongNames",true);}
  static void Changed(PlayModeStateChange state){if(!SessionState.GetBool(Key,false))return;File.AppendAllText(Path.Combine(Output,"progress.txt"),"state="+state+" tick="+tick+" failed="+SessionState.GetBool(Key+"Failed",false)+"\n");if(state==PlayModeStateChange.EnteredPlayMode){tick=assertions=0;lastFrame=-1;start=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;}if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",false)?1:0);}}
  static void Check(bool value,string text){assertions++;if(!value)throw new Exception(text);}
  static void Set(string name,object value)=>typeof(RaceMenu).GetField(name,Private).SetValue(menu,value);
  static void Action(string id){foreach(var c in (IEnumerable)typeof(RaceMenu).GetField("controls",Private).GetValue(menu)){if((string)c.GetType().GetField("id").GetValue(c)==id){((System.Action)c.GetType().GetField("action").GetValue(c))();return;}}throw new Exception("Missing action "+id);}
  static bool Has(string id){foreach(var c in (IEnumerable)typeof(RaceMenu).GetField("controls",Private).GetValue(menu))if((string)c.GetType().GetField("id").GetValue(c)==id)return true;return false;}
  static void Capture(string name)=>ScreenCapture.CaptureScreenshot(Path.Combine(Output,name+".png"));
  static void Close()=>typeof(RaceMenu).GetMethod("LabClose",Private).Invoke(menu,null);
  static void BeginRace(){var config=new LocalRaceConfig{humans=1,entrants=8,theme="cloud-city",seed="77"};config.devices[0]=SessionControllers.Slot(pad);Check(director.TryStartSelected(config,out var error),"race start "+error);}
  static void Tick(){try{
   if(EditorApplication.timeSinceStartup-start>180)throw new Exception("Lab PlayMode timeout");EditorApplication.QueuePlayerLoopUpdate();if(view!=null)view.Repaint();if(Time.frameCount==lastFrame)return;lastFrame=Time.frameCount;tick++;if(tick%10==0)File.AppendAllText(Path.Combine(Output,"progress.txt"),"tick="+tick+" frame="+Time.frameCount+"\n");
   if(tick==1){
    previousPath=UserSettingsStore.OverridePath;previousPrefs=UserSettingsStore.Current;vsync=QualitySettings.vSyncCount;target=Application.targetFrameRate;hadRace=PlayerPrefs.HasKey(LocalRaceConfig.PreferenceKey);previousRace=PlayerPrefs.GetString(LocalRaceConfig.PreferenceKey,"");
    UserSettingsStore.OverridePath=Path.Combine(Output,"preferences.json");UserSettingsStore.Replace(new UserSettings());
    string store=Path.Combine(Output,"author");string source=Path.GetFullPath(Path.Combine(Application.dataPath,"../../design-lab"));foreach(var file in Directory.GetFiles(source,"*.json",SearchOption.AllDirectories)){string relative=file.Substring(source.Length+1);string dest=Path.Combine(store,relative);Directory.CreateDirectory(Path.GetDirectoryName(dest));File.Copy(file,dest);}
    lab=new LabLibrary(DesignLabRuntime.Package(),new LabFileStore(store),true);DesignLabRuntime.OverrideFactory=()=>lab;DesignLabRuntime.Reload();
    disabled=InputSystem.devices.Where(d=>(d is Mouse||d is Keyboard)&&d.enabled).ToArray();foreach(var device in disabled)InputSystem.DisableDevice(device);
    pad=InputSystem.AddDevice<Gamepad>();fixture=new GameObject("Design Lab fixture");director=fixture.AddComponent<RaceDirector>();menu=fixture.GetComponent<RaceMenu>();Check(fixture.GetComponent<RaceAudioCoordinator>()==null&&SilentStartup.Enabled,"silent startup initialized sound");
   }
   if(SessionState.GetBool(Key+"LongNames",false)&&tick>1){
    if(tick==10){string id=lab.ReleaseDefault().ProfileId;lab.Copy(id,1,new string('W',40));lab.Copy(id,1,new string('W',40));UserSettingsStore.Current.textScale=1.1f;menu.Open(RaceMenuScreen.Lab);}
    if(tick==20)Capture("long-name-root");
    if(tick==30)Action("lab-profile");
    if(tick==35){string previous=lab.Active.ProfileId;var broken=lab.Profiles.Skip(1).First();File.WriteAllText(Path.Combine(Output,"author",broken.id,"1.json"),"broken");Action("lab-choice-1");Check(lab.Active.ProfileId==previous&&menu.LabDialog=="profiles","failed activation replaced active selection or closed error context");Check(((string)typeof(RaceMenu).GetField("labMessage",Private).GetValue(menu)).Contains("повреждён"),"damaged-version UI exposes parser text instead of actionable message");}
    if(tick==40)Capture("long-name-profiles");
    if(tick==50)Close();
    if(tick==60)Action("lab-manage");
    if(tick==70)Capture("long-name-manage");
    if(tick==80)Close();
    if(tick==90)Action("lab-copy");
    if(tick==100)Capture("long-name-copy");
    if(tick==110){Check(lab.Selected.ProfileName.Length==40,"valid profile name truncated in data");foreach(string name in new[]{"long-name-root","long-name-profiles","long-name-manage","long-name-copy"})Check(new FileInfo(Path.Combine(Output,name+".png")).Length>1000,"missing long-name image");File.WriteAllText(Path.Combine(Output,"result.json"),"{\"success\":true,\"assertions\":"+assertions+",\"silent\":true}");Debug.Log("DESIGN_LAB_LONG_NAMES_OK assertions="+assertions);Finish(false);}return;
   }
   if(tick==20)Action("lab");
   if(tick==30){Capture("lab-default");Check(menu.ScreenState==RaceMenuScreen.Lab,"Lab route unavailable");}
   if(tick==40)Action("lab-copy");
   if(tick==50)Capture("lab-copy");
   if(tick==60){Action("lab-name-save");own=lab.Selected.ProfileId;Check(own!=lab.ReleaseDefault().ProfileId&&lab.Selected.Version==1,"copy failed");}
   if(tick==70){lab.Draft.Inputs["baseSpeedKmh"]="310";Set("focus","lab-field-baseSpeedKmh");}
   if(tick==80)Capture("lab-draft");
   if(tick==90)menu.Open(RaceMenuScreen.Main);
   if(tick==100){Capture("lab-dirty");Check(menu.ScreenState==RaceMenuScreen.Lab&&menu.LabDialog=="dirty","dirty transition unguarded");}
   if(tick==110)InputSystem.QueueStateEvent(pad,new GamepadState().WithButton(GamepadButton.East));
   if(tick==115){InputSystem.QueueStateEvent(pad,new GamepadState());Check(menu.LabDialog==null&&lab.Draft.Dirty,"pad cancel lost draft");Check((string)typeof(RaceMenu).GetField("focus",Private).GetValue(menu)=="lab-field-baseSpeedKmh","dialog cancel lost invoking focus");}
   if(tick==120)menu.Open(RaceMenuScreen.Main);
   if(tick==130){Action("lab-dirty-confirm");Check(menu.ScreenState==RaceMenuScreen.Main&&!lab.Draft.Dirty,"dirty confirm failed");}
   if(tick==140)menu.Open(RaceMenuScreen.Lab);
   if(tick==142)Set("focus","lab-search");
   if(tick==143)InputSystem.QueueStateEvent(pad,new GamepadState().WithButton(GamepadButton.South));
   if(tick==145)InputSystem.QueueStateEvent(pad,new GamepadState());
   if(tick==150){Check(menu.LabDialog=="search","pad search keyboard unavailable");Set("labText","nitro");}
   if(tick==153)Action("lab-name-save");
   if(tick==160){Capture("lab-search");Check(Has("lab-field-aiNitroReserve"),"global search missed AI group");}
   if(tick==170)Set("labQuery","неттакогопараметра");
   if(tick==180)Capture("lab-empty");
   if(tick==190){Set("labQuery","");Set("labGroup",LabParameters.Groups[0]);lab.Draft.Inputs["baseSpeedKmh"]="700";}
   if(tick==210){Capture("lab-invalid");Check(!Has("lab-save")&&lab.Draft.Inputs["baseSpeedKmh"]=="700","invalid input saved or lost");}
   if(tick==220)Action("lab-field-baseSpeedKmh");
   if(tick==230)Capture("lab-number");
   if(tick==240){Action("lab-key-13");Action("lab-key-2");Action("lab-key-0");Action("lab-key-9");Check(lab.Draft.Inputs["baseSpeedKmh"]=="310","numeric keypad input failed");}
   if(tick==250)Action("lab-number-done");
   if(tick==260){Action("lab-save");Check(lab.Selected.Version==2&&lab.Active.ProfileId==own,"save did not activate new version");}
   if(tick==270)Action("lab-manage");
   if(tick==280)Capture("lab-manage");
   if(tick==283)Action("lab-rename");
   if(tick==286){Close();Check(menu.LabDialog=="manage","nested Back skipped management layer");}
   if(tick==290)Close();
   if(tick==300)Action("lab-version");
   if(tick==310)Capture("lab-versions");
   if(tick==320){Close();UserSettingsStore.Current.textScale=1.1f;Set("labGroup",LabParameters.Groups[1]);Set("focus","lab-group-1");}
   if(tick==330){Capture("lab-large");foreach(var group in LabParameters.Groups)Check(menu.Skin.LabelWidth(RaceMenu.LabSidebarLabel(group,11),18)<230,"sidebar label does not fit "+group);}
   if(tick==340){UserSettingsStore.Current.textScale=1;File.AppendAllText(Path.Combine(Output,"progress.txt"),"before lab-test\n");Action("lab-test");File.AppendAllText(Path.Combine(Output,"progress.txt"),"after lab-test screen="+menu.ScreenState+" dialog="+menu.LabDialog+"\n");BeginRace();File.AppendAllText(Path.Combine(Output,"progress.txt"),"after BeginRace\n");hash=director.Balance.Hash;Check(director.RaceBalanceSnapshot.ProfileId==own&&director.RaceBalanceSnapshot.Version==2&&director.Balance["baseSpeedKmh"]==310,"selected balance not used");}
   if(tick==350){director.SetPaused(true);menu.Open(RaceMenuScreen.Lab);Check(menu.ScreenState!=RaceMenuScreen.Lab,"Lab accessible during race");lab.Reset();Check(director.Balance.Hash==hash&&director.RaceBalanceSnapshot.Version==2,"active selection mutated current race");}
   if(tick==360)Capture("race-profile");
   if(tick==370){director.Restart();Check(director.Balance.Hash==hash&&director.RaceBalanceSnapshot.Version==2,"Repeat recaptured active version");director.SetPaused(true);}
   if(tick==380){director.ExitToMenu();menu.Open(RaceMenuScreen.Lab);Check(menu.ScreenState==RaceMenuScreen.Lab,"return to Lab failed");}
   if(tick==385){var invalid=new LocalRaceConfig{humans=0};Check(!director.TryStartSelected(invalid,out _),"invalid race started");Check(director.Balance.Hash==hash&&director.RaceBalanceSnapshot.Version==2,"failed start published new balance");}
   if(tick==390){Action("lab-test");BeginRace();Check(director.RaceBalanceSnapshot.Version==1&&director.Balance["baseSpeedKmh"]==300,"next race did not capture active Default");director.SetPaused(true);}
   if(tick==400){director.ExitToMenu();FairAiChecks.RunContracts(Output);RaceHudChecks.Run();}
   if(tick==410){DesignLabRuntime.OverrideFactory=()=>new LabLibrary(DesignLabRuntime.Package(),new LabFileStore(Path.Combine(Output,"production-own")),false);DesignLabRuntime.Reload();menu.Open(RaceMenuScreen.Lab);}
   if(tick==420){Capture("lab-production");Check(!Has("lab-field-baseSpeedKmh")&&Has("lab-copy"),"production UI protection");}
   if(tick==430){string path=Path.Combine(Output,"author");var author=new LabLibrary(DesignLabRuntime.Package(),new LabFileStore(path),true);author.Mark(author.ReleaseDefault().ProfileId,1,false);DesignLabRuntime.OverrideFactory=()=>new LabLibrary(DesignLabRuntime.Package(),new LabFileStore(path),true);DesignLabRuntime.Reload();menu.Open(RaceMenuScreen.LocalSetup);Check(!menu.CanStartSelected,"startup without marked Default can start race");}
   if(tick==440)Capture("no-release-default");
   if(tick==450){menu.Open(RaceMenuScreen.Lab);Check(menu.ScreenState==RaceMenuScreen.Lab&&menu.Laboratory.Active==null,"Lab unavailable to repair missing release Default");menu.Laboratory.Select(menu.Laboratory.Profiles.First().id,1);menu.Laboratory.Mark(menu.Laboratory.Selected.ProfileId,1,true);menu.Laboratory.Reset();Check(menu.Laboratory.Active.Version==1,"release Default repair failed");}
   if(tick==470){foreach(string name in new[]{"lab-default","lab-copy","lab-draft","lab-dirty","lab-search","lab-empty","lab-invalid","lab-number","lab-manage","lab-versions","lab-large","race-profile","lab-production","no-release-default"})Check(new FileInfo(Path.Combine(Output,name+".png")).Length>1000,"missing PNG "+name);File.WriteAllText(Path.Combine(Output,"result.json"),"{\"success\":true,\"assertions\":"+assertions+",\"silent\":true,\"playerBuild\":false}");Debug.Log("DESIGN_LAB_PLAYMODE_OK assertions="+assertions);Finish(false);}
  }catch(Exception e){Debug.LogException(e);File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());Finish(true);}}
  static void Finish(bool failed){File.AppendAllText(Path.Combine(Output,"progress.txt"),"Finish failed="+failed+" tick="+tick+"\n");EditorApplication.update-=Tick;SessionState.SetBool(Key+"Failed",failed);if(fixture!=null)UnityEngine.Object.Destroy(fixture);if(pad!=null&&pad.added)InputSystem.RemoveDevice(pad);if(disabled!=null)foreach(var device in disabled)if(device.added&&!device.enabled)InputSystem.EnableDevice(device);DesignLabRuntime.OverrideFactory=null;DesignLabRuntime.Reload();UserSettingsStore.OverridePath=previousPath;if(previousPrefs!=null)UserSettingsStore.Replace(previousPrefs);QualitySettings.vSyncCount=vsync;Application.targetFrameRate=target;if(hadRace)PlayerPrefs.SetString(LocalRaceConfig.PreferenceKey,previousRace);else PlayerPrefs.DeleteKey(LocalRaceConfig.PreferenceKey);PlayerPrefs.Save();Time.timeScale=1;EditorApplication.isPlaying=false;}
 }
}

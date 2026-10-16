using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace StarRacingPrototype {
 [InitializeOnLoad]
 public static class StandardSettingsPlayModeChecks {
  const string Key="StarRacing.StandardSettingsQA";
  const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
  static int ticks,assertions,lastFrame=-1;static double started;static GameObject fixture;static EditorWindow view;static Gamepad pad;static RaceMenu menu;
  static string previousPath;static UserSettings previous;static int previousVsync,previousTarget;
  static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_SETTINGS_QA_DIR");
  static StandardSettingsPlayModeChecks(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run(){
   if(string.IsNullOrEmpty(Output))throw new Exception("STAR_RACING_SETTINGS_QA_DIR required");
   Directory.CreateDirectory(Output);SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",false);
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
   view=EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"),false,"Game",false);view.position=new Rect(0,0,1600,940);view.Show();EditorApplication.isPlaying=true;
  }
  static void Changed(PlayModeStateChange state){
   if(!SessionState.GetBool(Key,false))return;
   if(state==PlayModeStateChange.EnteredPlayMode){ticks=assertions=0;lastFrame=-1;started=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;}
   if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",false)?1:0);}
  }
  static void Check(bool condition,string text){assertions++;if(!condition)throw new Exception(text);}
  static void Section(int value)=>typeof(RaceMenu).GetField("settingsPage",Private).SetValue(menu,value);
  static void Press(GamepadButton button)=>InputSystem.QueueStateEvent(pad,new GamepadState().WithButton(button));
  static void Release()=>InputSystem.QueueStateEvent(pad,new GamepadState());
  static void Capture(string name)=>ScreenCapture.CaptureScreenshot(Path.Combine(Output,name+".png"));
  static void Action(string id){
   foreach(var control in (IEnumerable)typeof(RaceMenu).GetField("controls",Private).GetValue(menu)){
    var type=control.GetType();if((string)type.GetField("id").GetValue(control)==id){((System.Action)type.GetField("action").GetValue(control))();return;}
   }
   throw new Exception("Missing menu action "+id);
  }
  static void Tick(){
   try{
    if(EditorApplication.timeSinceStartup-started>180)throw new Exception("Settings PlayMode timeout");
    EditorApplication.QueuePlayerLoopUpdate();if(view!=null)view.Repaint();
    if(Time.frameCount==lastFrame)return;lastFrame=Time.frameCount;ticks++;
    if(ticks==1){
     previousPath=UserSettingsStore.OverridePath;previous=UserSettingsStore.Current;previousVsync=QualitySettings.vSyncCount;previousTarget=Application.targetFrameRate;
     UserSettingsStore.OverridePath=Path.Combine(Output,"preferences.json");UserSettingsStore.Replace(new UserSettings{master=.6f,music=.35f});UserSettingsStore.Save();
     pad=InputSystem.AddDevice<Gamepad>();fixture=new GameObject("Standard settings UI fixture");var director=fixture.AddComponent<RaceDirector>();menu=director.GetComponent<RaceMenu>();
     Check(SilentStartup.Enabled&&fixture.GetComponent<RaceAudioCoordinator>()==null,"silent runtime initialized audio");
    }
    if(ticks==10){typeof(RaceMenu).GetField("focus",Private).SetValue(menu,"settings");Action("settings");Section(2);}
    if(ticks==30){Capture("audio");Check(menu.Preferences.master==.6f&&menu.Preferences.music==.35f,"audio settings unavailable without coordinator");}
    if(ticks==40)Press(GamepadButton.North);
    if(ticks==50){Check(menu.SettingsModalOpen,"upper button did not open reset");Capture("reset");}
    if(ticks==60)Release();
    if(ticks==70)Press(GamepadButton.South);
    if(ticks==80){Check(!menu.SettingsModalOpen&&menu.Preferences.master==.6f,"cancel did not preserve preferences");Release();}
    if(ticks==90)Action("reset-all");
    if(ticks==100){Action("reset-confirm");Check(menu.Preferences.master==1&&menu.Preferences.music==1,"confirmed reset failed");Section(0);}
    if(ticks==110)Capture("screen");
    if(ticks==120)Action("fullscreen");
    if(ticks==130){Capture("display-confirmation");Check(menu.DisplayConfirmationPending&&menu.Preferences.display.fullscreen&&!menu.Display.fullscreen,"temporary display mode leaked to preferences");Check(UserSettings.Parse(File.ReadAllText(UserSettingsStore.PathName)).display.fullscreen,"unconfirmed display mode was saved");}
    if(ticks==140)Press(GamepadButton.East);
    if(ticks==150){Check(!menu.DisplayConfirmationPending&&menu.Display.fullscreen,"cancel did not restore screen");Release();Section(1);}
    if(ticks==165)Capture("graphics");
    if(ticks==175)Section(3);
    if(ticks==190)Capture("input");
    if(ticks==200)Section(4);
    if(ticks==215)Capture("interface");
    if(ticks==216){menu.Preferences.textScale=1.1f;menu.Preferences.highContrast=true;Section(1);}
    if(ticks==220)Capture("graphics-large-contrast");
    if(ticks==224){menu.Preferences.textScale=1;menu.Preferences.highContrast=false;Section(4);}
    if(ticks==225)Action("controls-help");
    if(ticks==240){Capture("help");Check(menu.SettingsModalOpen,"help route not modal");}
    if(ticks==245)Press(GamepadButton.East);
    if(ticks==250){Check(!menu.SettingsModalOpen,"back did not close help first");Release();menu.Open(RaceMenuScreen.Main);Check((string)typeof(RaceMenu).GetField("focus",Private).GetValue(menu)=="settings","return lost invoking main focus");}
    if(ticks==270){
     foreach(string name in new[]{"audio","reset","screen","display-confirmation","graphics","input","interface","help","graphics-large-contrast"})Check(new FileInfo(Path.Combine(Output,name+".png")).Length>1000,"missing screenshot "+name);
     File.WriteAllText(Path.Combine(Output,"result.json"),"{\"success\":true,\"assertions\":"+assertions+",\"silent\":true,\"playerBuild\":false}\n");
     Debug.Log("STANDARD_SETTINGS_PLAYMODE_OK assertions="+assertions);Finish(false);
    }
   }catch(Exception e){Debug.LogException(e);File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());Finish(true);}
  }
  static void Finish(bool failed){
   EditorApplication.update-=Tick;SessionState.SetBool(Key+"Failed",failed);
   if(pad!=null&&pad.added)InputSystem.RemoveDevice(pad);if(fixture!=null)UnityEngine.Object.Destroy(fixture);
   UserSettingsStore.OverridePath=previousPath;if(previous!=null)UserSettingsStore.Replace(previous);QualitySettings.vSyncCount=previousVsync;Application.targetFrameRate=previousTarget;
   Time.timeScale=1;EditorApplication.isPlaying=false;
  }
 }
}

using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace StarRacingPrototype {
 [InitializeOnLoad]
 public static class DisplaySettingsPlayModeChecks {
  const string Key="StarRacing.DisplayChecks";
  static int ticks;static double started;static GameObject preview;static EditorWindow view;
  static bool hadPrefs;static string prefs;static float volume;
  static int previousVSync,previousTarget;static string previousSettingsPath;static UserSettings previousSettings;
  static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_MENU_QA_DIR");
  static DisplaySettingsPlayModeChecks(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run(){
   if(string.IsNullOrEmpty(Output))throw new Exception("STAR_RACING_MENU_QA_DIR required");
   Directory.CreateDirectory(Output);SessionState.SetBool(Key+"Failed",false);SessionState.SetBool(Key,true);
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
   view=EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"),false,"Game",false);view.position=new Rect(0,0,1600,940);view.Show();EditorApplication.isPlaying=true;
  }
  static void Changed(PlayModeStateChange state){
   if(!SessionState.GetBool(Key,false))return;
   if(state==PlayModeStateChange.EnteredPlayMode){ticks=0;started=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;}
   if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",false)?1:0);}
  }
  static void Tick(){
   try{
    if(EditorApplication.timeSinceStartup-started>120)throw new Exception("Play Mode display checks timed out");
    EditorApplication.QueuePlayerLoopUpdate();if(view!=null)view.Repaint();ticks++;
    if(ticks==1){
     previousSettingsPath=UserSettingsStore.OverridePath;previousSettings=UserSettingsStore.Current;UserSettingsStore.OverridePath=Path.Combine(Output,"preferences.json");UserSettingsStore.Replace(new UserSettings());
     hadPrefs=PlayerPrefs.HasKey(DisplaySettings.PreferenceKey);prefs=PlayerPrefs.GetString(DisplaySettings.PreferenceKey);PlayerPrefs.DeleteKey(DisplaySettings.PreferenceKey);volume=AudioListener.volume;AudioListener.volume=0;
     previousVSync=QualitySettings.vSyncCount;previousTarget=Application.targetFrameRate;
     new DisplaySettings{vSync=true}.Save();
     new GameObject("Settings QA camera").AddComponent<Camera>();
     preview=new GameObject("Display settings preview");var director=preview.AddComponent<RaceDirector>();director.GetComponent<RaceMenu>().Open(RaceMenuScreen.Settings);
     if(!director.GetComponent<RaceMenu>().Display.vSync||QualitySettings.vSyncCount!=1||Application.targetFrameRate!=120)throw new Exception("startup did not apply saved VSync");
    }
    if(ticks==25)ScreenCapture.CaptureScreenshot(Path.Combine(Output,"settings.png"));
    if(ticks==40){
     var menu=preview.GetComponent<RaceMenu>();Invoke(menu,"fullscreen");
     if(menu.Display.fullscreen||!menu.DisplayConfirmationPending||!UserSettingsStore.Current.display.fullscreen)throw new Exception("screen mode was not temporary");
    }
    if(ticks==50){Invoke(preview.GetComponent<RaceMenu>(),"display-confirm");}
    if(ticks==60){
     var menu=preview.GetComponent<RaceMenu>();if(menu.Display.fullscreen||UserSettings.Parse(File.ReadAllText(UserSettingsStore.PathName)).display.fullscreen)throw new Exception("confirmed screen mode did not persist");
     Invoke(menu,"vsync");
     if(menu.Display.vSync||UserSettings.Parse(File.ReadAllText(UserSettingsStore.PathName)).display.vSync||QualitySettings.vSyncCount!=0||Application.targetFrameRate!=120)throw new Exception("main menu VSync toggle did not apply and persist");
     Invoke(menu,"resolution");if(!menu.PopupOpen)throw new Exception("resolution popup did not open");
    }
    if(ticks==70)ScreenCapture.CaptureScreenshot(Path.Combine(Output,"resolution-popup.png"));
    if(ticks==80){
     var menu=preview.GetComponent<RaceMenu>();Invoke(menu,"option-1");
     if(menu.PopupOpen||menu.Display.width<=0||!menu.DisplayConfirmationPending)throw new Exception("resolution choice did not preview");
    }
    if(ticks==90)Invoke(preview.GetComponent<RaceMenu>(),"display-confirm");
    if(ticks==100){
     var menu=preview.GetComponent<RaceMenu>();if(UserSettings.Parse(File.ReadAllText(UserSettingsStore.PathName)).display.width!=menu.Display.width)throw new Exception("confirmed resolution did not persist");
     menu.OpenPauseSettings();if(!menu.PauseSettings)throw new Exception("pause settings not available");
     var director=preview.GetComponent<RaceDirector>();director.enabled=false;typeof(RaceDirector).GetProperty("Started").SetValue(director,true);
    }
    if(ticks==110)ScreenCapture.CaptureScreenshot(Path.Combine(Output,"pause-settings.png"));
    if(ticks==120){
     var menu=preview.GetComponent<RaceMenu>();Invoke(menu,"vsync");
     if(!menu.Display.vSync||!UserSettings.Parse(File.ReadAllText(UserSettingsStore.PathName)).display.vSync||QualitySettings.vSyncCount!=1||!preview.GetComponent<RaceDirector>().Paused)throw new Exception("pause VSync toggle did not apply and persist while paused");
     Invoke(menu,"resolution");
    }
    if(ticks==130)ScreenCapture.CaptureScreenshot(Path.Combine(Output,"pause-resolution-popup.png"));
    if(ticks==140){
     var menu=preview.GetComponent<RaceMenu>();
     if(!menu.PopupOpen||!menu.ConsumePauseSettingsBack(true)||menu.PopupOpen||!menu.PauseSettings)throw new Exception("pause popup back failed");
     menu.ConsumePauseSettingsBack(true);if(!menu.PauseSettings)throw new Exception("same back press escaped two layers");
    }
    if(ticks==150){
     var menu=preview.GetComponent<RaceMenu>();menu.ConsumePauseSettingsBack(true);if(menu.PauseSettings||!preview.GetComponent<RaceDirector>().Paused)throw new Exception("pause settings back resumed race");
     foreach(var go in SceneManager.GetActiveScene().GetRootGameObjects())UnityEngine.Object.DestroyImmediate(go);
     UserSettingsStore.Reload();preview=new GameObject("Reload settings preview");var reloaded=preview.AddComponent<RaceDirector>();
     if(!reloaded.GetComponent<RaceMenu>().Display.vSync||QualitySettings.vSyncCount!=1||Application.targetFrameRate!=120)throw new Exception("reinitialization overrode saved VSync");
     foreach(var go in SceneManager.GetActiveScene().GetRootGameObjects())UnityEngine.Object.DestroyImmediate(go);
     DisplaySettingsChecks.Run();File.WriteAllText(Path.Combine(Output,"playmode.txt"),"DISPLAY_SETTINGS_PLAYMODE_OK\n");Debug.Log("DISPLAY_SETTINGS_PLAYMODE_OK");Finish(false);
    }

   }catch(Exception e){Debug.LogException(e);File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());Finish(true);}
  }
  static void Invoke(RaceMenu menu,string id){
   var controls=(System.Collections.IEnumerable)typeof(RaceMenu).GetField("controls",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(menu);
   foreach(var control in controls){var type=control.GetType();if((string)type.GetField("id").GetValue(control)==id){((Action)type.GetField("action").GetValue(control))();return;}}
   throw new Exception("Missing menu control: "+id);
  }
  static void Finish(bool error){
   EditorApplication.update-=Tick;SessionState.SetBool(Key+"Failed",error);
   if(hadPrefs)PlayerPrefs.SetString(DisplaySettings.PreferenceKey,prefs);else PlayerPrefs.DeleteKey(DisplaySettings.PreferenceKey);PlayerPrefs.Save();AudioListener.volume=volume;Time.timeScale=1;EditorApplication.isPlaying=false;
   UserSettingsStore.OverridePath=previousSettingsPath;if(previousSettings!=null)UserSettingsStore.Replace(previousSettings);
   QualitySettings.vSyncCount=previousVSync;Application.targetFrameRate=previousTarget;
  }
 }
}

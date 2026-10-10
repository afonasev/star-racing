using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace StarRacingPrototype {
 [InitializeOnLoad]
 public static class GamepadStartPlayModeChecks {
  const string Key="StarRacing.StartXChecks";
  static int ticks;static double started;static GameObject preview;
  static bool hadPrefs;static string prefs;static float volume;
  static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_MENU_QA_DIR");
  static GamepadStartPlayModeChecks(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run(){
   if(string.IsNullOrEmpty(Output))throw new Exception("STAR_RACING_MENU_QA_DIR required");
   Directory.CreateDirectory(Output);SessionState.SetBool(Key+"Failed",false);SessionState.SetBool(Key,true);
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.isPlaying=true;
  }
  static void Changed(PlayModeStateChange state){
   if(!SessionState.GetBool(Key,false))return;
   if(state==PlayModeStateChange.EnteredPlayMode){ticks=0;started=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;}
   if(state==PlayModeStateChange.EnteredEditMode){
    if(!SessionState.GetBool(Key+"Failed",false))try{GamepadStartChecks.Run();}catch(Exception e){Debug.LogException(e);SessionState.SetBool(Key+"Failed",true);}
    SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",false)?1:0);
   }
  }
  static void Tick(){
   try{
    if(EditorApplication.timeSinceStartup-started>120)throw new Exception("Play Mode shortcut checks timed out");
    ticks++;
    if(ticks==1){
     hadPrefs=PlayerPrefs.HasKey(LocalRaceConfig.PreferenceKey);prefs=PlayerPrefs.GetString(LocalRaceConfig.PreferenceKey);volume=AudioListener.volume;AudioListener.volume=0;
     new LocalRaceConfig{humans=1,entrants=8,devices=new[]{-2,-1,-2,-1},theme="cloud-city",jumps=false}.Save();
     preview=new GameObject("Start X preview");var director=preview.AddComponent<RaceDirector>();director.GetComponent<RaceMenu>().Open(RaceMenuScreen.LocalSetup);
    }
    if(ticks==25)ScreenCapture.CaptureScreenshot(Path.Combine(Output,"setup-x.png"));
    if(ticks==55){
     foreach(var go in SceneManager.GetActiveScene().GetRootGameObjects())UnityEngine.Object.DestroyImmediate(go);
     // Synchronous input contracts run after leaving Play Mode; loading lifecycle has its own asynchronous fixture.
     File.WriteAllText(Path.Combine(Output,"playmode.txt"),"GAMEPAD_START_PLAYMODE_OK\n");Debug.Log("GAMEPAD_START_PLAYMODE_OK");Finish(false);
    }
   }catch(Exception e){Debug.LogException(e);File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());Finish(true);}
  }
  static void Finish(bool error){
   EditorApplication.update-=Tick;SessionState.SetBool(Key+"Failed",error);
   if(hadPrefs)PlayerPrefs.SetString(LocalRaceConfig.PreferenceKey,prefs);else PlayerPrefs.DeleteKey(LocalRaceConfig.PreferenceKey);PlayerPrefs.Save();AudioListener.volume=volume;Time.timeScale=1;EditorApplication.isPlaying=false;
  }
 }
}

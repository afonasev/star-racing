using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StarRacingPrototype {
 [InitializeOnLoad]
 public static class NitroBlurSettingChecks {
  const string Key="StarRacing.NitroBlurSettingChecks";
  static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
  static int ticks,assertions;
  static double started;
  static bool hadPrefs,hadDisplayPrefs,prepared;
  static int prefs;
  static string displayPrefs;
  static float volume;
  static RaceDirector director;
  static EditorWindow view;
  static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_NITRO_SETTING_QA_DIR");
  static NitroBlurSettingChecks(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run(){
   if(string.IsNullOrEmpty(Output))throw new Exception("STAR_RACING_NITRO_SETTING_QA_DIR required");
   Directory.CreateDirectory(Output);SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",true);
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
   view=EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"),false,"Game",false);view.position=new Rect(0,0,1600,940);view.Show();EditorApplication.isPlaying=true;
  }
  static void Changed(PlayModeStateChange state){
   if(!SessionState.GetBool(Key,false))return;
   if(state==PlayModeStateChange.EnteredPlayMode){ticks=assertions=0;prepared=false;started=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;}
   if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",true)?1:0);}
  }
  static void Check(bool value,string message){assertions++;if(!value)throw new Exception("Nitro setting: "+message);}
  static void Invoke(RaceMenu menu,string id){
   var controls=(System.Collections.IEnumerable)typeof(RaceMenu).GetField("controls",Private).GetValue(menu);
   foreach(var control in controls)if((string)control.GetType().GetField("id").GetValue(control)==id){((Action)control.GetType().GetField("action").GetValue(control))();return;}
   throw new Exception("Missing settings control "+id);
  }
  static void Tick(){
   try{
    if(EditorApplication.timeSinceStartup-started>120)throw new Exception("Nitro setting PlayMode timed out");
    EditorApplication.QueuePlayerLoopUpdate();if(view!=null)view.Repaint();ticks++;
    if(ticks==1){
     hadPrefs=PlayerPrefs.HasKey(NitroBlurSettings.PreferenceKey);prefs=PlayerPrefs.GetInt(NitroBlurSettings.PreferenceKey);
     hadDisplayPrefs=PlayerPrefs.HasKey(DisplaySettings.PreferenceKey);displayPrefs=PlayerPrefs.GetString(DisplaySettings.PreferenceKey);volume=AudioListener.volume;prepared=true;
     PlayerPrefs.DeleteKey(NitroBlurSettings.PreferenceKey);NitroBlurSettings.Reload();Check(NitroBlurSettings.Enabled,"default enabled");AudioListener.volume=0;
     new GameObject("settings camera").AddComponent<Camera>();director=new GameObject("settings fixture").AddComponent<RaceDirector>();director.GetComponent<RaceMenu>().Open(RaceMenuScreen.Settings);
    }
    if(ticks==25)ScreenCapture.CaptureScreenshot(Path.Combine(Output,"settings-enabled.png"));
    if(ticks==35){
     var menu=director.GetComponent<RaceMenu>();
     typeof(RaceMenu).GetField("focus",Private).SetValue(menu,"fps");typeof(RaceMenu).GetMethod("Move",Private).Invoke(menu,new object[]{1});
     Check((string)typeof(RaceMenu).GetField("focus",Private).GetValue(menu)=="nitro-blur","navigation reaches toggle");
     Invoke(menu,"nitro-blur");Check(!NitroBlurSettings.Enabled&&PlayerPrefs.GetInt(NitroBlurSettings.PreferenceKey,-1)==0,"main toggle persists off");
     NitroBlurSettings.Reload();Check(!NitroBlurSettings.Enabled,"fresh load stays off");
    }
    if(ticks==45)ScreenCapture.CaptureScreenshot(Path.Combine(Output,"settings-disabled.png"));
    if(ticks==55){director.enabled=false;typeof(RaceDirector).GetProperty("Started").SetValue(director,true);director.GetComponent<RaceMenu>().OpenPauseSettings();}
    if(ticks==70){Invoke(director.GetComponent<RaceMenu>(),"nitro-blur");Check(NitroBlurSettings.Enabled&&PlayerPrefs.GetInt(NitroBlurSettings.PreferenceKey,-1)==1,"pause toggle persists on");}
    if(ticks==80)ScreenCapture.CaptureScreenshot(Path.Combine(Output,"pause-settings-enabled.png"));
    if(ticks==95){
     CameraChecks();DisplaySettingsChecks.Run();
     File.WriteAllText(Path.Combine(Output,"result.json"),$"{{\"success\":true,\"assertions\":{assertions},\"playMode\":true,\"localCameras\":4}}");Debug.Log($"NITRO_BLUR_SETTING_OK assertions={assertions} main pause persistence default all4cameras freshAttack displayAdjacent");Finish(false);
    }
   }catch(Exception error){Debug.LogException(error);File.WriteAllText(Path.Combine(Output,"failure.txt"),error.ToString());Finish(true);}
  }
  static void Advance(NitroEdgeBlur effect,float dt)=>typeof(NitroEdgeBlur).GetMethod("UpdateEffect",Private).Invoke(effect,new object[]{dt});
  static void CameraChecks(){
   var effects=new NitroEdgeBlur[4];Time.timeScale=1;
   typeof(RaceDirector).GetProperty("Paused").SetValue(director,false);
   NitroBlurSettings.Enabled=true;
   for(int i=0;i<4;i++){
    var car=director.Cars[i];car.Telemetry.speedKmh=450;car.SetRaceContext(true,1,8);car.SetPresentationInput(1,true);
    typeof(MagneticVehicle).GetField("acceptedBoost",Private).SetValue(car,true);typeof(MagneticVehicle).GetMethod("UpdateExhaust",Private).Invoke(car,new object[]{1f/60});
    var follow=director.CameraFor(i);follow.target=car;effects[i]=follow.GetComponent<NitroEdgeBlur>();Advance(effects[i],1);
    Check(effects[i].Strength>.99f,"enabled camera "+i);
   }
   NitroBlurSettings.Enabled=false;foreach(var effect in effects)Check(effect.Strength==0,"off immediate");
   NitroBlurSettings.Enabled=true;foreach(var effect in effects){Check(effect.Strength==0,"enable no old envelope");Advance(effect,1f/60);Check(effect.Strength>0&&effect.Strength<.2f,"fresh smooth attack");}
   NitroBlurSettings.Enabled=false;foreach(var effect in effects){Advance(effect,1);Check(effect.Strength==0,"disabled remains zero despite nitro");}
   NitroBlurSettings.Reload();Check(!NitroBlurSettings.Enabled,"reload stored off");
   var go=new GameObject("new session camera");try{var effect=go.AddComponent<NitroEdgeBlur>();go.GetComponent<ChaseCamera>().target=director.Cars[0];Advance(effect,1);Check(effect.Strength==0,"new camera observes persisted off");}finally{UnityEngine.Object.DestroyImmediate(go);}
   Check(director.Cars[0].NitroFeedbackActive,"preference does not suppress accepted physical feedback");
  }
  static void Finish(bool error){
   EditorApplication.update-=Tick;SessionState.SetBool(Key+"Failed",error);
   foreach(var go in SceneManager.GetActiveScene().GetRootGameObjects())UnityEngine.Object.DestroyImmediate(go);
   if(prepared){if(hadPrefs)PlayerPrefs.SetInt(NitroBlurSettings.PreferenceKey,prefs);else PlayerPrefs.DeleteKey(NitroBlurSettings.PreferenceKey);if(hadDisplayPrefs)PlayerPrefs.SetString(DisplaySettings.PreferenceKey,displayPrefs);else PlayerPrefs.DeleteKey(DisplaySettings.PreferenceKey);PlayerPrefs.Save();NitroBlurSettings.Reload();AudioListener.volume=volume;}
   Time.timeScale=1;EditorApplication.isPlaying=false;
  }
 }
}

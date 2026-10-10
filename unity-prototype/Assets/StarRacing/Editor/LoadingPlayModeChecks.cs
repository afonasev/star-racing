using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace StarRacingPrototype {
 [InitializeOnLoad]
 public static class LoadingPlayModeChecks {
  const string Key="StarRacing.LoadingChecks";
  static int state,assertions;static double deadline,next;static RaceDirector director;static RaceMenu menu;
  static bool had;static string prefs;static float volume;static int generation;
  static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_LOADING_QA_DIR");
  static LoadingPlayModeChecks(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run(){
   if(string.IsNullOrEmpty(Output))throw new Exception("STAR_RACING_LOADING_QA_DIR required");
   RaceHudChecks.Run();
   Directory.CreateDirectory(Output);SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",false);
   EditorSceneManager.OpenScene("Assets/StarRacing/Generated/Prototype.unity");
   EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Show();
   EditorApplication.isPlaying=true;
  }
  static void Check(bool ok,string name){assertions++;if(!ok)throw new Exception("LOADING_CHECK "+name);}
  static void Shot(string name)=>ScreenCapture.CaptureScreenshot(Path.Combine(Output,name+".png"));
  static void Changed(PlayModeStateChange mode){
   if(!SessionState.GetBool(Key,false))return;
   if(mode==PlayModeStateChange.EnteredPlayMode){
    Application.runInBackground=true;state=0;assertions=0;deadline=EditorApplication.timeSinceStartup+150;next=0;
    had=PlayerPrefs.HasKey(LocalRaceConfig.PreferenceKey);prefs=PlayerPrefs.GetString(LocalRaceConfig.PreferenceKey);volume=AudioListener.volume;AudioListener.volume=0;
    EditorApplication.update+=Tick;
   }
   if(mode==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",false)?1:0);}
  }
  static void Tick(){
   try{
    double now=EditorApplication.timeSinceStartup;if(now>deadline)throw new Exception("loading checks timeout");if(now<next)return;
    if(state==0){
     Check(UnityEngine.Object.FindAnyObjectByType<RaceLoading>()!=null,"startup loading exists before director");
     Check(UnityEngine.Object.FindAnyObjectByType<RaceDirector>()==null,"startup construction deferred");Shot("01-startup-loading");state++;next=now+.8;return;
    }
    if(state==1){director=UnityEngine.Object.FindAnyObjectByType<RaceDirector>();if(director==null)return;menu=director.GetComponent<RaceMenu>();Check(!director.Started,"main menu ready");Check(UnityEngine.Object.FindAnyObjectByType<RaceLoading>()==null,"startup overlay removed");Shot("02-main");state++;next=now+.3;return;}
    if(state==2){menu.Open(RaceMenuScreen.Settings);Shot("03-settings");state++;next=now+.3;return;}
    if(state==3){menu.Open(RaceMenuScreen.LocalSetup);menu.Selected.humans=1;menu.Selected.entrants=8;menu.Selected.devices[0]=-2;menu.Selected.theme="cloud-city";Shot("04-setup");state++;next=now+.3;return;}
    if(state==4){generation=director.RaceGeneration;menu.StartSelected();Check(menu.Loading&&!director.Started,"start deferred");menu.StartSelected();Check(director.RaceGeneration==generation,"duplicate blocked");Shot("05-match-loading");state++;next=now+.8;return;}
    if(state==5){if(menu.Loading)return;Check(director.Started&&director.Session.Phase==RacePhase.Countdown,"countdown after preparation");Check(director.RaceGeneration==generation+1,"one start only");director.SetPaused(true);Shot("06-pause");state++;next=now+.3;return;}
    if(state==6){menu.OpenPauseSettings();Shot("07-pause-settings");state++;next=now+.3;return;}
    if(state==7){menu.ClosePauseSettings();generation=director.RaceGeneration;menu.RepeatRace();Check(menu.Loading,"repeat overlay");menu.RepeatRace();Shot("08-repeat-loading");state++;next=now+.8;return;}
    if(state==8){if(menu.Loading)return;Check(director.RaceGeneration==generation+1,"one repeat only");director.SetPaused(true);typeof(RaceSession).GetProperty("Phase").SetValue(director.Session,RacePhase.Results);
     for(int i=0;i<director.Session.Racers.Length;i++)director.Session.Racers[i].GetType().GetProperty("Place").SetValue(director.Session.Racers[i],i+1);
     Shot("09-results");state++;next=now+.3;return;}
    if(state==9){if(menu.Loading)return;Check(director.ExitToMenu(),"return menu");menu.Selected.devices[0]=int.MaxValue;menu.StartSelected(); // invalid binding returns through overlay
     state++;next=now+.8;return;}
    if(state==10){if(menu.Loading)return;Check(!director.Started&&!string.IsNullOrEmpty(menu.Error),"failure returns with error");Shot("09-error-menu");
     Check(CloudlineSkin.FooterText=="© 2026 Evgeniy Afonasev · afonasev.tech · Made with Codex","exact footer");
     File.WriteAllText(Path.Combine(Output,"success.txt"),"LOADING_PLAYMODE_OK assertions="+assertions);Debug.Log("LOADING_PLAYMODE_OK assertions="+assertions);Finish(false);}
   }catch(Exception e){Debug.LogException(e);File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());Finish(true);}
  }
  static void Finish(bool failed){EditorApplication.update-=Tick;SessionState.SetBool(Key+"Failed",failed);if(had)PlayerPrefs.SetString(LocalRaceConfig.PreferenceKey,prefs);else PlayerPrefs.DeleteKey(LocalRaceConfig.PreferenceKey);PlayerPrefs.Save();AudioListener.volume=volume;Time.timeScale=1;EditorApplication.isPlaying=false;}
 }
}

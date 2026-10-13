using System;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using StarRacingPrototype.Distribution;

namespace StarRacingPrototype {
 [InitializeOnLoad]
 public static class DesktopUpdatePlayModeChecks {
  const string Key="StarRacing.DesktopUpdateChecks";
  static int ticks;static double started;static EditorWindow view;static GameObject preview;
  static DesktopUpdater updater;static CancellationTokenSource cancellation;static float volume;
  static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_UPDATE_QA_DIR");
  static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
  static DesktopUpdatePlayModeChecks(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run(){
   if(string.IsNullOrEmpty(Output))throw new Exception("STAR_RACING_UPDATE_QA_DIR required");
   DesktopUpdateChecks.Run();Directory.CreateDirectory(Output);SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",false);
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
   view=EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"),false,"Game",false);view.position=new Rect(0,0,1600,940);view.Show();EditorApplication.isPlaying=true;
  }
  static void Changed(PlayModeStateChange state){
   if(!SessionState.GetBool(Key,false))return;
   if(state==PlayModeStateChange.EnteredPlayMode){ticks=0;started=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;}
   if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",false)?1:0);}
  }
  static void Set(string key,object value)=>typeof(DesktopUpdater).GetProperty(key).SetValue(updater,value);
  static bool HasControl(string id){
   foreach(var control in (IEnumerable)typeof(RaceMenu).GetField("controls",Private).GetValue(preview.GetComponent<RaceMenu>()))
    if((string)control.GetType().GetField("id").GetValue(control)==id)return true;
   return false;
  }
  static void Tick(){
   try{
    if(EditorApplication.timeSinceStartup-started>120)throw new Exception("Desktop update PlayMode checks timed out");
    EditorApplication.QueuePlayerLoopUpdate();view?.Repaint();ticks++;
    if(ticks==1){
     volume=AudioListener.volume;AudioListener.volume=0;new GameObject("Update QA camera").AddComponent<Camera>();
     preview=new GameObject("Update menu preview");preview.AddComponent<RaceDirector>();updater=DesktopUpdater.Instance;
     var source=new AuthenticatedUpdateSource("tech.afonasev.star-racing.win-x64","win-x64",1);
     typeof(AuthenticatedUpdateSource).GetProperty("Release").SetValue(source,new DesktopRelease{version="1.2.3",size=104857600});
     typeof(DesktopUpdater).GetField("source",Private).SetValue(updater,source);
     Set("InstalledVersion","0.3.3");Set("PublishedDate","2026-10-11");Set("CanUpdate",true);Set("Available",true);Set("Busy",true);Set("Status","Скачивание обновления");
     cancellation=new CancellationTokenSource();typeof(DesktopUpdater).GetField("download",Private).SetValue(updater,cancellation);typeof(DesktopUpdater).GetField("progress",Private).SetValue(updater,42);
    }
    if(ticks==25){
     if(HasControl("updates")||!HasControl("cancel-update"))throw new Exception("Download controls incorrect");
     ScreenCapture.CaptureScreenshot(Path.Combine(Output,"download.png"));
    }
    if(ticks==40){
     updater.Cancel();if(!cancellation.IsCancellationRequested)throw new Exception("Cancellation did not reach transfer");
     typeof(DesktopUpdater).GetField("download",Private).SetValue(updater,null);cancellation.Dispose();cancellation=null;
     Set("Busy",false);Set("Status","Не удалось загрузить обновление: пакет повреждён. Повторите загрузку.");
    }
    if(ticks==55){if(!HasControl("updates")||HasControl("cancel-update"))throw new Exception("Retry controls incorrect");ScreenCapture.CaptureScreenshot(Path.Combine(Output,"error.png"));}
    if(ticks==70){Set("Staged",true);Set("Status",DesktopUpdater.PreparedStatus);}
    if(ticks==85){if(HasControl("updates")||!HasControl("restart-update")||HasControl("check-updates")||!HasControl("local"))throw new Exception("Staged install action missing");ScreenCapture.CaptureScreenshot(Path.Combine(Output,"staged.png"));}
    if(ticks==100){Set("StartupApplying",true);}
    if(ticks==115){if(HasControl("local")||HasControl("updates")||HasControl("restart-update")||HasControl("quit"))throw new Exception("Applying actions must be disabled");Set("StartupApplying",false);Set("Staged",false);Set("Available",false);Set("CurrentVersionConfirmed",true);Set("Status","Установлена актуальная версия.");}
    if(ticks==130){if(HasControl("updates")||HasControl("check-updates")||!HasControl("local")||updater.ShowUpdateBlock)throw new Exception("Current version controls incorrect");ScreenCapture.CaptureScreenshot(Path.Combine(Output,"current.png"));}
    if(ticks==140){Set("CurrentVersionConfirmed",false);Set("Busy",true);Set("Status","Проверка обновлений…");}
    if(ticks==145){if(HasControl("check-updates")||HasControl("updates")||HasControl("local")||!updater.ShowUpdateBlock)throw new Exception("Checking actions must be disabled");ScreenCapture.CaptureScreenshot(Path.Combine(Output,"checking.png"));}
    if(ticks==150){Set("Busy",false);Set("Available",true);Set("Status","Доступна версия 1.2.3. Нажмите «Обновить», чтобы скачать изменения.");}
    if(ticks==160){if(!HasControl("updates")||HasControl("check-updates"))throw new Exception("Available update controls incorrect");ScreenCapture.CaptureScreenshot(Path.Combine(Output,"available.png"));}
    if(ticks==175){File.WriteAllText(Path.Combine(Output,"result.txt"),"DESKTOP_UPDATE_PLAYMODE_OK states=download,cancel,error,retry,staged,applying,current,checking,available\n");Debug.Log("DESKTOP_UPDATE_PLAYMODE_OK");Finish(false);}
   }catch(Exception e){Debug.LogException(e);File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());Finish(true);}
  }
  static void Finish(bool failed){
   SessionState.SetBool(Key+"Failed",failed);EditorApplication.update-=Tick;
   if(updater!=null)typeof(DesktopUpdater).GetField("download",Private).SetValue(updater,null);cancellation?.Dispose();cancellation=null;
   AudioListener.volume=volume;Time.timeScale=1;EditorApplication.isPlaying=false;
  }
 }
}

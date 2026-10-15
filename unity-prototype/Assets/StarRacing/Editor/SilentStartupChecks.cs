using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using StarRacingPrototype.Distribution;

namespace StarRacingPrototype {
 public static class SilentStartupChecks {
  public static void Run(){
   int assertions=0;
   GameObject fixture=null;
   void Check(bool result,string message){assertions++;if(!result)throw new Exception(message);}
   try{
    var initialize=typeof(SilentStartup).GetMethod("Initialize",BindingFlags.Static|BindingFlags.Public);
    var callback=(RuntimeInitializeOnLoadMethodAttribute)initialize.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute),false).Single();
    Check(callback.loadType==RuntimeInitializeLoadType.BeforeSplashScreen,"silent handler is not registered before the splash screen");
    SilentStartup.Initialize();
    Check(SilentStartup.Enabled,"--muted was not applied before the first scene");
    Check(AudioListener.pause,"silent startup did not pause Unity audio before the scene");
    Check(SilentStartup.IsRequested(new[]{"game","-muted"}),"canonical startup argument is not recognized");
    Check(SilentStartup.IsRequested(new[]{"game","--muted"}),"documented argument is not recognized");
    Check(SilentStartup.IsRequested(new[]{"game","-noaudio"}),"Unity no-audio argument is not recognized");
    Check(!SilentStartup.IsRequested(new[]{"game"}),"normal startup was classified as silent");
    fixture=new GameObject("silent coordinator fixture");var audio=fixture.AddComponent<RaceAudioCoordinator>();
    Check(audio.Director==null&&fixture.GetComponentsInChildren<AudioSource>().Length==0,"silent coordinator initialized game audio");
    var method=typeof(DesktopUpdater).GetMethod("RestartArguments",BindingFlags.Static|BindingFlags.NonPublic);
    var restarted=(string[])method.Invoke(null,new object[]{new[]{"--muted","--star-racing-apply-attempt=old","--custom"},"new"});
    Check(restarted.Contains("--muted")&&restarted.Contains("--custom"),"updater restart dropped existing arguments");
    Check(!restarted.Contains("--star-racing-apply-attempt=old")&&restarted.Contains("--star-racing-apply-attempt=new"),"updater restart attempt argument was not replaced");
    typeof(SilentStartup).GetMethod("ResetSession",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
    Check(!SilentStartup.Enabled&&!AudioListener.pause,"silent session state was not reset before the next session");
    Debug.Log("SILENT_STARTUP_CHECKS_OK assertions="+assertions+" before-splash muted noaudio updater-restart");
   }finally{if(fixture!=null)UnityEngine.Object.DestroyImmediate(fixture);}
  }
 }
}

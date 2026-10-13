using System;
using UnityEngine;

namespace StarRacingPrototype {
 public static class DisplaySettingsChecks {
  static int checks;
  static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
  public static void Run(){
   bool had=PlayerPrefs.HasKey(DisplaySettings.PreferenceKey);string previous=PlayerPrefs.GetString(DisplaySettings.PreferenceKey);
   int previousVSync=QualitySettings.vSyncCount,previousTarget=Application.targetFrameRate;
   try{
    checks=0;PlayerPrefs.DeleteKey(DisplaySettings.PreferenceKey);
    var settings=DisplaySettings.Load();Check(settings.fullscreen&&settings.width==0&&settings.height==0,"first launch defaults");
    Check(settings.vSync,"first launch enables VSync");
    PlayerPrefs.SetString(DisplaySettings.PreferenceKey,"{\"fullscreen\":false,\"width\":1920,\"height\":1080}");
    var legacy=DisplaySettings.Load();Check(legacy.vSync&&!legacy.fullscreen&&legacy.width==1920&&legacy.height==1080,"legacy display settings enable VSync without losing values");
    PlayerPrefs.SetString(DisplaySettings.PreferenceKey,"{}");
    var empty=DisplaySettings.Load();Check(empty.vSync&&empty.fullscreen&&empty.width==0&&empty.height==0,"missing fields retain defaults");
    var options=DisplaySettings.Options(new[]{new Resolution{width=1920,height=1080},new Resolution{width=1280,height=720},new Resolution{width=1920,height=1080}},new Vector2Int(2560,1440));
    Check(options.Length==4&&options[0]==Vector2Int.zero&&options[1]==new Vector2Int(1280,720),"resolution deduplication and sort");
    settings.fullscreen=false;settings.width=1920;settings.height=1080;settings.Save();settings=DisplaySettings.Load();
    Check(!settings.fullscreen&&settings.Selected(options)==2,"persist window and resolution");
    settings.vSync=true;settings.Save();settings=DisplaySettings.Load();
    Check(settings.vSync&&!settings.fullscreen&&settings.Selected(options)==2,"persist enabled VSync and existing settings");
    Application.targetFrameRate=120;settings.Apply(new Vector2Int(1920,1080));
    Check(QualitySettings.vSyncCount==1&&Application.targetFrameRate==120,"enable VSync without changing fallback frame limit");
    settings.vSync=false;settings.Save();settings=DisplaySettings.Load();settings.Apply(new Vector2Int(1920,1080));
    Check(!settings.vSync&&QualitySettings.vSyncCount==0&&Application.targetFrameRate==120,"persist disabled VSync and restore frame limit");
    settings.Validate(DisplaySettings.Options(Array.Empty<Resolution>(),new Vector2Int(1280,720)));
    Check(!settings.fullscreen&&settings.width==0&&settings.height==0,"monitor fallback preserves mode");
    PlayerPrefs.SetString(DisplaySettings.PreferenceKey,"invalid");settings=DisplaySettings.Load();Check(settings.fullscreen&&settings.width==0,"corrupt preference fallback");
    Check(settings.vSync,"corrupt preference enables VSync by default");
    var mode=Screen.fullScreenMode;settings.Apply(new Vector2Int(1920,1080));Check(Screen.fullScreenMode==mode,"Editor host display unchanged");
    Debug.Log("DISPLAY_SETTINGS_CHECKS_OK assertions="+checks);
   }finally{if(had)PlayerPrefs.SetString(DisplaySettings.PreferenceKey,previous);else PlayerPrefs.DeleteKey(DisplaySettings.PreferenceKey);PlayerPrefs.Save();QualitySettings.vSyncCount=previousVSync;Application.targetFrameRate=previousTarget;}
  }
  public static void RunWithHud(){Run();RaceHudChecks.Run();}
 }
}

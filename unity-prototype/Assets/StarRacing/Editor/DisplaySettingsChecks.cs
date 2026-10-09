using System;
using UnityEngine;

namespace StarRacingPrototype {
 public static class DisplaySettingsChecks {
  static int checks;
  static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
  public static void Run(){
   bool had=PlayerPrefs.HasKey(DisplaySettings.PreferenceKey);string previous=PlayerPrefs.GetString(DisplaySettings.PreferenceKey);
   try{
    checks=0;PlayerPrefs.DeleteKey(DisplaySettings.PreferenceKey);
    var settings=DisplaySettings.Load();Check(settings.fullscreen&&settings.width==0&&settings.height==0,"first launch defaults");
    var options=DisplaySettings.Options(new[]{new Resolution{width=1920,height=1080},new Resolution{width=1280,height=720},new Resolution{width=1920,height=1080}},new Vector2Int(2560,1440));
    Check(options.Length==4&&options[0]==Vector2Int.zero&&options[1]==new Vector2Int(1280,720),"resolution deduplication and sort");
    settings.fullscreen=false;settings.width=1920;settings.height=1080;settings.Save();settings=DisplaySettings.Load();
    Check(!settings.fullscreen&&settings.Selected(options)==2,"persist window and resolution");
    settings.Validate(DisplaySettings.Options(Array.Empty<Resolution>(),new Vector2Int(1280,720)));
    Check(!settings.fullscreen&&settings.width==0&&settings.height==0,"monitor fallback preserves mode");
    PlayerPrefs.SetString(DisplaySettings.PreferenceKey,"invalid");settings=DisplaySettings.Load();Check(settings.fullscreen&&settings.width==0,"corrupt preference fallback");
    var mode=Screen.fullScreenMode;settings.Apply(new Vector2Int(1920,1080));Check(Screen.fullScreenMode==mode,"Editor host display unchanged");
    Debug.Log("DISPLAY_SETTINGS_CHECKS_OK assertions="+checks);
   }finally{if(had)PlayerPrefs.SetString(DisplaySettings.PreferenceKey,previous);else PlayerPrefs.DeleteKey(DisplaySettings.PreferenceKey);PlayerPrefs.Save();}
  }
 }
}

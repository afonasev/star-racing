using System;
using System.IO;
using UnityEngine;

namespace StarRacingPrototype {
 [Serializable]
 public sealed class UserSettings {
  public int version=1;
  public DisplaySettings display=new DisplaySettings();
  public RaceGraphicsSettings graphics=new RaceGraphicsSettings();
  public float master=1,music=1,engines=1,effects=1;
  public float stickSensitivity=1,stickDeadzone=.18f,textScale=1;
  public bool highContrast,showFps=true,nitroBlur=true;
  public int frameLimit=120;
  // -1 preserves the existing pipeline choice on migration/first launch.
  public int antiAliasing=-1;
  public static readonly int[] FrameLimits={0,30,60,90,120,144,165,240};
  static float Valid(float value,float min,float max,float fallback)=>float.IsNaN(value)||float.IsInfinity(value)||value<min||value>max?fallback:value;
  public static UserSettings Parse(string json){
   var value=new UserSettings();
   if(!string.IsNullOrWhiteSpace(json))JsonUtility.FromJsonOverwrite(json,value);
   if(value.version!=1)throw new FormatException("Неподдерживаемая версия настроек");
   value.display=value.display??new DisplaySettings();value.graphics=value.graphics??new RaceGraphicsSettings();
   value.master=Valid(value.master,0,1,1);value.music=Valid(value.music,0,1,1);value.engines=Valid(value.engines,0,1,1);value.effects=Valid(value.effects,0,1,1);
   value.stickSensitivity=Valid(value.stickSensitivity,.5f,2,1);value.stickDeadzone=Valid(value.stickDeadzone,0,.5f,.18f);value.textScale=Valid(value.textScale,.9f,1.1f,1);
   if(Array.IndexOf(FrameLimits,value.frameLimit)<0)value.frameLimit=120;
   if(value.antiAliasing!= -1&&value.antiAliasing!=0&&value.antiAliasing!=1&&value.antiAliasing!=2)value.antiAliasing=-1;
   return value;
  }
  public UserSettings Copy()=>Parse(JsonUtility.ToJson(this));
 }

 // One durable file for preferences; runtime changes may remain active if a save fails.
 // Neither failed writes nor technical silent startup overwrite the previous saved file.
 public static class UserSettingsStore {
  static UserSettings current;
  public static string LastError {get;private set;}="";
#if UNITY_EDITOR
  public static string OverridePath;
#else
  static readonly string OverridePath=null;
#endif
  public static string PathName=>OverridePath??Path.Combine(Application.persistentDataPath,"user-settings-v1.json");
  public static UserSettings Current=>current??(current=Load());
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  public static void Reload(){current=null;LastError="";}
  static UserSettings Legacy(){
   return new UserSettings{display=DisplaySettings.Load(),graphics=RaceGraphicsSettings.Load(),master=PlayerPrefs.GetInt("StarRacing.Audio.Muted",0)!=0?0:1,
    music=Mathf.Clamp01(PlayerPrefs.GetFloat("StarRacing.Audio.Music",1)),engines=Mathf.Clamp01(PlayerPrefs.GetFloat("StarRacing.Audio.Engines",1)),effects=Mathf.Clamp01(PlayerPrefs.GetFloat("StarRacing.Audio.Effects",1)),
    showFps=PlayerPrefs.GetInt("StarRacing.ShowFps",1)!=0,nitroBlur=PlayerPrefs.GetInt(NitroBlurSettings.PreferenceKey,1)!=0};
  }
  static UserSettings Load(){
   try{return File.Exists(PathName)?UserSettings.Parse(File.ReadAllText(PathName)):Legacy();}
   catch(Exception e) when(e is IOException||e is UnauthorizedAccessException||e is ArgumentException||e is FormatException){LastError="Не удалось прочитать настройки. Используются безопасные значения. "+e.Message;return new UserSettings();}
  }
  public static bool Save(){
   try{WriteFile(PathName,JsonUtility.ToJson(Current,true));LastError="";return true;}
   catch(Exception e) when(e is IOException||e is UnauthorizedAccessException||e is ArgumentException){LastError="Изменение действует сейчас, но не сохранено между запусками. Проверьте доступ и свободное место. "+e.Message;return false;}
  }
  internal static void WriteFile(string path,string json){
   Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
   try{
    using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var writer=new StreamWriter(file)){writer.Write(json);writer.Flush();file.Flush(true);}
    if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
   }finally{if(File.Exists(temp))File.Delete(temp);}
  }
  public static void Replace(UserSettings value){if(value==null)throw new ArgumentNullException(nameof(value));current=value;}
 }
}

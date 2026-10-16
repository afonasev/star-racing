using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace StarRacingPrototype {
 public static class StandardSettingsChecks {
  static int assertions;
  static void Check(bool value,string message){assertions++;if(!value)throw new Exception(message);}
  static void Invoke(object target,string name)=>target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,null);
  public static void RunAffected(){Run();DisplaySettingsChecks.Run();RaceHudChecks.Run();SilentStartupChecks.Run();}
  public static void Run(){
   string folder=Environment.GetEnvironmentVariable("STAR_RACING_SETTINGS_QA_DIR");if(string.IsNullOrEmpty(folder))throw new Exception("STAR_RACING_SETTINGS_QA_DIR required");
   Directory.CreateDirectory(folder);string path=Path.Combine(folder,"fixture-preferences.json");
   string previousPath=UserSettingsStore.OverridePath;var previous=UserSettingsStore.Current;int vsync=QualitySettings.vSyncCount,target=Application.targetFrameRate;
   GameObject fixture=null;
   try{
    assertions=0;UserSettingsStore.OverridePath=path;UserSettingsStore.Replace(new UserSettings());
    var defaults=new UserSettings();Check(defaults.master==1&&defaults.stickDeadzone==.18f&&defaults.frameLimit==120,"defaults changed from existing runtime");
    var zeros=UserSettings.Parse("{\"master\":0,\"music\":0,\"engines\":0,\"effects\":0,\"showFps\":false,\"nitroBlur\":false,\"stickDeadzone\":0,\"display\":{\"fullscreen\":false,\"vSync\":false},\"graphics\":{\"shadows\":false}}");
    Check(zeros.master==0&&zeros.music==0&&zeros.engines==0&&zeros.effects==0,"valid zero volumes lost");
    Check(!zeros.showFps&&!zeros.nitroBlur&&zeros.stickDeadzone==0&&!zeros.display.fullscreen&&!zeros.display.vSync&&!zeros.graphics.shadows,"false/zero fields lost");
    var missing=UserSettings.Parse("{}");Check(missing.master==1&&missing.display.fullscreen&&missing.display.vSync&&missing.graphics.shadows,"missing defaults lost");
    var invalid=UserSettings.Parse("{\"master\":10,\"stickSensitivity\":0,\"stickDeadzone\":2,\"textScale\":4,\"frameLimit\":13}");
    Check(invalid.master==1&&invalid.stickSensitivity==1&&invalid.stickDeadzone==.18f&&invalid.textScale==1&&invalid.frameLimit==120,"unsafe stored values accepted");
    Check(LocalInputRouter.ApplyStickPreference(.17f,1,.18f)==0&&LocalInputRouter.ApplyStickPreference(.18f,1,.18f)==.18f,"default steering threshold changed");
    Check(LocalInputRouter.ApplyStickPreference(.5f,1,.18f)==.5f&&LocalInputRouter.ApplyStickPreference(-1,2,.18f)==-1,"stick sensitivity/default response broken");
    Check(LocalInputRouter.ApplyStickPreference(.25f,.5f,0)==.125f,"stick preference is not applied");
    Check(RaceMenu.PadGlyph("XInputController","north")=="Y"&&RaceMenu.PadGlyph("DualSenseGamepadHID","north")=="△"&&RaceMenu.PadGlyph("SwitchProControllerHID","north")=="X"&&RaceMenu.PadGlyph("Gamepad","north")=="Верх","reset glyph family mapping");
    UserSettingsStore.Replace(zeros);Check(UserSettingsStore.Save(),"first durable save");var saved=File.ReadAllText(path);UserSettingsStore.Reload();
    Check(UserSettingsStore.Current.master==0&&!UserSettingsStore.Current.display.vSync,"saved zeros/false did not survive reload");
    UserSettingsStore.Current.music=.35f;UserSettingsStore.OverridePath=Path.Combine(path,"blocked","settings.json");
    Check(!UserSettingsStore.Save()&&!string.IsNullOrEmpty(UserSettingsStore.LastError),"failed save announced success");
    Check(File.ReadAllText(path)==saved&&UserSettingsStore.Current.music==.35f,"failed save lost saved bytes or active value");
    UserSettingsStore.OverridePath=path;Check(UserSettingsStore.Save()&&UserSettingsStore.LastError=="","retry did not save/clear error");
    fixture=new GameObject("Offline audio bus fixture");var audio=fixture.AddComponent<RaceAudioCoordinator>();
    UserSettingsStore.Current.master=.4f;UserSettingsStore.Current.music=0;UserSettingsStore.Current.engines=.2f;UserSettingsStore.Current.effects=.5f;audio.RefreshPreferences();
    Check(audio.MusicGain==0&&Mathf.Abs(audio.EnginesGain-.08f)<.000001f&&Mathf.Abs(audio.EffectsGain-.2f)<.000001f,"master/channel products or channel isolation broken");
    UserSettingsStore.Current.master=0;Check(audio.MusicGain==0&&audio.EnginesGain==0&&audio.EffectsGain==0&&audio.Muted,"master zero did not silence every bus");
    Check(fixture.GetComponentsInChildren<AudioSource>().Length==0,"offline bus check created audible sources");
    UnityEngine.Object.DestroyImmediate(fixture);fixture=null;
    UserSettingsStore.Replace(new UserSettings{display=new DisplaySettings{fullscreen=false,vSync=false,width=1280,height=720},master=.4f,showFps=false});UserSettingsStore.Save();saved=File.ReadAllText(path);
    fixture=new GameObject("Standard settings contract fixture");var menu=fixture.AddComponent<RaceMenu>();Invoke(menu,"Awake");
    menu.Display.fullscreen=true;Invoke(menu,"ApplyDisplay");
    Check(menu.DisplayConfirmationPending&&!UserSettingsStore.Current.display.fullscreen&&File.ReadAllText(path)==saved,"unconfirmed screen mode persisted");
    menu.RevertDisplay();Check(!menu.DisplayConfirmationPending&&!menu.Display.fullscreen&&File.ReadAllText(path)==saved,"screen cancel failed");
    menu.Display.fullscreen=true;Invoke(menu,"ApplyDisplay");typeof(RaceMenu).GetField("displayDeadline",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(menu,-1d);Invoke(menu,"TickSettings");
    Check(!menu.DisplayConfirmationPending&&!menu.Display.fullscreen,"screen timer failed to restore mode");
    menu.Display.fullscreen=true;Invoke(menu,"ApplyDisplay");menu.ConfirmDisplay();UserSettingsStore.Reload();
    Check(!menu.DisplayConfirmationPending&&UserSettingsStore.Current.display.fullscreen,"confirmed mode did not persist");
    var progress=Path.Combine(folder,"progress-sentinel.txt");File.WriteAllText(progress,"unrelated progress");
    menu.ResetAllPreferences();
    Check(UserSettingsStore.Current.master==1&&UserSettingsStore.Current.showFps&&UserSettingsStore.Current.stickDeadzone==.18f,"reset did not restore preferences");
    Check(File.ReadAllText(progress)=="unrelated progress","reset modified unrelated data");
    menu.RevertDisplay();
    Check(SilentStartup.IsRequested(new[]{"-noaudio","-muted"}),"silent launch contract lost");
    Debug.Log("STANDARD_SETTINGS_CHECKS_OK assertions="+assertions);
   }finally{
    if(fixture!=null)UnityEngine.Object.DestroyImmediate(fixture);
    UserSettingsStore.OverridePath=previousPath;UserSettingsStore.Replace(previous);QualitySettings.vSyncCount=vsync;Application.targetFrameRate=target;
   }
  }
 }
}

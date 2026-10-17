using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using StarRacingPrototype.Distribution;
namespace StarRacingPrototype {
 // Opt-in packaged UI smoke. Never reads or applies the user's staged update.
 public sealed class MenuReleaseSmoke:MonoBehaviour {
  public static string Output {get{var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--menu-release-smoke-dir");return i>=0&&i+1<args.Length?args[i+1]:null;}}
  public static bool Enabled=>!string.IsNullOrEmpty(Output);
  DesktopUpdater updater;RaceMenu menu;int assertions;float originalScale;
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Install(){if(Enabled)new GameObject("Packaged menu smoke").AddComponent<MenuReleaseSmoke>();}
  void Check(bool value,string label){assertions++;if(!value)throw new Exception("MENU_RELEASE_SMOKE "+label);}
  void Set(string key,object value)=>typeof(DesktopUpdater).GetProperty(key).SetValue(updater,value);
  IEnumerator Start(){
   Application.runInBackground=true;Directory.CreateDirectory(Output);
   // Preparation now spans frames; readiness, rather than a fixed delay, owns admission.
   RaceDirector director=null;float deadline=Time.realtimeSinceStartup+120;
   while(director==null||!director.Ready){
    director=FindFirstObjectByType<RaceDirector>();
    if(Time.realtimeSinceStartup>deadline){Fail(new TimeoutException("Packaged startup did not become ready"));yield break;}
    yield return null;
   }
   yield return new WaitForSecondsRealtime(2);
   try{Check(director!=null&&director.Ready&&!director.Started,"startup main menu");menu=director.GetComponent<RaceMenu>();originalScale=menu.Preferences.textScale;updater=DesktopUpdater.Instance;Check(menu!=null&&menu.ScreenState==RaceMenuScreen.Main,"main route");Check(updater!=null,"updater component");Check(updater.InstalledVersion==Application.version,"built version in footer");Check(!updater.CanUpdate,"isolated smoke avoids installed update queue");}
   catch(Exception e){Fail(e);yield break;}
   Set("CanUpdate",true);Set("Busy",true);Set("Status","Проверка обновлений…");
   yield return Shot("checking");
   try{Check(updater.ShowUpdateBlock,"checking block visible");Check(!Has("updates"),"no download before check completes");}catch(Exception e){Fail(e);yield break;}
   Set("Busy",false);Set("CurrentVersionConfirmed",true);Set("Status","Установлена актуальная версия.");
   yield return Shot("current");
   try{Check(!updater.ShowUpdateBlock&&!Has("updates")&&!Has("check-updates"),"current block hidden");Check(Has("local")&&Has("quit"),"normal navigation remains");}catch(Exception e){Fail(e);yield break;}
   Set("CurrentVersionConfirmed",false);Set("Available",true);Set("Status","Доступна новая версия. Нажмите «Обновить», чтобы скачать изменения.");
   yield return Shot("available");
   try{Check(updater.ShowUpdateBlock&&Has("updates"),"available action inside block");Check(CloudlineSkin.FooterText.StartsWith("v"+Application.version+" · "),"footer uses built version");}
   catch(Exception e){Fail(e);yield break;}
   try{Check(SilentStartup.Enabled,"silent from startup");Check(menu.Laboratory.Active!=null,"Lab active loaded");menu.Laboratory.ReleaseDefault();}
   catch(Exception e){Fail(e);yield break;}
   Set("HistoryEntries",new[]{new ReleaseHistoryEntry{Version=Application.version,Date="2026-10-16",Body="## Русский\n### Новое\n- Проверка истории.\n## English\n### Added\n- Release history check."}});Set("HistoryStatus","");menu.Open(RaceMenuScreen.History);
   yield return Shot("history-ru");
   try{Check(Has("history-ru")&&Has("history-en"),"language controls");menu.Preferences.textScale=1.1f;Activate("history-en");}
   catch(Exception e){Fail(e);yield break;}
   yield return Shot("history-en-110pct");
   try{Check((string)typeof(RaceMenu).GetField("historyLanguage",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(menu)=="en","EN button action");Activate("history-ru");Check(Has("history-version-0"),"installed version selectable");}
   catch(Exception e){Fail(e);yield break;}
   menu.Preferences.textScale=originalScale;
   File.WriteAllText(Path.Combine(Output,"success.json"),"{\"success\":true,\"scope\":\"menu\",\"synthetic_update_states\":true,\"installed_queue_untouched\":true,\"assertions\":"+assertions+",\"version\":\""+Application.version+"\",\"physicalAcceptance\":false}");Debug.Log("MENU_RELEASE_SMOKE_OK assertions="+assertions);Application.Quit();
  }
  void Activate(string id){foreach(var c in (IEnumerable)typeof(RaceMenu).GetField("controls",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(menu))if((string)c.GetType().GetField("id").GetValue(c)==id){((Action)c.GetType().GetField("action").GetValue(c))();return;}throw new Exception("Missing action "+id);}
  void OnApplicationQuit(){if(menu!=null)menu.Preferences.textScale=originalScale;}
  bool Has(string id){foreach(var c in (IEnumerable)typeof(RaceMenu).GetField("controls",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(menu))if((string)c.GetType().GetField("id").GetValue(c)==id)return true;return false;}
  IEnumerator Shot(string name){yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(Output,name+".png"));yield return new WaitForSecondsRealtime(.4f);}
  void Fail(Exception e){Debug.LogException(e);File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());Application.Quit(1);}
 }
}

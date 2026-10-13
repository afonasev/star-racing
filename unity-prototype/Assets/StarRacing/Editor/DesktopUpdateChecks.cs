using System;
using System.Reflection;
using System.Threading;
using UnityEngine;
using StarRacingPrototype.Distribution;

namespace StarRacingPrototype {
 public static class DesktopUpdateChecks {
  public static void Run(){
   int assertions=0;var root=new GameObject("desktop update checks");var updater=root.AddComponent<DesktopUpdater>();
   void Check(bool ok,string message){assertions++;if(!ok)throw new Exception(message);}
   void Set(string name,object value)=>typeof(DesktopUpdater).GetProperty(name).SetValue(updater,value);
   Check(CloudlineSkin.FormatFooter("0.3.3","2026-10-11")=="v0.3.3 · Published 2026-10-11 · "+CloudlineSkin.CopyrightText,"short English published footer");
   Check(CloudlineSkin.FormatFooter("0.3.3",null)=="v0.3.3 · "+CloudlineSkin.CopyrightText,"unknown publication date is omitted");
   Check(!CloudlineSkin.FormatFooter("0.3.3","2026-02-30").Contains("Published"),"invalid publication date is omitted");
   var flags=BindingFlags.Instance|BindingFlags.NonPublic;
   using(var cancellation=new CancellationTokenSource())try{
    Check(updater.ButtonLabel=="Проверить обновления","idle action");
    Set("CanUpdate",true);Set("Available",true);Set("Busy",true);Set("Status","Подключение к серверу…");
    typeof(DesktopUpdater).GetField("download",flags).SetValue(updater,cancellation);
    Check(updater.ButtonLabel.Contains("0%")&&updater.VisibleStatus.Contains("Подключение"),"immediate feedback before first network byte");
    typeof(DesktopUpdater).GetField("progress",flags).SetValue(updater,42);
    Check(updater.ButtonLabel.Contains("42%")&&updater.VisibleStatus.Contains("42%"),"progress visible while available remains true");
    updater.Cancel();Check(cancellation.IsCancellationRequested,"cancel forwards to active transfer");
    typeof(DesktopUpdater).GetField("progress",flags).SetValue(updater,100);Set("Status","Проверка обновления…");
    Check(updater.VisibleStatus=="Проверка обновления…","verification status at 100 percent");
    typeof(DesktopUpdater).GetField("download",flags).SetValue(updater,null);Set("Busy",false);Set("Status","Не удалось загрузить обновление");
    Check(updater.VisibleStatus.Contains("Не удалось"),"download error remains visible for retry");
    Set("Staged",true);Check(updater.ButtonLabel=="Установить и перезапустить","staged action explicitly requests restart");
    updater.InstallAndRestart();Check(!updater.StartupApplying&&updater.Status.Contains("главное меню"),"activation outside preparation menu gives status");
    Set("StartupApplying",true);Check(updater.ButtonLabel.Contains("Установка"),"activation disables action via state");
    Debug.Log("DESKTOP_UPDATE_CHECKS_OK assertions="+assertions);
   }finally{typeof(DesktopUpdater).GetField("download",flags).SetValue(updater,null);UnityEngine.Object.DestroyImmediate(root);}
  }
 }
}

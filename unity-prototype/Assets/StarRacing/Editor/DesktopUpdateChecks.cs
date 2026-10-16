using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;
using Velopack.Logging;
using Velopack.NuGet;
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
   Set("CanUpdate",true);Check(updater.ShowUpdateBlock,"startup check block visible");
   Set("CurrentVersionConfirmed",true);Check(!updater.ShowUpdateBlock,"current release hides entire block");
   Set("Busy",true);Check(updater.ShowUpdateBlock,"new check reveals block");Set("Busy",false);Set("CurrentVersionConfirmed",false);
   var flags=BindingFlags.Instance|BindingFlags.NonPublic;
   using(var cancellation=new CancellationTokenSource())try{
    Check(updater.ButtonLabel=="Обновления недоступны","idle does not expose manual discovery");
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
    Set("StartupApplying",false);Set("Staged",false);Set("Busy",false);
    string temp=Path.Combine(Path.GetTempPath(),"star-racing-discovery-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
    try{
     var counted=new CountingSource();var actual=new AuthenticatedUpdateSource("tech.afonasev.star-racing.win-x64","win-x64",0);
     typeof(DesktopUpdater).GetField("source",flags).SetValue(updater,actual);
     typeof(DesktopUpdater).GetField("manager",flags).SetValue(updater,new UpdateManager(counted,new UpdateOptions{ExplicitChannel="win-x64"},new Locator(temp)));
     updater.Check().GetAwaiter().GetResult();updater.Check().GetAwaiter().GetResult();
     Check(counted.Discoveries==1,"exactly one discovery even after explicit second call");
     var pending=new TaskCompletionSource<bool>();typeof(DesktopUpdater).GetField("downloadTask",flags).SetValue(updater,pending.Task);
     var suspension=updater.SuspendForRace();Check(actual.NetworkSuspended&&!suspension.IsCompleted,"race admission cancels network and awaits active operation");
     pending.SetResult(true);suspension.GetAwaiter().GetResult();Check(actual.NetworkSuspended,"network remains suspended throughout race");
     updater.ResumeInMenu();Check(!actual.NetworkSuspended&&counted.Discoveries==1,"menu resume does not rediscover");
    }finally{Directory.Delete(temp,true);}
    Debug.Log("DESKTOP_UPDATE_CHECKS_OK assertions="+assertions);
   }finally{typeof(DesktopUpdater).GetField("download",flags).SetValue(updater,null);UnityEngine.Object.DestroyImmediate(root);}
  }
  sealed class CountingSource:IUpdateSource {
   public int Discoveries;
   public Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger,string app,string channel,Guid? stagingId=null,VelopackAsset latestLocalRelease=null){Discoveries++;return Task.FromResult(new VelopackAssetFeed{Assets=new VelopackAsset[0]});}
   public Task DownloadReleaseEntry(IVelopackLogger logger,VelopackAsset asset,string local,Action<int> progress,CancellationToken token=default)=>throw new Exception("Discovery fixture must never download");
  }
  sealed class Locator:VelopackLocator {
   readonly string root;public Locator(string path){root=path;}
   public override string AppId=>"tech.afonasev.star-racing.win-x64";public override string Channel=>"win-x64";
   public override string RootAppDir=>root;public override string AppContentDir=>root;public override string PackagesDir=>root;public override string UpdateExePath=>Path.Combine(root,"Update.exe");
   public override IProcessImpl Process=>throw new Exception("Discovery must not start a process");
   public override SemanticVersion CurrentlyInstalledVersion=>SemanticVersion.Parse("0.3.5");
  }

 }
}

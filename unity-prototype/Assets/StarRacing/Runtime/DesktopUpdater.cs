using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Linq;
using UnityEngine;
using Velopack;
using Velopack.Locators;
using Velopack.NuGet;

namespace StarRacingPrototype.Distribution {
 public sealed class DesktopUpdater:MonoBehaviour {
  public static DesktopUpdater Instance {get;private set;}
  public string InstalledVersion {get;private set;}
  public string Status {get;private set;}="Проверка обновлений…";
  public string NewVersion=>source?.Release?.version;
  public string Notes=>source?.Release?.notes??"";
  public float SizeMB=>(source?.Release?.size??0)/1048576f;
  public bool Busy {get;private set;}
  public bool Downloading=>download!=null;
  public bool Available {get;private set;}
  public bool Staged {get;private set;}
  public bool CanUpdate {get;private set;}
  public bool StartupApplying {get;private set;}
  string journal;
  readonly string track=DesktopReleaseChannel.Track;
  int progress;
  public int Progress=>Volatile.Read(ref progress);
  UpdateManager manager;AuthenticatedUpdateSource source;UpdateInfo update;CancellationTokenSource download;
  string receipt,channel,appId;long minimumSequence;
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
  static void Bootstrap(){
   if(Instance!=null)return;
   var obj=new GameObject("Desktop updates");DontDestroyOnLoad(obj);Instance=obj.AddComponent<DesktopUpdater>();
   Instance.Initialize();
  }
  void Initialize(){
   InstalledVersion=Application.version;
   if(Application.isEditor){Status="Обновления доступны в установленной игре";return;}
   try{
    VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
    channel=Application.platform==RuntimePlatform.WindowsPlayer?"win-x64":"osx-universal";
    appId="tech.afonasev.star-racing."+channel;
    minimumSequence=PlayerPrefs.GetInt("StarRacing.UpdateSequence."+track,track=="test"?PlayerPrefs.GetInt("StarRacing.UpdateSequence",0):0);
    receipt=Path.Combine(Application.persistentDataPath,"pending-update-"+track+".json");
    journal=Path.Combine(Application.persistentDataPath,"update-attempt-"+track+".json");
    source=new AuthenticatedUpdateSource(appId,channel,minimumSequence,track);
    manager=new UpdateManager(source,new UpdateOptions{ExplicitChannel=channel});
    CanUpdate=manager.IsInstalled;
    if(!CanUpdate){Status="Обновления доступны после установки игры";return;}
    InstalledVersion=manager.CurrentVersion.ToNormalizedString();
    Debug.Log("DESKTOP_UPDATE_READY version="+InstalledVersion+" track="+track+" installed="+CanUpdate);
    if(File.Exists(receipt)){
     source.Restore(File.ReadAllText(receipt));
     if(SemanticVersion.Parse(source.Release.version)>manager.CurrentVersion){
      ReleaseAuthentication.VerifyFile(source.Release,PackagePath(source.Release));
      update=new UpdateInfo(source.Release.Asset,false);Staged=true;Available=true;Status="Обновление готово к установке";
      TryApplyAtStartup();return;
     }
     File.Delete(receipt);if(File.Exists(journal))File.Delete(journal);
    }
    _=Check();
   }catch(Exception e){Status="Обновление недоступно: "+SafeError(e);Debug.LogWarning("DESKTOP_UPDATE_INIT "+e);}
  }
  string PackagePath(DesktopRelease release)=>Path.Combine(VelopackLocator.Current.PackagesDir,release.fileName);
  public async Task Check(){
   if(!CanUpdate||Busy||Staged)return;Busy=true;Status="Проверка обновлений…";
   try{
    source.MinimumSequence=minimumSequence;
    update=await manager.CheckForUpdatesAsync();
    Available=update!=null;
    if(Available){Status="Доступна версия "+NewVersion;minimumSequence=Math.Max(minimumSequence,source.Release.sequence);PlayerPrefs.SetInt("StarRacing.UpdateSequence."+track,checked((int)minimumSequence));PlayerPrefs.Save();}
    else Status="Установлена актуальная версия";
   }catch(Exception e){Available=false;Status="Не удалось проверить обновление. Можно играть офлайн.";Debug.LogWarning("DESKTOP_UPDATE_CHECK "+e);}
   finally{Busy=false;}
  }
  public async Task Download(){
   if(!CanUpdate||Busy||!Available||Staged||update==null)return;
   Busy=true;Interlocked.Exchange(ref progress,0);Status="Загрузка обновления";download=new CancellationTokenSource();
   try{
    source.DownloadConsent=true;
    string cached=PackagePath(source.Release);
    if(File.Exists(cached)){
     try{ReleaseAuthentication.VerifyFile(source.Release,cached);}catch(InvalidDataException){File.Delete(cached);}
    }
    await manager.DownloadUpdatesAsync(update,value=>Interlocked.Exchange(ref progress,value),download.Token);
    ReleaseAuthentication.VerifyFile(source.Release,PackagePath(source.Release));
    UpdateAttemptJournal.Save(receipt,source.Envelope);
    if(File.Exists(journal))File.Delete(journal);
    Debug.Log("DESKTOP_UPDATE_STAGED version="+NewVersion);
    Staged=true;Status="Готово. Перезапустите игру или установите при следующем запуске.";
   }catch(OperationCanceledException){Status="Загрузка отменена. Текущая версия доступна.";}
   catch(Exception e){Status="Не удалось загрузить обновление: "+SafeError(e);Debug.LogWarning("DESKTOP_UPDATE_DOWNLOAD "+e);}
   finally{source.DownloadConsent=false;Busy=false;download.Dispose();download=null;}
  }
  public void Cancel(){download?.Cancel();}
  public void InstallAndRestart(){Apply(false);}
  void TryApplyAtStartup(){Apply(true);}
  void Apply(bool startup){
   if(!Staged||Busy||!CanUpdate||(!startup&&!GetMenuState()))return;
   try{
    // A separate receipt is authenticated again immediately before activation.
    var release=ReleaseAuthentication.Verify(File.ReadAllText(receipt),appId,channel,minimumSequence,track);
    if(release.version!=NewVersion)throw new InvalidDataException("Согласованная версия изменилась");
    ReleaseAuthentication.VerifyFile(release,PackagePath(release));
    using(var mutex=new Mutex(false,"StarRacing-Apply-"+channel+"-"+track)){
     if(!mutex.WaitOne(0)){Status="Обновление применяется другой копией игры";return;}
     try{
      if(startup&&UpdateAttemptJournal.AlreadyAttempted(journal,source.Envelope,InstalledVersion,release.version)){Status="Предыдущая установка не завершилась. Можно играть или повторить вручную.";return;}
      if(Application.platform==RuntimePlatform.OSXPlayer){
       // User-scope package supports atomic bundle swap; never invoke non-atomic elevated fallback.
       var parent=Directory.GetParent(Application.dataPath)?.Parent?.FullName;
       var probe=Path.Combine(parent,".star-racing-update-"+Guid.NewGuid().ToString("N"));
       try{using(File.Create(probe)){}File.Delete(probe);}catch{Status="Для безопасного обновления переместите игру в ~/Applications. Текущая игра доступна.";return;}
      }
      UpdateAttemptJournal.Begin(journal,source.Envelope,InstalledVersion,release.version);
      PlayerPrefs.Save();StartupApplying=true;
      var args=Environment.GetCommandLineArgs().Skip(1).Where(x=>!x.StartsWith("--star-racing-apply-attempt=")).Concat(new[]{"--star-racing-apply-attempt="+release.version}).ToArray();
      Debug.Log("DESKTOP_UPDATE_APPLY_WAIT from="+InstalledVersion+" target="+release.version+" startup="+startup);
      manager.WaitExitThenApplyUpdates(release.Asset,false,true,args);
     }finally{mutex.ReleaseMutex();}
    }
    Application.Quit();
   }catch(Exception e){StartupApplying=false;Status="Обновление не установлено: "+SafeError(e);Debug.LogWarning("DESKTOP_UPDATE_APPLY "+e);}
  }
  static bool GetMenuState(){var director=FindFirstObjectByType<RaceDirector>();return director!=null&&director.InPreparationMenu;}
  static string SafeError(Exception error)=>error is IOException||error is InvalidDataException||error is CryptographicException?error.Message:"Проверьте подключение и свободное место";
  void OnDestroy(){download?.Cancel();source?.Dispose();if(Instance==this)Instance=null;}
 }
}

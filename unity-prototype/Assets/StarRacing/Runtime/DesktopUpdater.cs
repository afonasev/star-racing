using System;
using System.Collections.Generic;
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
 public sealed partial class DesktopUpdater:MonoBehaviour {
  public const string PreparedStatus="Обновление готово. Можно продолжить играть; новая версия установится при следующем запуске. Или нажмите «Перезапустить».";
  public static DesktopUpdater Instance {get;private set;}
  public string InstalledVersion {get;private set;}
  public string PublishedDate {get;private set;}
  public string Status {get;private set;}="Проверка обновлений…";
  public string NewVersion=>source?.Release?.version;
  public string Notes=>source?.Release?.notes??"";
  public float SizeMB=>(source?.Release?.size??0)/1048576f;
  public bool Busy {get;private set;}
  public bool Downloading=>download!=null;
  public bool Available {get;private set;}
  public bool Staged {get;private set;}
  public bool CanUpdate {get;private set;}
  public bool CurrentVersionConfirmed {get;private set;}
  public bool ShowUpdateBlock=>CanUpdate&&(!CurrentVersionConfirmed||Busy||Staged||StartupApplying);
  public bool StartupApplying {get;private set;}
  string journal;
  bool discoveryStarted,raceNetworkSuspended,resumeSelected,raceObserved;
  Task discoveryTask=Task.CompletedTask,downloadTask=Task.CompletedTask,dateTask=Task.CompletedTask,historyTask=Task.CompletedTask;
  public ReleaseHistoryEntry[] HistoryEntries {get;private set;}=new ReleaseHistoryEntry[0];
  public string HistoryStatus {get;private set;}="";
  public Task LoadHistory(){if(raceNetworkSuspended||!GetMenuState()||!historyTask.IsCompleted)return historyTask;return historyTask=ReadHistory();}
  async Task ReadHistory(){
   if(source==null){HistoryStatus="История доступна в установленной игре";return;}
   HistoryStatus="Загрузка истории…";
   try{HistoryEntries=await source.History(InstalledVersion);HistoryStatus="";}
   catch(OperationCanceledException){HistoryStatus="История приостановлена на время гонки";}
   catch(Exception e){HistoryStatus="История недоступна. Проверьте подключение.";Debug.LogWarning("DESKTOP_HISTORY "+e);}
  }
  public async Task SuspendForRace(){
   raceNetworkSuspended=true;resumeSelected|=Downloading;source?.SuspendNetwork();download?.Cancel();
   try{await Task.WhenAll(discoveryTask,downloadTask,dateTask,historyTask).ConfigureAwait(false);}catch(OperationCanceledException){}
  }
  public void ResumeInMenu(){
   if(!raceNetworkSuspended)return;raceNetworkSuspended=false;raceObserved=false;source?.ResumeNetwork();
   if(resumeSelected){resumeSelected=false;_=Download();}
  }
  void Update(){if(!raceNetworkSuspended)return;if(!GetMenuState())raceObserved=true;else if(raceObserved)ResumeInMenu();}

  readonly string track=DesktopReleaseChannel.Track;
  int progress;
  public int Progress=>Volatile.Read(ref progress);
  public string ButtonLabel=>StartupApplying?"Установка обновления…":Downloading?"Скачивание · "+Progress+"%":Busy?"Проверка обновлений…":Staged?"Установить и перезапустить":Available?"Обновить до "+NewVersion:"Обновления недоступны";
  public string VisibleStatus=>Downloading&&Progress<100?Status+" · "+Progress+"% из "+SizeMB.ToString("F1")+" МБ":Status;
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
   if(Application.isEditor||MenuReleaseSmoke.Enabled){Status="Обновления доступны в установленной игре";return;}
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
    PublishedDate=PlayerPrefs.GetString("StarRacing.Published."+InstalledVersion,"");
    if(ResumeRecovery())return;
    dateTask=ReadPublicationDate();
    Debug.Log("DESKTOP_UPDATE_READY version="+InstalledVersion+" track="+track+" installed="+CanUpdate);
    // Legacy receipts do not constitute a health/recovery transaction. Keep them for
    // diagnosis; normal discovery offers a fresh signed preparation with recovery.
    discoveryTask=Check();
   }catch(Exception e){Status="Обновление недоступно: "+SafeError(e);Debug.LogWarning("DESKTOP_UPDATE_INIT "+e);}
  }
  async Task ReadPublicationDate(){
   if(!string.IsNullOrEmpty(PublishedDate))return;
   try{var version=InstalledVersion;var date=await source.PublicationDate(version);if(this==null||InstalledVersion!=version)return;PublishedDate=date;PlayerPrefs.SetString("StarRacing.Published."+version,date);PlayerPrefs.Save();}
   catch(Exception e){Debug.LogWarning("DESKTOP_PUBLICATION_DATE unavailable: "+SafeError(e));}
  }
  string PackagePath(DesktopRelease release)=>Path.Combine(VelopackLocator.Current.PackagesDir,release.fileName);
  public async Task Check(){
   if(discoveryStarted||raceNetworkSuspended||!CanUpdate||Busy||Staged)return;discoveryStarted=true;CurrentVersionConfirmed=false;Busy=true;Status="Проверка обновлений…";
   try{
    source.MinimumSequence=minimumSequence;
    update=await manager.CheckForUpdatesAsync();
    Available=update!=null;CurrentVersionConfirmed=!Available;
    if(Available&&recovery?.state=="recovery-required"&&NewVersion==recovery.targetVersion){Available=false;Status=recovery.restored?"Обновление не запустилось. Восстановлена предыдущая версия.":"Требуется восстановление игры. Переустановите её; данные сохранятся.";return;}
    if(Available){Status="Доступна версия "+NewVersion+". Нажмите «Обновить», чтобы скачать изменения.";minimumSequence=Math.Max(minimumSequence,source.Release.sequence);PlayerPrefs.SetInt("StarRacing.UpdateSequence."+track,checked((int)minimumSequence));PlayerPrefs.Save();}
    else Status="Установлена актуальная версия";
   }catch(Exception e){Available=false;Status="Не удалось проверить обновление. Можно играть офлайн.";Debug.LogWarning("DESKTOP_UPDATE_CHECK "+e);}
   finally{Busy=false;}
  }
  public Task Download(){if(raceNetworkSuspended||Busy||Staged)return downloadTask;return downloadTask=DownloadSelected();}
  async Task DownloadSelected(){
   if(!CanUpdate||Busy||!Available||Staged||update==null)return;
   Busy=true;Interlocked.Exchange(ref progress,0);Status="Подключение к серверу…";download=new CancellationTokenSource();
   var cancellation=download;
   try{
    Debug.Log("DESKTOP_UPDATE_DOWNLOAD_BEGIN version="+NewVersion);
    Status="Подготовка пакета восстановления…";await PrepareBaseline(cancellation.Token);
    source.DownloadConsent=true;
    string cached=PackagePath(source.Release);
    if(File.Exists(cached)){
     Status="Проверка скачанного пакета…";
     try{await Task.Run(()=>ReleaseAuthentication.VerifyFile(source.Release,cached));}catch(InvalidDataException){File.Delete(cached);}
    }
    cancellation.Token.ThrowIfCancellationRequested();Status="Скачивание обновления";
    await manager.DownloadUpdatesAsync(update,value=>Interlocked.Exchange(ref progress,Math.Max(0,Math.Min(100,value))),cancellation.Token);
    cancellation.Token.ThrowIfCancellationRequested();Interlocked.Exchange(ref progress,100);Status="Проверка обновления…";
    await Task.Run(()=>ReleaseAuthentication.VerifyFile(source.Release,PackagePath(source.Release)));
    cancellation.Token.ThrowIfCancellationRequested();
    PrepareRecovery();
    UpdateAttemptJournal.Save(receipt,source.Envelope);
    if(File.Exists(journal))File.Delete(journal);
    Debug.Log("DESKTOP_UPDATE_STAGED version="+NewVersion);
    Staged=true;Status=PreparedStatus;
   }catch(OperationCanceledException){Status=raceNetworkSuspended?"Загрузка приостановлена на время гонки":"Загрузка отменена. Текущая версия доступна.";}
   catch(Exception e){Status="Не удалось загрузить обновление: "+SafeError(e);Debug.LogWarning("DESKTOP_UPDATE_DOWNLOAD "+e);}
   finally{source.DownloadConsent=false;Busy=false;cancellation.Dispose();download=null;}
  }
  public void Cancel(){resumeSelected=false;download?.Cancel();}
  public void InstallAndRestart(){var menu=FindFirstObjectByType<RaceMenu>();if(menu!=null)menu.GuardExit(Apply);else Apply();}
  void Apply(){
   if(!Staged||Busy||StartupApplying||!CanUpdate)return;
   if(!GetMenuState()){Status="Вернитесь в главное меню для установки обновления";return;}
   try{
    // A separate receipt is authenticated again immediately before activation.
    var expectedVersion=NewVersion;
    var envelope=File.ReadAllText(receipt);
    source.MinimumSequence=minimumSequence;
    var release=source.VerifyDescriptor(envelope);
    if(release.version!=expectedVersion)throw new InvalidDataException("Согласованная версия изменилась");
    ReleaseAuthentication.VerifyFile(release,PackagePath(release));
    using(var mutex=new Mutex(false,"StarRacing-Apply-"+channel+"-"+track)){
     if(!mutex.WaitOne(0)){Status="Обновление применяется другой копией игры";return;}
     try{
      if(Application.platform==RuntimePlatform.OSXPlayer){
       // User-scope package supports atomic bundle swap; never invoke non-atomic elevated fallback.
       var parent=Directory.GetParent(Application.dataPath)?.Parent?.FullName;
       var probe=Path.Combine(parent,".star-racing-update-"+Guid.NewGuid().ToString("N"));
       try{using(File.Create(probe)){}File.Delete(probe);}catch{Status="Для безопасного обновления переместите игру в ~/Applications. Текущая игра доступна.";return;}
      }
      UpdateAttemptJournal.Begin(journal,envelope,InstalledVersion,release.version);
      PlayerPrefs.Save();StartupApplying=true;Status="Установка обновления. Игра перезапустится…";
      Debug.Log("DESKTOP_UPDATE_APPLY_WAIT from="+InstalledVersion+" target="+release.version+" startup=false");
      recovery=RecoveryTransaction.Read(RecoveryPath);if(recovery.root!=VelopackLocator.Current.RootAppDir||recovery.previousVersion!=InstalledVersion||UpdateAttemptJournal.Digest(recovery.targetEnvelope)!=UpdateAttemptJournal.Digest(envelope))throw new InvalidDataException("Подготовленная версия или установка изменилась");RecoveryTransaction.Verified(recovery,true);RecoveryTransaction.Verified(recovery,false);StartRecoveryAgent();
     }finally{mutex.ReleaseMutex();}
    }
    Application.Quit();
   }catch(Exception e){StartupApplying=false;Status="Обновление не установлено: "+SafeError(e);Debug.LogWarning("DESKTOP_UPDATE_APPLY "+e);}
  }
  internal static string[] RestartArguments(IEnumerable<string> current,string version)=>current.Where(x=>!x.StartsWith("--star-racing-apply-attempt=")).Concat(new[]{"--star-racing-apply-attempt="+version}).ToArray();
  RaceDirector menuDirector;
  bool GetMenuState(){if(menuDirector==null)menuDirector=FindFirstObjectByType<RaceDirector>();return menuDirector!=null&&menuDirector.InPreparationMenu;}
  static string SafeError(Exception error)=>error is UnauthorizedAccessException?"Нет доступа к папке обновления":error is IOException||error is InvalidDataException||error is CryptographicException||error is TimeoutException?error.Message:"Проверьте подключение и свободное место";
  void OnDestroy(){download?.Cancel();source?.Dispose();if(Instance==this)Instance=null;}
 }
}

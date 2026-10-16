using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Velopack.Locators;
using UnityEngine;
namespace StarRacingPrototype.Distribution {
 public sealed partial class DesktopUpdater {
  string RecoveryPath=>Path.Combine(Application.persistentDataPath,"desktop-recovery-"+track,"transaction.json");
  RecoveryRecord recovery;
  string BaselineEnvelope;
  async Task PrepareBaseline(CancellationToken token){
   BaselineEnvelope=await source.RecoveryBaseline(InstalledVersion,token);
   string packages=Path.Combine(Path.GetDirectoryName(RecoveryPath),"packages");Directory.CreateDirectory(packages);
   using(var baseline=new AuthenticatedUpdateSource(appId,channel,0,track)){
    baseline.Restore(BaselineEnvelope);string path=Path.Combine(packages,baseline.Release.fileName);
    if(File.Exists(path)){try{ReleaseAuthentication.VerifyFile(baseline.Release,path);return;}catch(InvalidDataException){File.Delete(path);}}
    long needed=checked(source.Release.size*2+baseline.Release.size*2+268435456);
    if(new DriveInfo(Path.GetPathRoot(packages)).AvailableFreeSpace<needed)throw new IOException("Недостаточно места для обновления и пакета восстановления");
    string current=Path.Combine(VelopackLocator.Current.PackagesDir,baseline.Release.fileName);
    if(File.Exists(current)){ReleaseAuthentication.VerifyFile(baseline.Release,current);File.Copy(current,path,true);}
    else{baseline.DownloadConsent=true;await baseline.DownloadReleaseEntry(null,baseline.Release.Asset,path,null,token);}
   }
  }
  void PrepareRecovery(){
   using(var ownership=new FileStream(RecoveryPath+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
   var locator=VelopackLocator.Current;string folder=Path.GetDirectoryName(RecoveryPath),packages=Path.Combine(folder,"packages");
   File.Copy(PackagePath(source.Release),Path.Combine(packages,source.Release.fileName),true);
   string content=locator.AppContentDir;
   string runtime=channel=="win-x64"?"win-x64":System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().Equals("Arm64",StringComparison.OrdinalIgnoreCase)?"osx-arm64":"osx-x64";
   string sourceAgent=Path.Combine(content,"recovery",runtime);string agent=Path.Combine(folder,"agent-"+InstalledVersion);
   CopyAgent(sourceAgent,agent);
   string helper=Path.Combine(agent,Path.GetFileName(locator.UpdateExePath));File.Copy(locator.UpdateExePath,helper,true);
   using(var process=Process.GetCurrentProcess()){
    recovery=new RecoveryRecord{appId=appId,channel=channel,track=track,previousVersion=InstalledVersion,targetVersion=NewVersion,previousEnvelope=BaselineEnvelope,targetEnvelope=source.Envelope,
     root=locator.RootAppDir,packages=packages,helper=helper,executable=process.MainModule.FileName,parentPid=process.Id,parentStarted=process.StartTime.ToUniversalTime().Ticks,
     arguments=RestartArguments(Environment.GetCommandLineArgs().Skip(1),NewVersion),token=Guid.NewGuid().ToString("N")};
   }
   RecoveryTransaction.Verified(recovery,true);RecoveryTransaction.Verified(recovery,false);RecoveryTransaction.Save(RecoveryPath,recovery);
   }
  }
  static void CopyAgent(string from,string to){
   if(!Directory.Exists(from))throw new IOException("Компонент безопасного восстановления отсутствует. Переустановите игру.");
   Directory.CreateDirectory(to);foreach(string file in Directory.GetFiles(from))File.Copy(file,Path.Combine(to,Path.GetFileName(file)),true);
   foreach(string directory in Directory.GetDirectories(from))CopyAgent(directory,Path.Combine(to,Path.GetFileName(directory)));
  }
  void StartRecoveryAgent(){
   string folder=Path.GetDirectoryName(recovery.helper);string exe=Path.Combine(folder,channel=="win-x64"?"StarRacingRecovery.exe":"StarRacingRecovery");
   if(!File.Exists(exe))throw new IOException("Не найден компонент восстановления");
   using(var child=Process.Start(new ProcessStartInfo(exe,Quote(RecoveryPath)+" "+Quote(recovery.token)){UseShellExecute=false,WorkingDirectory=folder,CreateNoWindow=true})){if(child==null)throw new IOException("Не удалось запустить компонент восстановления");}
  }
  internal static string Quote(string value){
   var result=new System.Text.StringBuilder("\"");int slashes=0;
   foreach(char c in value){if(c=='\\'){slashes++;continue;}if(c=='"'){result.Append('\\',slashes*2+1).Append(c);}else{result.Append('\\',slashes).Append(c);}slashes=0;}
   return result.Append('\\',slashes*2).Append('"').ToString();
  }

  bool ResumeRecovery(){
   if(!File.Exists(RecoveryPath))return false;recovery=RecoveryTransaction.Read(RecoveryPath);
   if(recovery.root!=VelopackLocator.Current.RootAppDir)throw new InvalidDataException("Подготовлено обновление другой копии игры. Запустите её для завершения.");
   if(recovery.state=="healthy")return false;
   if(recovery.state=="pending-health"&&Environment.GetCommandLineArgs().Contains("--star-racing-health-token="+recovery.token)&&InstalledVersion==recovery.targetVersion)return false;
   if(recovery.state=="recovery-required"&&recovery.recoveryAttempted){Status=recovery.restored?"Обновление не запустилось. Восстановлена предыдущая версия.":"Требуется восстановление игры. Переустановите её; пользовательские данные сохранятся.";return false;}
   try{using(var ownership=new FileStream(RecoveryPath+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){} }
   catch(IOException){StartupApplying=true;Application.Quit();return true;}
   using(var process=Process.GetCurrentProcess()){recovery.parentPid=process.Id;recovery.parentStarted=process.StartTime.ToUniversalTime().Ticks;}
   RecoveryTransaction.Save(RecoveryPath,recovery);StartRecoveryAgent();StartupApplying=true;Application.Quit();return true;
  }
  public void ConfirmReadyMenu(){
   if(Application.isEditor||recovery==null||recovery.state!="pending-health")return;
   if(!Environment.GetCommandLineArgs().Contains("--star-racing-health-token="+recovery.token)||InstalledVersion!=recovery.targetVersion)return;
   try{
    recovery=RecoveryTransaction.Read(RecoveryPath);
    var lab=DesignLabRuntime.Current;if(lab.Active==null||lab.Warning!=""||lab.CatalogError!=""||UserSettingsStore.LastError!="")return;
    lab.ReleaseDefault(); // Validate the protected release Default in addition to loaded user data.
    using(var process=Process.GetCurrentProcess())RecoveryTransaction.Acknowledge(RecoveryPath,recovery,InstalledVersion,recovery.token,process.Id,process.StartTime.ToUniversalTime().Ticks);
    recovery=null;
   }catch(Exception e){UnityEngine.Debug.LogWarning("DESKTOP_HEALTH_NOT_READY "+e.Message);}
  }
 }
}

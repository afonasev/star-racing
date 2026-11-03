using System.Diagnostics;
using StarRacingPrototype.Distribution;
using Velopack;
using Velopack.Locators;
using Velopack.NuGet;

if(args.Length>0&&args[0].StartsWith("--",StringComparison.Ordinal)){
 try{
  if(!OperatingSystem.IsWindows())throw new IOException("Installer operations require Windows");
  switch(args[0]){
   case "--validate-install-token" when args.Length==2:WindowsInstall.ValidToken(args[1]);break;
   case "--new-install-token" when args.Length==1:Console.Write(Guid.NewGuid().ToString("N"));break;
   case "--inspect-install" when args.Length==3:Console.Write(WindowsInstall.Inspect(args[1],args[2]));break;
   case "--prepare-install" when args.Length==5:Console.Write(WindowsInstall.Prepare(args[1],args[2],args[3],args[4]));break;
   case "--remove-install" when args.Length==2:WindowsInstall.RemoveInstalled(args[1]);break;
   case "--commit-install" when args.Length==3:WindowsInstall.Commit(args[1],args[2]);break;
   case "--rollback-install" when args.Length==3:WindowsInstall.Rollback(args[1],args[2]);break;
   default:throw new IOException("Unknown installer operation");
  }
  return 0;
 }catch(Exception error){Console.Error.Write(error.Message);return 1;}
}

if((args.Length!=1&&args.Length!=2)||!Path.IsPathRooted(args[0]))return 2;
string journal=args[0];RecoveryRecord record=null;
try {
 using var ownership=new FileStream(journal+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
 var loaded=RecoveryTransaction.Read(journal);
 if(args.Length==2&&args[1]!=loaded.token)return 2;
 record=loaded;
 if(record.state=="healthy"||record.state=="recovery-required"&&record.recoveryAttempted)return 0;
 var target=RecoveryTransaction.Verified(record,false);var previous=RecoveryTransaction.Verified(record,true);
 await WaitForOriginal(record);
 if(record.state=="prepared"){
  record.state="applying";RecoveryTransaction.Save(journal,record);
  await Apply(record,target);
  record.state="pending-health";record.token=Guid.NewGuid().ToString("N");RecoveryTransaction.Save(journal,record);
  if(File.Exists(journal+".child"))File.Delete(journal+".child");
  var launch=StartGame(record,true,journal);var game=launch.Process;
  if(launch.Launcher){record.launcherPid=game.Id;record.launcherStarted=game.StartTime.ToUniversalTime().Ticks;record.gamePid=0;record.gameStarted=0;}
  else{record.gamePid=game.Id;record.gameStarted=game.StartTime.ToUniversalTime().Ticks;}
  RecoveryTransaction.Save(journal,record);
 }else if(record.state=="applying")throw new IOException("Activation interrupted before health monitoring");
 if(record.state=="pending-health"){
  var until=DateTime.UtcNow.AddMinutes(2);
  while(true){
   if(record.launcherPid>0&&record.gamePid==0){
    var child=RecoveryTransaction.Child(journal,record);
    if(child!=null){
     using var player=Process.GetProcessById(child.pid);
     if(player.StartTime.ToUniversalTime().Ticks!=child.started||!string.Equals(Path.GetFullPath(player.MainModule!.FileName),Path.GetFullPath(record.executable),OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))throw new IOException("Launcher reported a different Player");
     record.gamePid=child.pid;record.gameStarted=child.started;RecoveryTransaction.Save(journal,record);File.Delete(journal+".child");
    }
   }
   if(RecoveryTransaction.HasHealth(journal,record)){
    record.state="healthy";RecoveryTransaction.Save(journal,record);
    // Signed recovery package is retained outside Velopack cleanup until this exact ack.
    try{File.Delete(Path.Combine(record.packages,previous.fileName));File.Delete(journal+".health");}
    catch(Exception cleanup) when(cleanup is IOException||cleanup is UnauthorizedAccessException){Console.Error.WriteLine("Healthy; deferred recovery-package cleanup: "+cleanup.Message);}
    return 0;
   }
   if(!(Alive(record.gamePid,record.gameStarted)||record.gamePid==0&&Alive(record.launcherPid,record.launcherStarted))||DateTime.UtcNow>=until)throw new IOException("Updated game exited or did not confirm data, Default and ready menu");
   await Task.Delay(100);
  }
 }
 throw new IOException("Recovery requested");
} catch(Exception error){
 if(record==null){Console.Error.WriteLine(error);return 1;}
 try{
  record.state="recovery-required";record.error=error.Message;RecoveryTransaction.Save(journal,record);
  // Never replace a running game, including one that timed out during startup.
  if(Alive(record.gamePid,record.gameStarted)||Alive(record.launcherPid,record.launcherStarted))return 1;
  using var ownership=new FileStream(journal+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
  if(record.recoveryAttempted)return 1;
  record.recoveryAttempted=true;RecoveryTransaction.Save(journal,record);
  var previous=RecoveryTransaction.Verified(record,true);
  await WaitForOriginal(record);await Apply(record,previous);
  record.restored=true;RecoveryTransaction.Save(journal,record);StartGame(record,false,journal);return 1;
 }catch(Exception recoveryError){record.error+="; recovery: "+recoveryError.Message;RecoveryTransaction.Save(journal,record);Console.Error.WriteLine(record.error);return 1;}
}

static bool Alive(int pid,long started){if(pid<=0||started<=0)return false;try{using var p=Process.GetProcessById(pid);return !p.HasExited&&p.StartTime.ToUniversalTime().Ticks==started;}catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}}
static async Task WaitForOriginal(RecoveryRecord r){
 while(Alive(r.parentPid,r.parentStarted)||Alive(r.parentLauncherPid,r.parentLauncherStarted)||GameStillRunning(r))await Task.Delay(100);
}
static bool GameStillRunning(RecoveryRecord r){
 // A supervising launcher may still be unwinding after its Player exited.
 // Wait for it before taking the install gate; never replace a live supervisor.
 foreach(var launcher in Process.GetProcessesByName("StarRacingLauncher"))using(launcher){
  if(launcher.Id==Environment.ProcessId)continue;
  try{if(Path.GetFullPath(launcher.MainModule!.FileName).StartsWith(Path.GetFullPath(r.root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))return true;}
  catch(ArgumentException){}catch(InvalidOperationException){}
 }
 string expected=Path.GetFileNameWithoutExtension(r.executable);
 foreach(var process in Process.GetProcessesByName(expected))using(process){
  if(process.Id==Environment.ProcessId)continue;
  try{if(!string.Equals(process.ProcessName,expected,StringComparison.OrdinalIgnoreCase))continue;
   if(string.Equals(Path.GetFullPath(process.MainModule!.FileName),Path.GetFullPath(r.executable),OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))return true;
  }catch(ArgumentException){}catch(InvalidOperationException){} // process exited during inspection
 }
 return false;
}
static (Process Process,bool Launcher) StartGame(RecoveryRecord r,bool target,string journal){
 string launcher=Path.Combine(Path.GetDirectoryName(r.executable)!,OperatingSystem.IsWindows()?"StarRacingLauncher.exe":"StarRacingLauncher");
 bool throughLauncher=target&&File.Exists(launcher);
 var start=new ProcessStartInfo(throughLauncher?launcher:r.executable){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(r.executable)!};
 start.Environment["STAR_RACING_LAUNCHER_PLAYER"]="1";
 foreach(var name in new[]{"STAR_RACING_LAUNCHER_READY","STAR_RACING_LAUNCHER_TOKEN","STAR_RACING_LAUNCHER_VERSION","STAR_RACING_RECOVERY_LAUNCHER","STAR_RACING_RECOVERY_TOKEN","STAR_RACING_LAUNCHER_PID","STAR_RACING_LAUNCHER_STARTED"})start.Environment.Remove(name);
 if(throughLauncher){start.Environment["STAR_RACING_RECOVERY_LAUNCHER"]=journal;start.Environment["STAR_RACING_RECOVERY_TOKEN"]=r.token;}
 foreach(var arg in r.arguments.Where(x=>!x.StartsWith("--star-racing-health-token=",StringComparison.Ordinal)))start.ArgumentList.Add(arg);
 if(target)start.ArgumentList.Add("--star-racing-health-token="+r.token);
 return (Process.Start(start)??throw new IOException("Cannot start game"),throughLauncher);
}
static async Task Apply(RecoveryRecord r,DesktopRelease release){
 using var gate=GameInstallGate.Acquire();
 // SDK supplies its pinned platform-specific command and paths; no reconstructed native CLI.
 var process=new NativeProcess(r.parentPid);var locator=new Locator(r,process);
 using var source=new AuthenticatedUpdateSource(r.appId,r.channel,0,r.track);
 var manager=new UpdateManager(source,new UpdateOptions{ExplicitChannel=r.channel,AllowVersionDowngrade=true},locator);
 manager.WaitExitThenApplyUpdates(release.Asset,false,false,Array.Empty<string>());
 using var helper=process.Child??throw new IOException("Native updater did not start");await helper.WaitForExitAsync();
 if(helper.ExitCode!=0)throw new IOException("Native apply failed: "+helper.ExitCode);
 var deadline=DateTime.UtcNow.AddSeconds(60);
 while(true){
  string metadata=Path.Combine(Path.GetDirectoryName(r.executable)!,"sq.version");
  if(!File.Exists(metadata)&&r.channel=="osx-universal")metadata=Path.Combine(r.root,"Contents","Resources","sq.version");
  if(File.Exists(r.executable)&&PackageManifest.TryParseFromFile(metadata,out var manifest)&&manifest.Id==r.appId&&manifest.Channel==r.channel&&manifest.Version==SemanticVersion.Parse(release.version))break;
  if(DateTime.UtcNow>=deadline)throw new IOException("Native helper exited without the requested installed version");
  await Task.Delay(100);
 }

}
sealed class NativeProcess:IProcessImpl {
 readonly uint waiting;public Process Child;
 public NativeProcess(int pid){waiting=checked((uint)pid);}
 public string GetCurrentProcessPath()=>Environment.ProcessPath!;public uint GetCurrentProcessId()=>waiting;
 public void StartProcess(string exe,IEnumerable<string> args,string dir,bool window){var info=new ProcessStartInfo(exe){UseShellExecute=false,WorkingDirectory=dir,CreateNoWindow=!window};foreach(var arg in args)info.ArgumentList.Add(arg);Child=Process.Start(info);}
 public void Exit(int code)=>throw new InvalidOperationException("Unexpected SDK exit");
}
sealed class Locator:VelopackLocator {
 readonly RecoveryRecord record;readonly IProcessImpl process;
 public Locator(RecoveryRecord r,IProcessImpl p){record=r;process=p;}
 public override string AppId=>record.appId;public override string RootAppDir=>record.root;
 public override string PackagesDir=>record.packages;public override string UpdateExePath=>record.helper;
 public override string AppContentDir=>Path.GetDirectoryName(record.executable)!;public override string Channel=>record.channel;
 public override SemanticVersion CurrentlyInstalledVersion=>SemanticVersion.Parse(record.previousVersion);public override IProcessImpl Process=>process;
}

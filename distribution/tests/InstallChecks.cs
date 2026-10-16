using Newtonsoft.Json;
using StarRacingPrototype.Distribution;
static class InstallChecks {
 public static void Run(Action<bool,string> require){
  string root=Path.Combine(Path.GetTempPath(),"star-install-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  string target=Path.Combine(root,"Game"),data=Path.Combine(root,"user-data");File.WriteAllText(data,"settings and Lab");
  string stage(string token)=>target+".sr-stage-"+token;
  void Payload(string dir,string version,string label){
   Directory.CreateDirectory(Path.Combine(dir,"current"));Directory.CreateDirectory(Path.Combine(dir,"packages"));
   File.WriteAllText(Path.Combine(dir,"current","sq.version"),$"<package><metadata><id>tech.afonasev.star-racing.win-x64</id><version>{version}</version><channel>win-x64</channel></metadata></package>");
   foreach(string node in WindowsInstall.Nodes.Where(x=>x!="current"&&x!="packages"))File.WriteAllText(Path.Combine(dir,node),node=="installer.complete"?"StarRacingDesktop-v1":label+node);
   File.WriteAllText(Path.Combine(dir,"current","game.bin"),label);File.WriteAllText(Path.Combine(dir,"packages","cached"),label);
  }
  string Snapshot()=>string.Join("\n",Directory.GetFiles(target,"*",SearchOption.AllDirectories).OrderBy(x=>x).Select(x=>Path.GetRelativePath(target,x)+":"+Convert.ToBase64String(File.ReadAllBytes(x))));
  void Reject(Action action,string label){bool failed=false;try{action();}catch{failed=true;}require(failed,label);}
  try{
   require(WindowsInstall.Inspect(target,"1.0.0")=="N","empty new install");
   Payload(target,"1.0.0","old");File.WriteAllText(Path.Combine(target,"foreign.txt"),"do not delete");
   require(WindowsInstall.Inspect(target,"1.0.0")=="R1.0.0","same version repair offered");
   require(WindowsInstall.Inspect(target,"1.1.0")=="U1.0.0","upgrade offered with installed version");
   Reject(()=>WindowsInstall.Inspect(target,"0.9.0"),"downgrade refused");
   Reject(()=>WindowsInstall.Inspect(target,"1.1.0-test.1"),"channel crossing refused");
   string before=Snapshot();
   // Execute actual transaction and fail after EVERY individual old/new move.
   foreach(string point in WindowsInstall.Nodes.SelectMany(x=>new[]{"backup:"+x,"install:"+x})){
    string token=Guid.NewGuid().ToString("N");Payload(stage(token),"1.1.0","new");bool reached=false;
    Reject(()=>WindowsInstall.Prepare(stage(token),target,"1.1.0",token,p=>{if(p==point){reached=true;throw new IOException("injected "+p);}}),"move fault "+point);
    require(reached&&Snapshot()==before,"exact rollback "+point);
    require(!Directory.Exists(stage(token))&&!File.Exists(target+".sr-install.json"),"rollback cleans transaction "+point);
   }
   string upgrade="upgrade";Payload(stage(upgrade),"1.1.0","new");WindowsInstall.Prepare(stage(upgrade),target,"1.1.0",upgrade);
   require(File.ReadAllText(Path.Combine(target,"current","game.bin"))=="new","prepared payload visible");
   Reject(()=>WindowsInstall.Commit(target,"stale"),"foreign completion token rejected");
   WindowsInstall.Commit(target,upgrade);
   require(File.ReadAllText(Path.Combine(target,"installer.receipt"))==upgrade,"current attempt receipt");
   require(WindowsInstall.Inspect(target,"1.1.0")=="R1.1.0","upgrade confirmed");
   require(File.ReadAllText(Path.Combine(target,"foreign.txt"))=="do not delete"&&File.ReadAllText(data)=="settings and Lab","foreign files and external user data preserved");
   // Repair missing owned file from the same version; no invented installed version.
   File.Delete(Path.Combine(target,"Update.exe"));Payload(stage("repair"),"1.1.0","repair");WindowsInstall.Prepare(stage("repair"),target,"1.1.0","repair");WindowsInstall.Commit(target,"repair");
   require(File.ReadAllText(Path.Combine(target,"Update.exe"))=="repairUpdate.exe","same-version repair restores owned file");
   before=Snapshot();Payload(stage("interrupted"),"1.2.0","next");WindowsInstall.Prepare(stage("interrupted"),target,"1.2.0","interrupted");
   require(WindowsInstall.Inspect(target,"1.2.0")=="P","pending transaction identified before wizard");
   // Simulate process exit after prepare: next attempt must recover prior bytes first.
   Payload(stage("retry"),"1.2.0","retry");WindowsInstall.Prepare(stage("retry"),target,"1.2.0","retry");WindowsInstall.Rollback(target,"retry");
   require(Snapshot()==before,"next attempt recovers interrupted transaction exactly");
   Payload(stage("escape"),"1.2.0","bad");Reject(()=>WindowsInstall.Prepare(stage("escape"),target,"1.2.0","../escape"),"invalid token cannot escape");
   string foreign=Path.Combine(root,"Other");Directory.CreateDirectory(foreign);File.WriteAllText(Path.Combine(foreign,"x"),"foreign");Reject(()=>WindowsInstall.Inspect(foreign,"1.0.0"),"foreign nonempty directory refused");
   if(!OperatingSystem.IsWindows()){
    File.CreateSymbolicLink(Path.Combine(stage("escape"),"current","link"),data);
    Reject(()=>WindowsInstall.Prepare(stage("escape"),target,"1.2.0","escape"),"symlink payload refused");
    File.Delete(Path.Combine(stage("escape"),"current","link"));
   }
   require(Snapshot()==before,"all rejections preserve original installed bytes");
   WindowsInstall.RemoveInstalled(target);
   require(File.ReadAllText(Path.Combine(target,"foreign.txt"))=="do not delete"&&Directory.GetFiles(target).Length==1,"uninstall removes only owned nodes");
   require(File.ReadAllText(data)=="settings and Lab","uninstall preserves user data");
  }finally{Directory.Delete(root,true);}
 }
}

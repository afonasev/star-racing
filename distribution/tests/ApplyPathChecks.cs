using Velopack;
using Velopack.Locators;
using Velopack.NuGet;
using StarRacingPrototype.Distribution;
static class ApplyPathChecks {
 public static void Run(Action<bool,string> require){
  var directory=Path.Combine(Path.GetTempPath(),"star-racing-apply-path-"+Guid.NewGuid().ToString("N"));
  try{
   var root=Path.Combine(directory,"Program Files","Star Racing");
   var fallback=Path.Combine(directory,"Users","Test User Кириллица","AppData","Local","tech.afonasev.star-racing.win-x64");
   var packages=Path.Combine(fallback,"packages");Directory.CreateDirectory(packages);Directory.CreateDirectory(root);
   File.WriteAllText(Path.Combine(fallback,"Update.exe"),"probe");File.WriteAllText(Path.Combine(packages,"release-full.nupkg"),"probe");
   var process=new ProcessProbe();var locator=new Locator(root,packages,Path.Combine(fallback,"Update.exe"),process);
   using var source=new AuthenticatedUpdateSource(locator.AppId!,"win-x64",1);
   var manager=new UpdateManager(source,null,locator);
   var release=new VelopackAsset{FileName="release-full.nupkg",Version=SemanticVersion.Parse("0.3.4"),Type=VelopackAssetType.Full};
   manager.WaitExitThenApplyUpdates(release,false,true,new[]{"-noaudio","--star-racing-apply-attempt=0.3.4"});
   require(process.Executable==locator.UpdateExePath,"apply uses fallback helper");
   require(process.WorkingDirectory==fallback,"helper working directory is independent from installation root");
   require(process.Arguments.SequenceEqual(new[]{"apply","--package",Path.Combine(packages,release.FileName),"--waitPid","12345","--rootDir",root,"--packageDir",packages,"--","-noaudio","--star-racing-apply-attempt=0.3.4"}),"SDK keeps spaced unicode paths as separate complete args and explicit installation root");
  }finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
 }
 sealed class ProcessProbe:IProcessImpl {
  public string Executable="",WorkingDirectory="";public string[] Arguments=Array.Empty<string>();
  public string GetCurrentProcessPath()=>"probe";public uint GetCurrentProcessId()=>12345;
  public void StartProcess(string exe,IEnumerable<string> args,string dir,bool window){Executable=exe;Arguments=args.ToArray();WorkingDirectory=dir;}
  public void Exit(int code)=>throw new Exception("Probe must not exit");
 }
 sealed class Locator:VelopackLocator {
  readonly ProcessProbe process;
  public Locator(string root,string packages,string helper,ProcessProbe process){RootAppDir=root;PackagesDir=packages;UpdateExePath=helper;this.process=process;}
  public override string AppId=>"tech.afonasev.star-racing.win-x64";
  public override string RootAppDir{get;} public override string PackagesDir{get;} public override string UpdateExePath{get;}
  public override string AppContentDir=>Path.Combine(RootAppDir,"current"); public override string Channel=>"win-x64";
  public override SemanticVersion CurrentlyInstalledVersion=>SemanticVersion.Parse("0.3.3");public override IProcessImpl Process=>process;
 }
}

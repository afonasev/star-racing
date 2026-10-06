using System;
using System.IO;
using StarRacingPrototype.Distribution;
using Velopack;
class MonoProbe {
 static void Main(string[] args){
  VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
  var release=ReleaseAuthentication.Verify(File.ReadAllText(args[0]),"tech.afonasev.star-racing.osx-universal","osx-universal",1);
  var manager=new UpdateManager(new AuthenticatedUpdateSource(release.appId,release.channel,1),new UpdateOptions{ExplicitChannel=release.channel});
  if(args.Length>1){
   var checksum=typeof(UpdateManager).GetMethod("VerifyPackageChecksumAsync",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
   if(checksum==null)throw new Exception("Pinned SDK checksum method missing");
   ((System.Threading.Tasks.Task)checksum.Invoke(manager,new object[]{release.Asset,args[1]})).GetAwaiter().GetResult();
   Console.WriteLine("MONO_SDK_CHECKSUM_OK "+release.version);
  }
  Console.WriteLine("MONO_AUTH_PROBE_OK version="+release.version+" installed="+manager.IsInstalled);
 }
}

using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace StarRacingPrototype.Distribution {
 public sealed class RecoveryRecord {
  public int schema=1,parentPid,gamePid;
  public long parentStarted,gameStarted;
  public string state="prepared",appId,channel,track,previousVersion,targetVersion,previousEnvelope,targetEnvelope;
  public string root,packages,helper,executable,token,error="";
  public string[] arguments=new string[0];
  public bool recoveryAttempted,restored;
 }
 public sealed class RecoveryHealth {public int schema=1,pid;public string version,token;public long started;}
 public static class RecoveryTransaction {
  public static void Atomic(string path,string text){
   Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
   try{
    using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){var bytes=new UTF8Encoding(false,true).GetBytes(text);file.Write(bytes,0,bytes.Length);file.Flush(true);}
    if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
   }finally{if(File.Exists(temp))File.Delete(temp);}
  }
  public static RecoveryRecord Read(string path){
   string json=File.ReadAllText(path);if(json.Length>1048576)throw new InvalidDataException("Recovery journal too large");
   // Reject duplicate state/identity fields instead of silently changing an interrupted transaction.
   JObject value;using(var reader=new JsonTextReader(new StringReader(json))){
    value=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});if(reader.Read())throw new InvalidDataException("Trailing recovery journal");
   }
   if(value["schema"]?.Type!=JTokenType.Integer||(int)value["schema"]!=1||value["state"]?.Type!=JTokenType.String)throw new InvalidDataException("Missing recovery schema/state");
   var record=value.ToObject<RecoveryRecord>();Validate(record);return record;
  }
  public static void Validate(RecoveryRecord r){
   if(r==null||r.schema!=1||Array.IndexOf(new[]{"prepared","applying","pending-health","healthy","recovery-required"},r.state)<0)throw new InvalidDataException("Invalid recovery journal");
   if(r.track!="production"&&r.track!="test"||r.appId!="tech.afonasev.star-racing."+r.channel||r.channel!="win-x64"&&r.channel!="osx-universal"||r.previousVersion==r.targetVersion)throw new InvalidDataException("Invalid recovery identity");
   foreach(string path in new[]{r.root,r.packages,r.helper,r.executable})if(string.IsNullOrEmpty(path)||!Path.IsPathRooted(path))throw new InvalidDataException("Recovery paths must be absolute");
   if(r.arguments==null||string.IsNullOrEmpty(r.token)||r.token.Length<32)throw new InvalidDataException("Invalid recovery launch");
  }
  public static void Save(string path,RecoveryRecord record){Validate(record);Atomic(path,JsonConvert.SerializeObject(record));}
  public static DesktopRelease Verified(RecoveryRecord r,bool previous,string modulus=DesktopUpdateKey.Modulus){
   var release=ReleaseAuthentication.Verify(previous?r.previousEnvelope:r.targetEnvelope,r.appId,r.channel,0,r.track,modulus);
   if(release.version!=(previous?r.previousVersion:r.targetVersion))throw new InvalidDataException("Recovery package identity changed");
   ReleaseAuthentication.VerifyFile(release,Path.Combine(r.packages,release.fileName));return release;
  }
  public static void Acknowledge(string path,RecoveryRecord r,string version,string token,int pid,long started){
   if(r.state!="pending-health"||version!=r.targetVersion||token!=r.token||pid!=r.gamePid||Math.Abs(started-r.gameStarted)>TimeSpan.TicksPerSecond)throw new InvalidDataException("Health identity mismatch");
   Atomic(path+".health",JsonConvert.SerializeObject(new RecoveryHealth{version=version,token=token,pid=pid,started=r.gameStarted}));
  }
  public static bool HasHealth(string path,RecoveryRecord r){
   if(!File.Exists(path+".health"))return false;
   try{var h=ReleaseAuthentication.Parse(File.ReadAllText(path+".health")).ToObject<RecoveryHealth>();return h.schema==1&&h.version==r.targetVersion&&h.token==r.token&&h.pid==r.gamePid&&h.started==r.gameStarted;}
   catch(Exception e) when(e is IOException||e is JsonException){return false;}
  }
 }
}

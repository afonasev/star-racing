using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using System.Text;
using StarRacingPrototype.Distribution;
using Velopack;
int assertions=0;
void Require(bool condition,string label){assertions++;if(!condition)throw new Exception(label);}
void Reject(Action action,string label){try{action();}catch(Exception){assertions++;return;}throw new Exception("Accepted "+label);}
using var rsa=RSA.Create(3072);
string modulus=Convert.ToBase64String(rsa.ExportParameters(false).Modulus!);
var data=new JObject{["schema"]=2,["appId"]="tech.afonasev.star-racing.win-x64",["channel"]="win-x64",["releaseTrack"]="test",["version"]="0.2.1-test.2",["sequence"]=2,["fileName"]="racing-0.2.1-test.2-full.nupkg",["size"]=3,["sha256"]=Convert.ToHexString(SHA256.HashData(new byte[]{1,2,3})),["url"]="https://github.com/afonasev/star-racing/releases/download/v0.2.1-test.2/racing-0.2.1-test.2-full.nupkg",["notes"]="test"};
string Envelope(JObject obj){byte[] payload=Encoding.UTF8.GetBytes(obj.ToString());return new JObject{["keyId"]=DesktopUpdateKey.Id,["payloadBase64"]=Convert.ToBase64String(payload),["signatureBase64"]=Convert.ToBase64String(rsa.SignData(payload,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1))}.ToString();}
DesktopRelease Verify(string json,long min=1)=>ReleaseAuthentication.Verify(json,"tech.afonasev.star-racing.win-x64","win-x64",min,"test",modulus);
data["sha256"]=((string)data["sha256"]!).ToLowerInvariant();
var release=Verify(Envelope(data));Require(release.sequence==2,"valid golden vector");
var envelope=JObject.Parse(Envelope(data));envelope["payloadBase64"]=Convert.ToBase64String(Encoding.UTF8.GetBytes("{}"));Reject(()=>Verify(envelope.ToString()),"bad signature");
Reject(()=>Verify(Envelope(data),3),"replay");
foreach(var pair in new[]{("appId","foreign"),("channel","osx-universal"),("url","http://racing.afonasev.tech/a"),("url","https://evil.invalid/a"),("fileName","../racing-full.nupkg"),("fileName","C:\\racing-full.nupkg"),("sha256","abc"),("version","unknown")}){var other=(JObject)data.DeepClone();other[pair.Item1]=pair.Item2;Reject(()=>Verify(Envelope(other)),pair.Item1+pair.Item2);}
var duplicate=Envelope(data).Replace("\"keyId\":", "\"keyId\": \"other\", \"keyId\":");Reject(()=>Verify(duplicate),"duplicate fields");
Reject(()=>Verify(Envelope(data)+"{}"),"trailing json");
Reject(()=>Verify(new string('a',131073)),"oversized metadata");
string file=Path.GetTempFileName();try{
 File.WriteAllBytes(file,new byte[]{1,2,3});ReleaseAuthentication.VerifyFile(release,file);assertions++;
 using var compatibilitySource=new AuthenticatedUpdateSource("tech.afonasev.star-racing.win-x64","win-x64",1);
 VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
 var sdkManager=new UpdateManager(compatibilitySource);
 var checksum=typeof(UpdateManager).GetMethod("VerifyPackageChecksumAsync",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
 Require(checksum!=null,"pinned SDK checksum method available");
 await (Task)checksum!.Invoke(sdkManager,new object[]{release.Asset,file})!;assertions++;
 File.WriteAllBytes(file,new byte[]{1,2,4});Reject(()=>ReleaseAuthentication.VerifyFile(release,file),"cache tampering");
 Reject(()=>((Task)checksum.Invoke(sdkManager,new object[]{release.Asset,file})!).GetAwaiter().GetResult(),"SDK rejects cache tampering");
 File.WriteAllBytes(file,new byte[]{1,2});Reject(()=>ReleaseAuthentication.VerifyFile(release,file),"partial file");
}finally{File.Delete(file);}
using var source=new AuthenticatedUpdateSource("tech.afonasev.star-racing.win-x64","win-x64",1);
try{await source.DownloadReleaseEntry(null,release.Asset,"unreachable",null);throw new Exception("download without consent accepted");}catch(Exception ex){Require(!File.Exists("unreachable"),"no payload file without consent");Require(ex is not HttpRequestException,"no network without consent");}
Require(ReleaseAuthentication.GithubPackage(release)=="https://github.com/afonasev/star-racing/releases/download/v0.2.1-test.2/racing-0.2.1-test.2-full.nupkg","authenticated GitHub mapping");
Require(ReleaseAuthentication.IsReleaseRedirect(new Uri("https://release-assets.githubusercontent.com/github-production-release-asset/123/a?token=test")),"GitHub asset redirect");
foreach(var url in new[]{"http://release-assets.githubusercontent.com/a","https://evil.invalid/a","https://release-assets.githubusercontent.com.evil.invalid/a","https://user@release-assets.githubusercontent.com/a","https://release-assets.githubusercontent.com:444/a"}) Require(!ReleaseAuthentication.IsReleaseRedirect(new Uri(url)),"reject redirect "+url);
var production=(JObject)data.DeepClone();production["releaseTrack"]="production";production["version"]="0.3.0";production["fileName"]="racing-0.3.0-full.nupkg";production["url"]="https://github.com/afonasev/star-racing/releases/download/v0.3.0/racing-0.3.0-full.nupkg";production["unsignedProductionCatalog"]=true;
DesktopRelease Unsigned(JObject obj,long min=1,string track="production")=>ReleaseAuthentication.VerifyUnsignedProduction(obj.ToString(),"tech.afonasev.star-racing.win-x64","win-x64",min,track);
Reject(()=>Verify(production.ToString()),"unsigned passed to signed route");
Reject(()=>Unsigned(production,1,"test"),"unsigned test route");
#if STAR_RACING_UNSIGNED_PRODUCTION
var unsignedRelease=Unsigned(production);Require(unsignedRelease.version=="0.3.0","unsigned production accepted with compiled opt-in");
Reject(()=>Unsigned(production,3),"unsigned replay");
foreach(var pair in new[]{("appId","foreign"),("channel","osx-universal"),("releaseTrack","test"),("version","0.3.0-test.1"),("url","https://evil.invalid/a"),("fileName","../racing-full.nupkg"),("sha256","abc")}){var other=(JObject)production.DeepClone();other[pair.Item1]=pair.Item2;Reject(()=>Unsigned(other),"unsigned "+pair.Item1);}
foreach(var marker in new JToken[]{new JValue(false),new JValue("true"),JValue.CreateNull()}){var other=(JObject)production.DeepClone();other["unsignedProductionCatalog"]=marker;Reject(()=>Unsigned(other),"unsigned marker type");}
string unsignedFile=Path.GetTempFileName();try{File.WriteAllBytes(unsignedFile,new byte[]{1,2,3});ReleaseAuthentication.VerifyFile(unsignedRelease,unsignedFile);assertions++;File.WriteAllBytes(unsignedFile,new byte[]{1,2,4});Reject(()=>ReleaseAuthentication.VerifyFile(unsignedRelease,unsignedFile),"unsigned cache tampering");}finally{File.Delete(unsignedFile);}
#else
Reject(()=>Unsigned(production),"unsigned rejected without compiled opt-in");
#endif
Console.WriteLine("UPDATE_CONTRACTS_OK "+assertions);

if(args.Length==2&&args[0]=="--live-production"){
 Directory.CreateDirectory(args[1]);
 foreach(var platform in new[]{"win-x64","osx-universal"}){
  using var live=new AuthenticatedUpdateSource("tech.afonasev.star-racing."+platform,platform,1,"production");
  var feed=await live.GetReleaseFeed(null,"tech.afonasev.star-racing."+platform,platform);
  Require(feed.Assets.Length==1,"live single full asset");live.DownloadConsent=true;
  string downloaded=Path.Combine(args[1],platform+".nupkg");
  try{await live.DownloadReleaseEntry(null,feed.Assets[0],downloaded,null);ReleaseAuthentication.VerifyFile(live.Release,downloaded);
   File.WriteAllText(Path.Combine(args[1],platform+".json"),new JObject{["success"]=true,["version"]=live.Release.version,["size"]=live.Release.size,["sha256"]=live.Release.sha256,["url"]=live.Release.url,["descriptor"]=JObject.Parse(live.Envelope)}.ToString());
   Console.WriteLine("LIVE_PRODUCTION_DOWNLOAD_OK "+platform+" "+live.Release.version);
  }finally{if(File.Exists(downloaded))File.Delete(downloaded);}
 }
}

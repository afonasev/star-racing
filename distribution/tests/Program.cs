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
Console.WriteLine("UPDATE_CONTRACTS_OK "+assertions);

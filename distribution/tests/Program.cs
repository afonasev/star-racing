using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using System.Text;
using StarRacingPrototype.Distribution;
using Velopack;
if(args.Length==4&&args[0]=="--live"){
 string track=args[1],platform=args[2],target=args[3];
 using var live=new AuthenticatedUpdateSource("tech.afonasev.star-racing."+platform,platform,0,track);
 var feed=await live.GetReleaseFeed(null,"tech.afonasev.star-racing."+platform,platform);
 Console.WriteLine("LIVE_METADATA_SIGNATURE_OK "+live.Release.version+" "+platform+" "+track);
 if(File.Exists(target))throw new Exception("Refusing to overwrite live evidence download");
 live.DownloadConsent=true;await live.DownloadReleaseEntry(null,feed.Assets.Single(),target,null);
 ReleaseAuthentication.VerifyFile(live.Release,target);
 Console.WriteLine("LIVE_PACKAGE_SHA256_OK "+live.Release.sha256+" bytes="+live.Release.size);return;
}
int assertions=0;
void Require(bool condition,string label){assertions++;if(!condition)throw new Exception(label);}
void Reject(Action action,string label){try{action();}catch(Exception){assertions++;return;}throw new Exception("Accepted "+label);}
using var rsa=RSA.Create(3072);
string modulus=Convert.ToBase64String(rsa.ExportParameters(false).Modulus!);
var data=new JObject{["schema"]=2,["releaseTrack"]="test",["appId"]="tech.afonasev.star-racing.win-x64",["channel"]="win-x64",["version"]="0.2.1-test.2",["sequence"]=2,["fileName"]="racing-0.2.1-test.2-full.nupkg",["size"]=3,["sha256"]=Convert.ToHexString(SHA256.HashData(new byte[]{1,2,3})),["url"]="https://github.com/afonasev/star-racing/releases/download/v0.2.1-test.2/racing-0.2.1-test.2-full.nupkg",["notes"]="test"};
string Envelope(JObject obj){byte[] payload=Encoding.UTF8.GetBytes(obj.ToString());return new JObject{["keyId"]=DesktopUpdateKey.Id,["payloadBase64"]=Convert.ToBase64String(payload),["signatureBase64"]=Convert.ToBase64String(rsa.SignData(payload,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1))}.ToString();}
DesktopRelease Verify(string json,long min=1)=>ReleaseAuthentication.Verify(json,"tech.afonasev.star-racing.win-x64","win-x64",min,"test",modulus);
data["sha256"]=((string)data["sha256"]!).ToLowerInvariant();
var release=Verify(Envelope(data));Require(release.sequence==2,"valid golden vector");
var envelope=JObject.Parse(Envelope(data));envelope["payloadBase64"]=Convert.ToBase64String(Encoding.UTF8.GetBytes("{}"));Reject(()=>Verify(envelope.ToString()),"bad signature");
Reject(()=>Verify(Envelope(data),3),"replay");
foreach(var pair in new[]{("releaseTrack","production"),("schema","1"),("appId","foreign"),("channel","osx-universal"),("url","http://racing.afonasev.tech/a"),("url","https://evil.invalid/a"),("fileName","../racing-full.nupkg"),("fileName","C:\\racing-full.nupkg"),("sha256","abc"),("version","unknown")}){var other=(JObject)data.DeepClone();other[pair.Item1]=pair.Item2;Reject(()=>Verify(Envelope(other)),pair.Item1+pair.Item2);}
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
string journal=Path.Combine(Path.GetTempPath(),"star-racing-journal-"+Guid.NewGuid()+".json");
try{
 string signed=Envelope(data);
 Require(!UpdateAttemptJournal.AlreadyAttempted(journal,signed,"0.2.0","0.2.1-test.2"),"no prior attempt");
 UpdateAttemptJournal.Begin(journal,signed,"0.2.0","0.2.1-test.2");
 Require(UpdateAttemptJournal.AlreadyAttempted(journal,signed,"0.2.0","0.2.1-test.2"),"failed install suppresses automatic UAC retry");
 Require(UpdateAttemptJournal.AlreadyAttempted(journal,signed+" ","0.2.0","0.2.1-test.2"),"envelope formatting cannot retrigger UAC");
 var changed=(JObject)data.DeepClone();changed["sequence"]=3;Require(!UpdateAttemptJournal.AlreadyAttempted(journal,Envelope(changed),"0.2.0","0.2.1-test.2"),"new signed payload independent");
 File.WriteAllText(journal,"broken");Require(UpdateAttemptJournal.AlreadyAttempted(journal,signed,"0.2.0","0.2.1-test.2"),"corrupt journal fails safe");
 UpdateAttemptJournal.Save(journal,signed);Require(File.ReadAllText(journal)==signed,"atomic receipt replacement");
}finally{File.Delete(journal);}
var apiBody=new JObject{["tag_name"]="channel-test",["draft"]=false,["prerelease"]=true,["body"]=new JObject{["schema"]=1,["track"]="test",["version"]=data["version"],["platforms"]=new JObject{["win-x64"]=JObject.Parse(Envelope(data))}}.ToString()};
var calls=new List<string>();
using(var authenticated=new AuthenticatedUpdateSource("tech.afonasev.star-racing.win-x64","win-x64",1,"test",new StubHandler(request=>{
 calls.Add(request.RequestUri!.AbsoluteUri);
 if(request.RequestUri.Host=="api.github.com")return new HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new StringContent(apiBody.ToString())};
 return new HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new ByteArrayContent(new byte[]{1,2,3})};
}),modulus)){
 var feed=await authenticated.GetReleaseFeed(null,"tech.afonasev.star-racing.win-x64","win-x64");
 Require(feed.Assets.Length==1,"authenticated GitHub feed");
 string downloaded=Path.Combine(Path.GetTempPath(),"star-racing-download-"+Guid.NewGuid());
 try{
  Reject(()=>authenticated.DownloadReleaseEntry(null,feed.Assets[0],downloaded,null).GetAwaiter().GetResult(),"no request without download consent");
  Require(calls.Count==1,"only metadata before consent");
  authenticated.DownloadConsent=true;await authenticated.DownloadReleaseEntry(null,feed.Assets[0],downloaded,null);assertions++;
  Require(File.ReadAllBytes(downloaded).SequenceEqual(new byte[]{1,2,3}),"signed content exact");
  Require(calls.All(url=>!url.Contains("afonasev.tech")),"new client never contacts VPS");
 }finally{File.Delete(downloaded);}
}
apiBody["prerelease"]=false;
using(var wrongTrack=new AuthenticatedUpdateSource("tech.afonasev.star-racing.win-x64","win-x64",1,"test",new StubHandler(_=>new HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new StringContent(apiBody.ToString())}),modulus))
 Reject(()=>wrongTrack.GetReleaseFeed(null,"tech.afonasev.star-racing.win-x64","win-x64").GetAwaiter().GetResult(),"GitHub channel cannot silently become production");
Console.WriteLine("UPDATE_CONTRACTS_OK "+assertions);
sealed class StubHandler(Func<HttpRequestMessage,HttpResponseMessage> respond):HttpMessageHandler{
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>Task.FromResult(respond(request));
}

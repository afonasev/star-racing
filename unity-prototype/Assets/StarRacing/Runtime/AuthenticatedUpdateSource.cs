using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Velopack;
using Velopack.Logging;
using Velopack.NuGet;
using Velopack.Sources;

namespace StarRacingPrototype.Distribution {
 public sealed class DesktopRelease {
  public string appId,channel,version,fileName,sha256,url,notes;
  public long size,sequence;
  public VelopackAsset Asset => new VelopackAsset {PackageId=appId,Version=SemanticVersion.Parse(version),Type=VelopackAssetType.Full,FileName=fileName,SHA256=sha256.ToUpperInvariant(),Size=size,NotesMarkdown=notes};
 }

 // Only this authenticated, immutable description is ever exposed to Velopack.
 public static class ReleaseAuthentication {
  static JObject Parse(string json){
   if(json==null||Encoding.UTF8.GetByteCount(json)>131072)throw new InvalidDataException("Каталог слишком велик");
   using(var reader=new JsonTextReader(new StringReader(json))){
    var obj=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});
    if(reader.Read())throw new InvalidDataException("Лишние данные каталога");return obj;
   }
  }
  static string Text(JObject obj,string key){if(obj[key]?.Type!=JTokenType.String)throw new InvalidDataException("Поле каталога: "+key);return (string)obj[key];}
  static long Number(JObject obj,string key){if(obj[key]?.Type!=JTokenType.Integer)throw new InvalidDataException("Поле каталога: "+key);return (long)obj[key];}
  public static DesktopRelease Verify(string envelope,string appId,string channel,long minimumSequence,string modulus=DesktopUpdateKey.Modulus){
   var outer=Parse(envelope);
   if(Text(outer,"keyId")!=DesktopUpdateKey.Id)throw new InvalidDataException("Неизвестный ключ обновления");
   byte[] bytes=Convert.FromBase64String(Text(outer,"payloadBase64")),signature=Convert.FromBase64String(Text(outer,"signatureBase64"));
   using(var rsa=RSA.Create()){
    rsa.ImportParameters(new RSAParameters{Modulus=Convert.FromBase64String(modulus),Exponent=Convert.FromBase64String(DesktopUpdateKey.Exponent)});
    if(!rsa.VerifyData(bytes,signature,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1))throw new InvalidDataException("Подпись обновления не подтверждена");
   }
   var data=Parse(new UTF8Encoding(false,true).GetString(bytes));
   var release=new DesktopRelease{appId=Text(data,"appId"),channel=Text(data,"channel"),version=Text(data,"version"),fileName=Text(data,"fileName"),sha256=Text(data,"sha256"),url=Text(data,"url"),notes=Text(data,"notes"),size=Number(data,"size"),sequence=Number(data,"sequence")};
   if(Number(data,"schema")!=1||release.appId!=appId||release.channel!=channel||release.sequence<minimumSequence||release.sequence<=0||release.sequence>int.MaxValue)throw new InvalidDataException("Каталог другой игры, платформы или устаревший");
   var version=SemanticVersion.Parse(release.version);
   if(version.ToNormalizedString()!=release.version)throw new InvalidDataException("Некорректная версия");
   if(release.fileName!=Path.GetFileName(release.fileName)||release.fileName.Contains("\\")||release.fileName.Contains("..")||!release.fileName.EndsWith("-full.nupkg",StringComparison.Ordinal)||release.fileName.Length>180)throw new InvalidDataException("Некорректное имя пакета");
   if(release.size<=0||release.size>8589934592L||!System.Text.RegularExpressions.Regex.IsMatch(release.sha256,"\\A[0-9a-fA-F]{64}\\z"))throw new InvalidDataException("Некорректный размер или hash");
   var expected="https://racing.afonasev.tech/releases/"+channel+"/"+release.version+"/"+release.fileName;
   if(release.url!=expected)throw new InvalidDataException("Некорректный адрес пакета");
   return release;
  }
  public static string GithubPackage(DesktopRelease release) => "https://github.com/afonasev/star-racing/releases/download/v"+release.version+"/"+release.fileName;
  public static bool IsReleaseRedirect(Uri uri) => uri!=null && uri.Scheme=="https" && uri.Port==443 && string.IsNullOrEmpty(uri.UserInfo) && uri.Host=="release-assets.githubusercontent.com";
  public static void VerifyFile(DesktopRelease release,string file){
   if(new FileInfo(file).Length!=release.size)throw new InvalidDataException("Размер обновления не совпал");
   using(var hash=SHA256.Create())using(var stream=File.OpenRead(file)){
    string actual=BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","");
    if(!actual.Equals(release.sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Пакет обновления поврежден");
   }
  }
 }

 public sealed class AuthenticatedUpdateSource:IUpdateSource,IDisposable {
  readonly HttpClient client;
  readonly string appId,channel;
  public long MinimumSequence {get;set;}
  public string Envelope {get;private set;}
  public DesktopRelease Release {get;private set;}
  public bool DownloadConsent {get;set;}
  public AuthenticatedUpdateSource(string appId,string channel,long minimumSequence){
   this.appId=appId;this.channel=channel;MinimumSequence=minimumSequence;
   client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromMinutes(60)};
  }
  public void Restore(string envelope){var release=ReleaseAuthentication.Verify(envelope,appId,channel,MinimumSequence);Release=release;Envelope=envelope;}
  public async Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger,string requestedAppId,string requestedChannel,Guid? stagingId=null,VelopackAsset latestLocalRelease=null){
   if(requestedAppId!=appId||requestedChannel!=channel)throw new InvalidDataException("Неподходящий канал обновлений");
   using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20)))
   using(var response=await client.GetAsync("https://racing.afonasev.tech/releases/"+channel+"/latest.json",HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false)){
    response.EnsureSuccessStatusCode();
    using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var output=new MemoryStream()){
     await CopyBounded(input,output,131072,timeout.Token,null).ConfigureAwait(false);
     Restore(new UTF8Encoding(false,true).GetString(output.ToArray()));
    }
   }
   return new VelopackAssetFeed{Assets=new[]{Release.Asset}};
  }
  public async Task DownloadReleaseEntry(IVelopackLogger logger,VelopackAsset asset,string localFile,Action<int> progress,CancellationToken cancelToken=default){
   var release=ReleaseAuthentication.Verify(Envelope,appId,channel,MinimumSequence);
   if(!DownloadConsent||asset.PackageId!=release.appId||asset.Version!=SemanticVersion.Parse(release.version)||asset.Type!=VelopackAssetType.Full||asset.FileName!=release.fileName||asset.Size!=release.size||!string.Equals(asset.SHA256,release.sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Загрузка не согласована или пакет изменился");
   try{
    using(var response=await GetGithubPackage(release,cancelToken).ConfigureAwait(false)){
     response.EnsureSuccessStatusCode();
     if(response.Content.Headers.ContentLength.HasValue&&response.Content.Headers.ContentLength.Value!=release.size)throw new InvalidDataException("Размер загрузки изменился");
     using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var output=new FileStream(localFile,FileMode.Create,FileAccess.Write,FileShare.None,65536,true)){
      await CopyBounded(input,output,release.size,cancelToken,progress).ConfigureAwait(false);
     }
    }
    ReleaseAuthentication.VerifyFile(release,localFile); // Before SDK extracts/replaces its native helper.
   }catch{if(File.Exists(localFile))File.Delete(localFile);throw;}
  }
  async Task<HttpResponseMessage> GetGithubPackage(DesktopRelease release,CancellationToken token){
   var response=await client.GetAsync(ReleaseAuthentication.GithubPackage(release),HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);
   if((int)response.StatusCode==302 || (int)response.StatusCode==301 || (int)response.StatusCode==307){
    var location=response.Headers.Location;response.Dispose();
    if(!ReleaseAuthentication.IsReleaseRedirect(location))throw new InvalidDataException("Неподходящий адрес загрузки GitHub");
    response=await client.GetAsync(location,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);
   }
   return response; // caller checks status, exact byte limit and signed SHA256.
  }
  static async Task CopyBounded(Stream input,Stream output,long limit,CancellationToken token,Action<int> progress){
   var buffer=new byte[65536];long copied=0;int count;
   while((count=await input.ReadAsync(buffer,0,buffer.Length,token).ConfigureAwait(false))>0){
    copied+=count;if(copied>limit)throw new InvalidDataException("Превышен размер загрузки");
    await output.WriteAsync(buffer,0,count,token).ConfigureAwait(false);progress?.Invoke((int)(copied*100/limit));
   }
  }
  public void Dispose(){client.Dispose();}
 }
}

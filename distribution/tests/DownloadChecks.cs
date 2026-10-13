using System.Net;
using Newtonsoft.Json.Linq;
using StarRacingPrototype.Distribution;

static class DownloadChecks {
 public static async Task Run(JObject data,Func<JObject,string> envelope,string modulus,Action<bool,string> require){
  string root=Path.Combine(Path.GetTempPath(),"star-racing-download-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try{
   foreach(string scenario in new[]{"success","partial","oversized","tampered","bad-redirect","http-error","timeout","body-timeout","cancel","cancel-mid","retry"}){
    var handler=new PackageHandler(scenario);
    using var source=new AuthenticatedUpdateSource((string)data["appId"]!,(string)data["channel"]!,1,"test",handler,modulus);
    source.Restore(envelope(data));source.DownloadConsent=true;
    string file=Path.Combine(root,scenario);var progress=new List<int>();using var cancellation=new CancellationTokenSource();
    if(scenario=="cancel")cancellation.Cancel();
    bool success=false;
    try{await source.DownloadReleaseEntry(null,source.Release.Asset,file,value=>{progress.Add(value);if(scenario=="cancel-mid")cancellation.Cancel();},cancellation.Token);success=true;}
    catch(Exception e){require(e is InvalidDataException||e is IOException||e is HttpRequestException||e is OperationCanceledException||e is TimeoutException,"expected download failure: "+scenario+" "+e.GetType().Name);if(scenario=="timeout")require(e is TimeoutException,"network timeout distinguished from user cancellation");}
    require(success==(scenario=="success"),"download outcome: "+scenario);
    if(success){require(File.ReadAllBytes(file).SequenceEqual(new byte[]{1,2,3}),"exact package bytes");require(progress.Count==3&&progress[^1]==100&&progress.All(x=>x>=0&&x<=100)&&progress.SequenceEqual(progress.OrderBy(x=>x)),"download progress advances to 100");}
    else require(!File.Exists(file),"failed download removes partial bytes: "+scenario);
    if(scenario=="bad-redirect")require(handler.Requests==1,"untrusted redirect never contacted");
    if(scenario=="retry"){
     handler.Scenario="success";await source.DownloadReleaseEntry(null,source.Release.Asset,file,progress.Add);
     require(File.Exists(file)&&progress[^1]==100,"failed transfer can be retried");
    }
   }
  }finally{Directory.Delete(root,true);}
 }
 sealed class PackageHandler:HttpMessageHandler {
  public string Scenario;public int Requests;
  public PackageHandler(string scenario){Scenario=scenario;}
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
   token.ThrowIfCancellationRequested();Requests++;
   if(Scenario=="timeout")throw new TaskCanceledException("Simulated server timeout");
   if(request.RequestUri!.Host=="github.com"){
    var redirect=new HttpResponseMessage(HttpStatusCode.Found);
    redirect.Headers.Location=new Uri(Scenario=="bad-redirect"?"https://evil.invalid/package":"https://release-assets.githubusercontent.com/package");return Task.FromResult(redirect);
   }
   if(Scenario=="http-error")return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
   byte[] bytes=Scenario switch{"partial"=>new byte[]{1,2},"oversized"=>new byte[]{1,2,3,4},"tampered" or "retry"=>new byte[]{1,2,4},_=>new byte[]{1,2,3}};
   var content=new StreamContent(new ChunkStream(bytes,Scenario=="body-timeout"));content.Headers.ContentLength=3;
   return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=content});
  }
 }
 sealed class ChunkStream:MemoryStream {
  readonly bool timeout;
  public ChunkStream(byte[] bytes,bool timeout):base(bytes){this.timeout=timeout;}
  public override Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken token){
   if(timeout)throw new TaskCanceledException("Simulated transfer stall");
   return base.ReadAsync(buffer,offset,Math.Min(count,1),token);
  }
 }
}

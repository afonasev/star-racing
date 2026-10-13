using System.Net;
using StarRacingPrototype.Distribution;
static class PublicationChecks {
 public static async Task Run(Action<bool,string> require){
  foreach(var scenario in new[]{"valid","wrong-tag","draft","missing","invalid","http-error"}){
   using var source=new AuthenticatedUpdateSource("tech.afonasev.star-racing.win-x64","win-x64",1,"production",new Handler(scenario));
   bool success=false;string date=null;
   try{date=await source.PublicationDate("0.3.3");success=true;}catch(Exception e){require(e is InvalidDataException||e is HttpRequestException,"publication expected failure "+scenario);}
   require(success==(scenario=="valid"),"publication identity "+scenario);
   if(success)require(date=="2026-10-12","publication date normalized to UTC");
  }
 }
 sealed class Handler:HttpMessageHandler {
  readonly string scenario;public Handler(string value){scenario=value;}
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
   if(request.RequestUri!.AbsoluteUri!="https://api.github.com/repos/afonasev/star-racing/releases/tags/v0.3.3")throw new Exception("Must request installed immutable version");
   if(scenario=="http-error")return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
   var release=new Newtonsoft.Json.Linq.JObject{["tag_name"]=scenario=="wrong-tag"?"v0.3.4":"v0.3.3",["draft"]=scenario=="draft",["published_at"]=scenario=="invalid"?"invalid":"2026-10-11T23:30:00-07:00"};
   if(scenario=="missing")release.Remove("published_at");
   return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(release.ToString())});
  }
 }
}

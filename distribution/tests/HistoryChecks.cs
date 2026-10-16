using System.Net;
using Newtonsoft.Json.Linq;
using StarRacingPrototype.Distribution;
static class HistoryChecks {
 public static async Task Run(Action<bool,string> require){
  var handler=new Handler();using var source=new AuthenticatedUpdateSource("tech.afonasev.star-racing.win-x64","win-x64",1,"production",handler);
  var entries=await source.History("0.1.0");
  require(entries.Length==11&&entries.Any(x=>x.Version=="0.1.0"),"history includes ten recent and installed release outside window");
  require(entries[0].Version=="0.20.0"&&entries.All(x=>!x.Version.Contains("-")),"history filters own track and orders semantic versions");
  require(entries[0].Localized("ru").Contains("Настройки")&&!entries[0].Localized("ru").Contains("Settings"),"Russian body from release");
  require(entries[0].Localized("en").Contains("Settings")&&!entries[0].Localized("en").Contains("Настройки"),"English body from release");
  require(source.Release==null&&source.Envelope==null,"history cannot change update selection");
  require(!handler.Requests.Any(x=>x.Contains("channel-")),"history does not discover updates");
  require(new ReleaseHistoryEntry{Body="old raw body"}.Localized("ru").Contains("недоступно"),"missing translation not invented");
  source.SuspendNetwork();int previous=handler.Requests.Count;
  try{await source.History("0.1.0");throw new Exception("History allowed in race");}catch(OperationCanceledException){}
  require(handler.Requests.Count==previous,"suspended history performs no request");
  source.ResumeNetwork();await source.History("0.1.0");require(handler.Requests.Count>previous,"history resumes only when menu permits requests");
 }
 sealed class Handler:HttpMessageHandler {
  public readonly List<string> Requests=new();
  JObject Release(string version)=>new(){["tag_name"]="v"+version,["draft"]=false,["prerelease"]=version.Contains('-'),["published_at"]="2026-10-15T12:00:00Z",["body"]="## Русский\n### Новое\n- Настройки\n\n## English\n### Added\n- Settings"};
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
   token.ThrowIfCancellationRequested();Requests.Add(request.RequestUri!.AbsoluteUri);string body;
   if(request.RequestUri.AbsolutePath.Contains("/tags/"))body=Release("0.1.0").ToString();
   else{var items=new JArray();for(int i=1;i<=20;i++)items.Add(Release("0."+i+".0"));items.Add(Release("0.99.0-test.1"));var draft=Release("0.99.0");draft["draft"]=true;items.Add(draft);body=items.ToString();}
   return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body)});
  }
 }
}

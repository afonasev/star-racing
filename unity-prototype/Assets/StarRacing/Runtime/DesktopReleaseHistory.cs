using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Velopack.NuGet;
using Velopack;
namespace StarRacingPrototype.Distribution {
 public sealed class ReleaseHistoryEntry {
  public string Version,Date,Body;
  public string Localized(string language){
   string marker=language=="en"?"## English":"## Русский";
   var lines=(Body??"").Replace("\r","").Split('\n');int start=Array.IndexOf(lines,marker);
   if(start<0)return language=="en"?"Release notes are unavailable in English.":"Описание этой версии на русском недоступно.";
   return string.Join("\n",lines.Skip(start+1).TakeWhile(line=>!line.StartsWith("## ",StringComparison.Ordinal))).Trim();
  }
 }
 public sealed partial class AuthenticatedUpdateSource {
  // Presentation requests do not restore or replace the consented update descriptor.
  async Task<string> HistoryJson(string suffix){
   using(var timeout=MetadataTimeout())
   using(var response=await client.GetAsync("https://api.github.com/repos/afonasev/star-racing/"+suffix,HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false)){
    response.EnsureSuccessStatusCode();
    using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var output=new MemoryStream()){
     await CopyBounded(input,output,8388608,timeout.Token,null).ConfigureAwait(false);
     return new UTF8Encoding(false,true).GetString(output.ToArray());
    }
   }
  }
  public static ReleaseHistoryEntry HistoryEntry(JObject item,string expectedTrack){
   if(item["draft"]?.Type!=JTokenType.Boolean||(bool)item["draft"]||item["prerelease"]?.Type!=JTokenType.Boolean||(bool)item["prerelease"]!=(expectedTrack=="test"))return null;
   string tag=(string)item["tag_name"];if(tag==null||!tag.StartsWith("v",StringComparison.Ordinal))return null;
   string version=tag.Substring(1);if(!SemanticVersion.TryParse(version,out var parsed)||parsed.ToNormalizedString()!=version||(expectedTrack=="test")!=version.Contains("-"))return null;
   if(!DateTimeOffset.TryParse((string)item["published_at"],CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var date)||item["body"]?.Type!=JTokenType.String)return null;
   return new ReleaseHistoryEntry{Version=version,Date=date.UtcDateTime.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),Body=(string)item["body"]};
  }
  public async Task<ReleaseHistoryEntry[]> History(string installedVersion){
   var entries=new List<ReleaseHistoryEntry>();
   // Ten pages bound API work; each page is parsed with duplicate-field rejection.
   for(int page=1;page<=10&&entries.Count<10;page++){
    var json=await HistoryJson("releases?per_page=100&page="+page).ConfigureAwait(false);
    JArray list;using(var reader=new JsonTextReader(new StringReader(json))){list=JArray.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});if(reader.Read())throw new InvalidDataException("Trailing release history");}
    foreach(var value in list){var entry=HistoryEntry((JObject)value,track);if(entry!=null&&!entries.Any(e=>e.Version==entry.Version))entries.Add(entry);}
    if(list.Count<100)break;
   }
   entries=entries.OrderByDescending(e=>SemanticVersion.Parse(e.Version)).Take(10).ToList();
   if(!entries.Any(e=>e.Version==installedVersion)){
    if(!SemanticVersion.TryParse(installedVersion,out var version)||version.ToNormalizedString()!=installedVersion)throw new InvalidDataException("Invalid installed version");
    var item=ReleaseAuthentication.Parse(await HistoryJson("releases/tags/v"+installedVersion).ConfigureAwait(false));
    var installed=HistoryEntry(item,track);if(installed==null||installed.Version!=installedVersion)throw new InvalidDataException("Installed release history unavailable");entries.Add(installed);
   }
   return entries.OrderByDescending(e=>SemanticVersion.Parse(e.Version)).ToArray();
  }
 }
}

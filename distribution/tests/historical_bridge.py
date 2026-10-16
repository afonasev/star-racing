#!/usr/bin/env python3
"""Compile exact shipped old updater sources; prove signed-pointer bridge compatibility."""
import argparse,hashlib,json,os,subprocess,tempfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
SHIPPED='197de14d3041c043053c8f9956a7452f567babd9'
FILES=['AuthenticatedUpdateSource.cs','DesktopReleaseChannel.cs','DesktopUpdateKey.cs']
def run(dotnet,output):
 with tempfile.TemporaryDirectory(prefix='star-racing-old-client-') as tmp:
  tmp=Path(tmp);digests={}
  for name in FILES:
   data=subprocess.check_output(['gh','api','repos/afonasev/star-racing/contents/unity-prototype/Assets/StarRacing/Runtime/'+name+'?ref='+SHIPPED,'-H','Accept: application/vnd.github.raw+json'])
   snapshots=output/'historical-source';snapshots.mkdir(parents=True,exist_ok=True);(snapshots/name).write_bytes(data)
   (tmp/name).write_bytes(data);digests[name]=hashlib.sha256(data).hexdigest()
  (tmp/'Bridge.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><DefineConstants>STAR_RACING_UNSIGNED_PRODUCTION</DefineConstants></PropertyGroup><ItemGroup><PackageReference Include="Velopack" Version="1.2.158"/><PackageReference Include="Newtonsoft.Json" Version="13.0.4"/></ItemGroup></Project>''')
  (tmp/'Program.cs').write_text('''using System.Net;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using StarRacingPrototype.Distribution;
if(!DesktopReleaseChannel.AllowsUnsignedProduction)throw new Exception("Not the real opt-in path");
using var rsa=RSA.Create(2048);string modulus=Convert.ToBase64String(rsa.ExportParameters(false).Modulus!);
int count=0;
foreach(string channel in new[]{"win-x64","osx-universal"}){
 var data=new JObject{["schema"]=2,["appId"]="tech.afonasev.star-racing."+channel,["channel"]=channel,["releaseTrack"]="production",["version"]="0.3.6",["sequence"]=7,["fileName"]="racing-0.3.6-full.nupkg",["size"]=1,["sha256"]=Convert.ToHexString(SHA256.HashData(new byte[]{1})),["url"]="https://github.com/afonasev/star-racing/releases/download/v0.3.6/racing-0.3.6-full.nupkg",["notes"]="bridge"};
 byte[] payload=Encoding.UTF8.GetBytes(data.ToString());
 var envelope=new JObject{["keyId"]=DesktopUpdateKey.Id,["payloadBase64"]=Convert.ToBase64String(payload),["signatureBase64"]=Convert.ToBase64String(rsa.SignData(payload,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1))};
 var catalog=new JObject{["schema"]=1,["track"]="production",["version"]="0.3.6",["platforms"]=new JObject{[channel]=envelope}};
 using var source=new AuthenticatedUpdateSource((string)data["appId"]!,channel,6,"production",new Handler(catalog.ToString()),modulus);
 var feed=await source.GetReleaseFeed(null,(string)data["appId"]!,channel);
 if(feed.Assets.Length!=1||source.Release.version!="0.3.6"||source.VerifyDescriptor(source.Envelope).sequence!=7)throw new Exception("Signed bridge rejected by shipped client");count++;
 envelope["signatureBase64"]=Convert.ToBase64String(new byte[256]);bool rejected=false;
 try{source.VerifyDescriptor(envelope.ToString());}catch(Exception){rejected=true;}
 if(!rejected)throw new Exception("Old client accepted tampered bridge");count++;
}
Console.WriteLine("HISTORICAL_SIGNED_BRIDGE_OK "+count);
sealed class Handler:HttpMessageHandler {
 readonly string catalog;public Handler(string value){catalog=value;}
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
  if(!request.RequestUri!.AbsoluteUri.EndsWith("/releases/tags/channel-production"))throw new Exception("Unexpected discovery route");
  return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(new JObject{["draft"]=false,["prerelease"]=false,["tag_name"]="channel-production",["body"]=catalog}.ToString())});
 }
}
''')
  result=subprocess.run([dotnet,'run','--project',tmp/'Bridge.csproj'],env=dict(os.environ,DOTNET_ROOT=str(dotnet.parent)),capture_output=True,text=True)
  output.mkdir(parents=True,exist_ok=True);(output/'historical-bridge.log').write_text(result.stdout+result.stderr);result.check_returncode()
  assert 'HISTORICAL_SIGNED_BRIDGE_OK 4' in result.stdout
  (output/'historical-bridge.json').write_text(json.dumps(dict(success=True,shippedSourceRevision=SHIPPED,exactSourcesSha256=digests,platforms=['win-x64','osx-universal'],oldUnsignedOptIn=True,signedCatalogSchema=1,assertions=4,installedAcceptance=False),indent=2)+'\n')
  print('HISTORICAL_SIGNED_BRIDGE_OK 4')
if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('--dotnet',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args();run(a.dotnet.resolve(),a.output.resolve())

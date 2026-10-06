#!/usr/bin/env python3
"""One-time old-client TEST bridge. Upload only tiny metadata; retain every old path."""
import argparse,json,subprocess,urllib.parse,urllib.request,tempfile
from pathlib import Path
from metadata import verify,url
from publish import validate_identity
p=argparse.ArgumentParser();p.add_argument('--release',type=Path,action='append',required=True);p.add_argument('--host',default='gfe');p.add_argument('--evidence',type=Path,required=True);a=p.parse_args()
ROOT='/opt/star-racing-desktop'
def remote(script):return subprocess.check_output(['ssh','-o','BatchMode=yes','-o','ConnectTimeout=10',a.host,script],text=True)
original=json.loads(remote('cat '+ROOT+'/service/relay-catalog.json'));catalog=dict(original);feeds={}
for folder in a.release:
 identity=validate_identity(folder)
 if identity.get('releaseTrack')!='test':raise ValueError('Legacy clients are test clients; production cannot use their feed')
 envelope=json.loads((folder/'legacy-signed.json').read_text());descriptor=verify(envelope)
 if descriptor['schema']!=1 or descriptor['version']!=identity['version'] or descriptor['sequence']!=identity['sequence']:raise ValueError('Invalid legacy bridge')
 platform=identity['channel'];current=json.loads(remote('cat '+ROOT+'/public/releases/'+platform+'/latest.json'))
 previous=verify(current)
 if previous['sequence']>=descriptor['sequence']:raise ValueError('Never downgrade or overwrite an existing legacy sequence')
 # Verify that the exact immutable GitHub objects are already public, including size/digest.
 tag=json.loads(subprocess.check_output(['gh','api','repos/afonasev/star-racing/releases/tags/v'+descriptor['version']],text=True))
 if tag['draft'] or not tag['prerelease']:raise ValueError('Bridge requires a published test candidate')
 asset=next(x for x in tag['assets'] if x['name']==descriptor['fileName'])
 if asset['size']!=descriptor['size'] or asset.get('digest')!='sha256:'+descriptor['sha256'].lower():raise ValueError('Bridge asset mismatch')
 path=urllib.parse.urlsplit(descriptor['url']).path
 if path in catalog:raise ValueError('Legacy immutable path already exists')
 catalog[path]=dict(name=descriptor['fileName'],url=url(descriptor['version'],descriptor['fileName']),size=descriptor['size'],sha256=descriptor['sha256'].lower());feeds[platform]=envelope
if set(feeds)!={'win-x64','osx-universal'}:raise ValueError('Bridge requires both platforms')
a.evidence.mkdir(parents=True,exist_ok=False);(a.evidence/'legacy-before.json').write_text(json.dumps(original,indent=2)+'\n')
with tempfile.TemporaryDirectory(prefix='star-racing-bridge-') as tmp:
 folder=Path(tmp);file=folder/'relay-catalog.json';file.write_text(json.dumps(catalog,indent=2)+'\n')
 subprocess.run(['scp','-q',str(file),a.host+':'+ROOT+'/service/relay-catalog.json.next'],check=True)
 remote('mv '+ROOT+'/service/relay-catalog.json.next '+ROOT+'/service/relay-catalog.json && systemctl restart star-racing-legacy-relay')
 # Confirm relay routing before old clients can discover the new version.
 for platform,envelope in feeds.items():
  descriptor=verify(envelope)
  with urllib.request.urlopen(urllib.request.Request(descriptor['url'],method='HEAD'),timeout=60) as response:
   if response.status!=200 or int(response.headers['Content-Length'])!=descriptor['size']:raise ValueError('Legacy relay smoke failed; old feeds retained')
 for platform,envelope in feeds.items():
  file=folder/(platform+'.json');file.write_text(json.dumps(envelope)+'\n');target=ROOT+'/public/releases/'+platform+'/latest.json'
  subprocess.run(['scp','-q',str(file),a.host+':'+target+'.next'],check=True);remote('mv '+target+'.next '+target)
  received=json.loads(remote('cat '+target))
  if received!=envelope:raise ValueError('Legacy metadata readback failed')
  (a.evidence/(platform+'.json')).write_text(json.dumps(received,indent=2)+'\n')
(a.evidence/'relay-catalog.json').write_text(json.dumps(catalog,indent=2)+'\n')
print('LEGACY_TEST_BRIDGE_VERIFIED',a.evidence)

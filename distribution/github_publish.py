#!/usr/bin/env python3
"""Publish immutable release assets and verify GitHub's asset SHA256 readback."""
import argparse, hashlib, json, subprocess
from pathlib import Path
from publish import validate_identity
REPO='afonasev/star-racing'
def gh(*args): return subprocess.check_output(['gh',*args],text=True)
def publish(directory,packages_only=False):
    directory=Path(directory);identity=validate_identity(directory);tag='v'+identity['version']
    found=subprocess.run(['gh','release','view',tag,'-R',REPO],capture_output=True)
    if found.returncode:
        subprocess.run(['gh','release','create',tag,'-R',REPO,'--prerelease','--title','Star Racing '+identity['version'],'--notes','Unsigned Windows/macOS candidate. Install/update acceptance remains pending.'],check=True)
    existing={x['name']:x for x in json.loads(gh('api','repos/'+REPO+'/releases/tags/'+tag))['assets']}
    published=[]
    for entry in identity['files']:
        if not entry['name'].endswith(('.nupkg',) if packages_only else ('.nupkg','Setup.exe','Setup.pkg')):continue
        if entry['name'] not in existing:
            subprocess.run(['gh','release','upload',tag,str(directory/entry['name']),'-R',REPO],check=True)
        assets=json.loads(gh('api','repos/'+REPO+'/releases/tags/'+tag))['assets']
        asset=next(x for x in assets if x['name']==entry['name'])
        if asset['size']!=entry['size'] or asset.get('digest')!='sha256:'+entry['sha256']:
            raise ValueError('GitHub immutable hash/size mismatch: '+entry['name'])
        published.append(dict(name=entry['name'],url=asset['browser_download_url'],sha256=entry['sha256'],size=entry['size']))
    return dict(channel=identity['channel'],version=identity['version'],assets=published)
if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--release',type=Path,action='append',required=True);p.add_argument('--readback',type=Path,required=True);p.add_argument('--packages-only',action='store_true')
    a=p.parse_args();out=[publish(d,a.packages_only) for d in a.release];a.readback.write_text(json.dumps(out,indent=2)+'\n');print('GITHUB_RELEASES_VERIFIED',len(out))

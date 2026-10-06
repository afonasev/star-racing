#!/usr/bin/env python3
"""Publish immutable artifacts, verify remote SHA256, then switch signed metadata."""
import argparse,hashlib,json,re,shlex,subprocess,uuid,tempfile,zipfile
from pathlib import Path

ROOT='/opt/star-racing-desktop/public/releases'

def validate_windows_payload(package):
    with zipfile.ZipFile(package) as archive:
        names=set(archive.namelist())
        required={'lib/app/Star Racing.exe','lib/app/UnityPlayer.dll','lib/app/velopack_libc.dll'}
        if not required.issubset(names):raise ValueError('Windows payload is missing bootstrap runtime files')

def validate_identity(directory,public_key=None):
    directory=Path(directory).resolve();identity=json.loads((directory/'identity.json').read_text())
    if identity['channel'] not in ('win-x64','osx-universal') or not re.fullmatch(r'\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?',identity['version']):raise ValueError('Invalid release identity')
    for entry in identity['files']:
        if entry['name']!=Path(entry['name']).name or '\\' in entry['name']:raise ValueError('Unsafe artifact path')
        file=directory/entry['name']
        with file.open('rb') as stream:sha=hashlib.file_digest(stream,'sha256').hexdigest()
        if file.stat().st_size!=entry['size'] or sha!=entry['sha256']:raise ValueError('Artifact differs from immutable identity: '+entry['name'])
    names=[e['name'] for e in identity['files']]
    if not any(n.endswith('-full.nupkg') for n in names) or not any(n.endswith('Setup.exe' if identity['channel']=='win-x64' else 'Setup.pkg') for n in names):raise ValueError('Missing installer/full package')
    if 'signed.json' not in names:raise ValueError('Missing signed descriptor')
    import base64
    signed=json.loads((directory/'signed.json').read_text())
    serialized=base64.b64decode(signed['payloadBase64'],validate=True)
    signature=base64.b64decode(signed['signatureBase64'],validate=True)
    if signed['keyId']!='star-racing-test-2026':raise ValueError('Unknown descriptor key')
    public_key=Path(public_key or Path(__file__).resolve().parent/'update-public.pem')
    with tempfile.TemporaryDirectory(prefix='star-racing-descriptor-') as temporary:
        sig=Path(temporary)/'signature';sig.write_bytes(signature)
        verified=subprocess.run(['openssl','dgst','-sha256','-verify',str(public_key),'-signature',str(sig)],input=serialized,capture_output=True)
        if verified.returncode:raise ValueError('Release descriptor signature invalid')
    payload=json.loads(serialized)
    if payload['version']!=identity['version'] or payload['channel']!=identity['channel'] or payload['sequence']!=identity['sequence']:raise ValueError('Descriptor/identity mismatch')
    full=[e for e in identity['files'] if e['name'].endswith('-full.nupkg')]
    if len(full)!=1:raise ValueError('Expected one full package')
    if payload['schema'] not in (1,2) or payload['appId']!='tech.afonasev.star-racing.'+identity['channel']:raise ValueError('Descriptor identity invalid')
    if (payload['fileName'],payload['size'])!=(full[0]['name'],full[0]['size']) or payload['sha256'].lower()!=full[0]['sha256'].lower():raise ValueError('Descriptor package mismatch')
    expected='https://racing.afonasev.tech/releases/'+identity['channel']+'/'+identity['version']+'/'+full[0]['name']
    if payload['schema']==2:
        expected='https://github.com/afonasev/star-racing/releases/download/v'+identity['version']+'/'+full[0]['name']
        track=payload.get('releaseTrack')
        if track not in ('production','test') or (track=='test')!=('-' in identity['version']):raise ValueError('Descriptor track mismatch')
        installers=[e for e in identity['files'] if e['name'].endswith(('Setup.exe','Setup.pkg'))]
        if len(installers)!=1:raise ValueError('Expected one installer')
        expected_installer=installers[0]
        signed_installer=payload.get('installer',{})
        for key,field in [('fileName','name'),('size','size'),('sha256','sha256')]:
            if signed_installer.get(key)!=expected_installer[field]:raise ValueError('Signed installer mismatch')
        if signed_installer.get('url')!='https://github.com/afonasev/star-racing/releases/download/v'+identity['version']+'/'+expected_installer['name']:raise ValueError('Installer URL mismatch')
    if payload['url']!=expected:raise ValueError('Descriptor URL mismatch')
    if identity['channel']=='win-x64':validate_windows_payload(directory/full[0]['name'])
    return identity

def downloads(identities):
    out={}
    for identity in identities:
        extension='Setup.exe' if identity['channel']=='win-x64' else 'Setup.pkg'
        assets=[e for e in identity['files'] if e['name'].endswith(extension)]
        if len(assets)!=1:raise ValueError('Expected one installer')
        asset=assets[0]
        out[identity['channel']]=dict(version=identity['version'],size=asset['size'],sha256=asset['sha256'],url='/releases/'+identity['channel']+'/'+identity['version']+'/'+asset['name'])
    if set(out)!=set(['win-x64','osx-universal']):raise ValueError('Both installers are required')
    return dict(schema=1,downloads=out)

def main():
    p=argparse.ArgumentParser();p.add_argument('--release',type=Path,action='append',required=True);p.add_argument('--host',default='gfe');p.add_argument('--latest',action='store_true');p.add_argument('--downloads-output',type=Path)
    a=p.parse_args();identities=[validate_identity(d) for d in a.release]
    if a.downloads_output:a.downloads_output.write_text(json.dumps(downloads(identities),indent=2)+'\n')
    def remote(script):return subprocess.check_output(['ssh','-o','BatchMode=yes','-o','ConnectTimeout=10',a.host,script],text=True)
    for directory,identity in zip(a.release,identities):
        channel=identity['channel'];version=identity['version'];parent=ROOT+'/'+channel;target=parent+'/'+version
        required=[x for x in identity['files'] if x['name'].endswith(('.nupkg','Setup.exe','Setup.pkg')) or x['name']=='signed.json']
        exists=remote('test -d '+shlex.quote(target)+' && echo yes || echo no').strip()=='yes'
        if not exists:
            stage=parent+'/.incoming-'+uuid.uuid4().hex
            remote('mkdir -p '+shlex.quote(stage))
            for entry in required:
                subprocess.run(['scp','-q',str((directory/entry['name']).resolve()),a.host+':'+stage+'/'+entry['name']],check=True)
            verified=stage
        else:verified=target
        for entry in required:
            actual=remote('sha256sum '+shlex.quote(verified+'/'+entry['name'])).split()[0]
            if actual!=entry['sha256']:raise ValueError('Remote hash mismatch; immutable release was not replaced')
        if not exists:remote('mv -T '+shlex.quote(stage)+' '+shlex.quote(target))
        if a.latest:
            # Ensure a newer metadata sequence is never silently replaced by an older one.
            check=remote('test -f '+shlex.quote(parent+'/latest.json')+' && cat '+shlex.quote(parent+'/latest.json')+' || true').strip()
            if check:
                import base64
                previous=json.loads(base64.b64decode(json.loads(check)['payloadBase64']))
                if previous['sequence']>identity['sequence']:raise ValueError('Refusing latest downgrade')
                if previous['sequence']==identity['sequence'] and previous['version']!=version:raise ValueError('Sequence already used by another version')
            remote('cp '+shlex.quote(target+'/signed.json')+' '+shlex.quote(parent+'/latest.json.next')+' && mv '+shlex.quote(parent+'/latest.json.next')+' '+shlex.quote(parent+'/latest.json'))
        print('REMOTE_RELEASE_VERIFIED',channel,version,len(required))

if __name__=='__main__':main()

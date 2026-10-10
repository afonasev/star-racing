#!/usr/bin/env python3
"""Package an exact Unity Mono Player; sign an immutable full-update description."""
import argparse, base64, datetime, hashlib, json, os, re, shutil, subprocess, zipfile
from pathlib import Path


def digest(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--track', choices=['production','test'], required=True)
    p.add_argument('--channel', choices=['win-x64','osx-universal'], required=True)
    p.add_argument('--version', required=True)
    p.add_argument('--sequence', type=int, required=True)
    p.add_argument('--player', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--key', type=Path)
    p.add_argument('--unsigned-production', action='store_true')
    p.add_argument('--dotnet', type=Path, required=True)
    p.add_argument('--vpk', type=Path, required=True)
    p.add_argument('--notes', required=True)
    p.add_argument('--mingw', default='x86_64-w64-mingw32-g++')
    p.add_argument('--makensis', default='makensis')
    a=p.parse_args()
    if not re.fullmatch(r'\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?', a.version):p.error('Invalid SemVer')
    if not 0<a.sequence<2147483648:p.error('sequence must be positive Int32')
    root=Path(__file__).resolve().parent
    if (a.track=='test')!=('-' in a.version):raise ValueError('Version/track mismatch')
    if a.unsigned_production and a.track!='production':raise ValueError('Unsigned route is production-only')
    if not a.unsigned_production:
        if not a.key: p.error('--key is required for signed routes')
        # Verify the private key against the public key committed with the Player.
        pub=subprocess.check_output(['openssl','pkey','-in',str(a.key.resolve()),'-pubout'])
        if pub != (root/'update-public.pem').read_bytes():raise ValueError('Signing key is not the pinned Player key')
    a.output=a.output.resolve(); a.player=a.player.resolve();a.output.mkdir(parents=True,exist_ok=True)
    release=a.output/a.channel/a.version
    if release.exists():raise ValueError('Release already exists; immutable outputs must never be overwritten')
    release.mkdir(parents=True)
    payload=a.output/('payload-'+a.channel+'-'+a.version)
    if a.channel=='osx-universal':payload=Path(str(payload)+'.app')
    if payload.exists():raise ValueError('Payload already exists')
    env=os.environ.copy();env['DOTNET_ROOT']=str(a.dotnet.resolve().parent)
    if a.channel=='osx-universal':
        if a.player.suffix!='.app' or not a.player.is_dir():p.error('Expected macOS .app')
        subprocess.run(['ditto',str(a.player),str(payload)],check=True)
        import plistlib
        info=plistlib.loads((payload/'Contents/Info.plist').read_bytes())
        if info.get('CFBundleShortVersionString')!=a.version:raise ValueError('Player version mismatch')
        exe=info['CFBundleExecutable']
        icon=info.get('CFBundleIconFile','')
        if not icon or not (payload/'Contents/Resources'/icon).is_file():raise ValueError('Packaged macOS application icon missing')
    else:
        if not (a.player/'Star Racing.exe').is_file():p.error('Expected Windows Player directory')
        shutil.copytree(a.player,payload,ignore=shutil.ignore_patterns('*_DoNotShip','*.pdb'))
        exe='Star Racing.exe'
        # Use the vendor import library from the pinned native SDK; compiler never modifies source Player.
        native_zip=root.parent/'.local/desktop/velopack-native.zip'
        if not native_zip.exists():raise ValueError('Pinned native archive missing: run distribution/prepare.py')
        with zipfile.ZipFile(native_zip) as z:
            lib=payload/'velopack.dll.lib';lib.write_bytes(z.read('lib/velopack_libc_win_x64_msvc.dll.lib'))
        resource=payload/'icon.o'
        rc=payload/'icon.rc';rc.write_text('1 ICON "'+(root/'icons/Star-Racing.ico').as_posix()+'"\n')
        subprocess.run([a.mingw.replace('g++','windres'),str(rc),str(resource)],check=True)
        subprocess.run([a.mingw,'-std=c++17','-O2','-static','-municode','-mwindows',str(root/'windows/bootstrap.cpp'),str(resource),str(lib),'-o',str(payload/exe)],check=True)
        lib.unlink();rc.unlink();resource.unlink();shutil.copyfile(root/'windows/velopack_libc_win_x64_msvc.dll',payload/'velopack_libc.dll')
        # The vendor import library names velopack_libc.dll, irrespective of its
        # archive filename. Fail packaging if that required runtime is absent.
        imports=subprocess.check_output([a.mingw.replace('g++','objdump'),'-p',str(payload/exe)],text=True)
        imported=re.findall(r'DLL Name:\s*(\S+)',imports)
        if 'velopack_libc.dll' not in imported or not (payload/'velopack_libc.dll').is_file():
            raise ValueError('Windows bootstrap native dependency is missing')
    notes=release/'notes.md';notes.write_text(a.notes+'\n')
    cmd=[str(a.vpk.resolve())]+(['[win]'] if a.channel=='win-x64' else [])
    cmd+=['--skip-updates','pack','--packId','tech.afonasev.star-racing.'+a.channel,'--packVersion',a.version,
          '--packDir',str(payload),'--mainExe',exe,'--channel',a.channel,'--packTitle','Star Racing',
          '--releaseNotes',str(notes),'--delta','None','--outputDir',str(release)]
    if a.channel=='win-x64':cmd+=['--runtime','win-x64','--icon',str(root/'icons/Star-Racing.ico')]
    else:cmd+=['--signAppIdentity','-'] # Ad-hoc seal, no Developer ID or notarization.
    subprocess.run(cmd,check=True,env=env)
    if a.channel=='win-x64':
        # Distribute the wizard; stock Setup would take ownership of HKCU ARP.
        from windows.build_installer import build
        sdk_setup=next(release.glob('*Setup.exe'))
        portable=next(release.glob('*Portable.zip'))
        sdk_setup.unlink() # Superseded one-click installer is not distributed.
        wizard_output=release/('Star-Racing-'+a.version+'-Windows-Setup.exe')
        wizard_identity=build(portable,wizard_output,a.makensis,a.version)
        (release/'installer-policy.json').write_text(json.dumps(wizard_identity,indent=2)+'\n')
    else:
        setup=next(release.glob('*Setup.pkg'))
        setup.rename(release/('Star-Racing-'+a.version+'-macOS-Setup.pkg'))
    # Velopack mutates the final Mac bundle; preserve executable bits and ad-hoc signatures for ARM.
    # Portable zip from vpk is the canonical final installed bundle, not the original Unity build.
    feed=json.loads((release/('releases.'+a.channel+'.json')).read_text())
    assets=[x for x in feed['Assets'] if x['Type']=='Full' and x['Version']==a.version]
    if len(assets)!=1:raise ValueError('Expected one exact full release')
    asset=assets[0];package=release/asset['FileName']
    if package.stat().st_size!=asset['Size'] or digest(package).upper()!=asset['SHA256'].upper():raise ValueError('SDK asset mismatch')
    descriptor=dict(schema=2,appId='tech.afonasev.star-racing.'+a.channel,channel=a.channel,releaseTrack=a.track,version=a.version,
                    sequence=a.sequence,fileName=asset['FileName'],sha256=digest(package).upper(),size=package.stat().st_size,
                    url='https://github.com/afonasev/star-racing/releases/download/v'+a.version+'/'+asset['FileName'],notes=a.notes)
    if a.unsigned_production:
        descriptor['unsignedProductionCatalog']=True
        (release/'unsigned.json').write_text(json.dumps(descriptor,separators=(',',':'))+'\n')
    else:
        serialized=json.dumps(descriptor,ensure_ascii=False,separators=(',',':')).encode()
        signature=subprocess.run(['openssl','dgst','-sha256','-sign',str(a.key.resolve()),'-sigopt','rsa_padding_mode:pkcs1'],input=serialized,capture_output=True,check=True).stdout
        signed=dict(keyId='star-racing-test-2026',payloadBase64=base64.b64encode(serialized).decode(),signatureBase64=base64.b64encode(signature).decode())
        (release/'signed.json').write_text(json.dumps(signed)+'\n')
    identity=dict(version=a.version,sequence=a.sequence,channel=a.channel,
                  sourceRevision=subprocess.check_output(['git','rev-parse','HEAD'],cwd=root,text=True).strip(),
                  sourceStatus=subprocess.check_output(['git','status','--short'],cwd=root,text=True),
                  sourceDiffSHA256=hashlib.sha256(subprocess.check_output(['git','diff','HEAD','--binary'],cwd=root)).hexdigest(),
                  player=str(a.player),createdUTC=datetime.datetime.now(datetime.timezone.utc).isoformat(),
                  files=[dict(name=f.name,size=f.stat().st_size,sha256=digest(f)) for f in sorted(release.iterdir()) if f.is_file()])
    (release/'identity.json').write_text(json.dumps(identity,indent=2)+'\n')
    print('PACKAGED',a.channel,a.version,release)

if __name__=='__main__':main()

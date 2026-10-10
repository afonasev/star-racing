"""Signed immutable descriptors shared by packaging and publication."""
import base64, json, subprocess, hashlib, ssl, sys
from pathlib import Path
REPO='afonasev/star-racing'
KEY_ID='star-racing-test-2026'
def tls_context():
    # python.org macOS installs may omit CA links; use the OS trust bundle.
    system=Path('/etc/ssl/cert.pem')
    return ssl.create_default_context(cafile=str(system) if sys.platform=='darwin' and system.is_file() else None)
def sha(path):
    with Path(path).open('rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest()
def url(version,name):return f'https://github.com/{REPO}/releases/download/v{version}/{name}'
def sign(payload,key):
    raw=json.dumps(payload,ensure_ascii=False,separators=(',',':')).encode()
    signature=subprocess.run(['openssl','dgst','-sha256','-sign',str(key),'-sigopt','rsa_padding_mode:pkcs1'],input=raw,capture_output=True,check=True).stdout
    return dict(keyId=KEY_ID,payloadBase64=base64.b64encode(raw).decode(),signatureBase64=base64.b64encode(signature).decode())
def verify(envelope):
    import tempfile
    if envelope['keyId']!=KEY_ID:raise ValueError('Unknown signing key')
    raw=base64.b64decode(envelope['payloadBase64'],validate=True)
    with tempfile.TemporaryDirectory(prefix='star-racing-signature-') as tmp:
        sig=Path(tmp)/'signature';sig.write_bytes(base64.b64decode(envelope['signatureBase64'],validate=True))
        subprocess.run(['openssl','dgst','-sha256','-verify',str(Path(__file__).with_name('update-public.pem')),'-signature',str(sig)],input=raw,check=True,capture_output=True)
    return json.loads(raw)
def finalize(directory,track,key=None,*,unsigned_production=False):
    directory=Path(directory);identity=json.loads((directory/'identity.json').read_text())
    version=identity['version'];platform=identity['channel']
    if (track=='test')!=('-' in version):raise ValueError('Version/track mismatch')
    package=next(directory.glob('*-full.nupkg'))
    suffix='Windows-Setup.exe' if platform=='win-x64' else 'macOS-Setup.pkg'
    installer=directory/f'Star-Racing-{version}-{suffix}'
    descriptor=dict(schema=2,appId='tech.afonasev.star-racing.'+platform,channel=platform,releaseTrack=track,version=version,sequence=identity['sequence'],fileName=package.name,size=package.stat().st_size,sha256=sha(package).upper(),url=url(version,package.name),notes=(directory/'notes.md').read_text().strip(),installer=dict(fileName=installer.name,size=installer.stat().st_size,sha256=sha(installer),url=url(version,installer.name)))
    if unsigned_production:
        if track!='production':raise ValueError('Unsigned route is production-only')
        descriptor['unsignedProductionCatalog']=True
        (directory/'unsigned.json').write_text(json.dumps(descriptor,separators=(',',':'))+'\n')
        identity['releaseTrack']=track
        identity['files']=[dict(name=f.name,size=f.stat().st_size,sha256=sha(f)) for f in sorted(directory.iterdir()) if f.is_file() and f.name!='identity.json']
        (directory/'identity.json').write_text(json.dumps(identity,indent=2)+'\n')
        return descriptor
    envelope=sign(descriptor,key);verify(envelope)
    (directory/'signed.json').write_text(json.dumps(envelope)+'\n')
    # Only migration publisher uses this schema1. New Players never request it.
    legacy={k:v for k,v in descriptor.items() if k not in ('releaseTrack','installer')};legacy['schema']=1
    legacy['url']=f'https://racing.afonasev.tech/releases/{platform}/{version}/{package.name}'
    (directory/'legacy-signed.json').write_text(json.dumps(sign(legacy,key))+'\n')
    identity['releaseTrack']=track
    identity['files']=[dict(name=f.name,size=f.stat().st_size,sha256=sha(f)) for f in sorted(directory.iterdir()) if f.is_file() and f.name!='identity.json']
    (directory/'identity.json').write_text(json.dumps(identity,indent=2)+'\n')
    return envelope

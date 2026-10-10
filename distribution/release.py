#!/usr/bin/env python3
"""One-command exact-source build, signed draft/readback, and channel promotion."""
import argparse,base64,json,os,re,shutil,subprocess,tempfile,time,urllib.request,tarfile
from pathlib import Path
from metadata import REPO,sha,verify,finalize,tls_context
from publish import validate_identity
ROOT=Path(__file__).resolve().parents[1]
def publication_policy():
    """Read-only summary; automated guards and human gates are separate."""
    return dict(schema=1, repository=REPO, documentation='docs/PUBLISHING.md',
        tracks={'test':'prerelease SemVer; channel-test', 'production':'stable SemVer; channel-production'},
        platforms=['win-x64','osx-universal'],
        enforced=['clean source committed and available on GitHub',
                  'private signing key outside repository matching pinned public key',
                  'positive Int32 sequence, increasing version/sequence within track',
                  'reject existing versions including drafts; no asset overwrite',
                  'exclusive per-track publisher reservation',
                  'build both platforms; exact-source Windows compiler in GitHub Actions',
                  'sign platform/version/size/SHA256 and installer metadata locally',
                  'upload draft; check exact asset set, API digest and real download SHA256',
                  'publish complete version first; switch requested channel pointer last'],
        manualGates=['review appropriate checks before committing/pushing source',
                     'physical Windows/macOS acceptance and explicit authorization before production',
                     'preserve exact release artifacts and evidence before owned cleanup',
                     'retain legacy VPS feeds/relay until old-client migration is confirmed'],
        routineVpsUpload=False, metadataSignatureIsOsCertificate=False)
def run(args,**kw):return subprocess.run([str(x) for x in args],check=True,**kw)
def api(endpoint,method='GET',body=None):
    args=['gh','api',f'repos/{REPO}/'+endpoint,'-X',method]
    if body is not None:args+=['--input','-']
    result=subprocess.run(args,input=json.dumps(body) if body is not None else None,text=True,capture_output=True)
    if result.returncode:raise RuntimeError(result.stderr.strip())
    return json.loads(result.stdout) if result.stdout.strip() else None
def optional_release(tag):
    result=subprocess.run(['gh','api',f'repos/{REPO}/releases/tags/{tag}'],capture_output=True,text=True)
    if result.returncode:
        if '404' in result.stderr:
            # The tag endpoint excludes drafts for workflow tokens. A draft is
            # still an existing immutable candidate, never permission to rebuild.
            listed=subprocess.run(['gh','api','--paginate','--slurp',f'repos/{REPO}/releases?per_page=100'],capture_output=True,text=True)
            if listed.returncode:raise RuntimeError(listed.stderr)
            matches=[item for page in json.loads(listed.stdout) for item in page if item['tag_name']==tag]
            if len(matches)>1:raise ValueError('Duplicate release tag')
            return matches[0] if matches else None
        raise RuntimeError(result.stderr)
    return json.loads(result.stdout)
def validate_track(track,version):
    if not re.fullmatch(r'\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?',version) or (track=='test')!=('-' in version):raise ValueError('Production needs stable SemVer; test needs a prerelease version')
def version_key(version):
    main,sep,pre=version.partition('-')
    return tuple(int(x) for x in main.split('.'))+(not bool(sep),tuple((0,int(x)) if x.isdigit() else (1,x) for x in pre.split('.')) if sep else ())
def toolchain():
    config=json.loads((ROOT/'distribution/toolchain.json').read_text())
    folder=ROOT/'.local/toolchain';dotnet=folder/'dotnet/dotnet';vpk=folder/'vpk/vpk'
    if not dotnet.exists():
        archive=folder/'sdk.tar.gz';folder.mkdir(parents=True,exist_ok=True)
        with urllib.request.urlopen(config['dotnet']['url'],timeout=60,context=tls_context()) as response, archive.open('wb') as output:
            shutil.copyfileobj(response,output)
        import hashlib
        with archive.open('rb') as f:digest=hashlib.file_digest(f,'sha512').hexdigest()
        if digest!=config['dotnet']['sha512']:raise ValueError('SDK hash mismatch')
        (folder/'dotnet').mkdir();
        with tarfile.open(archive) as tar:tar.extractall(folder/'dotnet',filter='data')
        archive.unlink()
    env=dict(os.environ,DOTNET_ROOT=str(dotnet.parent))
    if not vpk.exists():run([dotnet,'tool','install','vpk','--version',config['velopack'],'--tool-path',folder/'vpk'],env=env)
    if subprocess.check_output([dotnet,'--version'],env=env,text=True).strip()!=config['dotnet']['version']:raise ValueError('Unexpected .NET SDK')
    run(['python3',ROOT/'distribution/prepare.py'])
    return dotnet,vpk
def build_players(version,track,evidence,unsigned_production=False):
    # Build preparation and Unity import may serialize these owned files. Restore
    # them even on failure so an exact-source release stays clean after building.
    paths=[ROOT/'unity-prototype/ProjectSettings/ProjectSettings.asset']
    paths+=list((ROOT/'unity-prototype/Assets/StarRacing/Generated').rglob('*'))
    paths+=list((ROOT/'unity-prototype/Assets/StarRacing').rglob('*.meta'))
    original={p:p.read_bytes() for p in paths if p.is_file()}
    env=dict(os.environ,STAR_RACING_VERSION=version,STAR_RACING_RELEASE_TRACK=track,BEE_BUILD_THREADS='2')
    if unsigned_production:env['STAR_RACING_UNSIGNED_PRODUCTION']='1'
    try:
        for platform,method in [('mac','BuildMac'),('windows','BuildWindows')]:
            log=evidence/('build-'+platform+'.log')
            run([ROOT/'tools/unity.sh','shared','-batchmode','-nographics','-disableManagedDebugger','-quit','-executeMethod','StarRacingPrototype.PrototypeBuilder.'+method,'-logFile',log],env=env)
            if 'PROTOTYPE_BUILD_OK' not in log.read_text():raise ValueError('Native build receipt missing')
    finally:
        for p,data in original.items():p.write_bytes(data)
def upload(tag,file,name=None):
    name=name or file.name
    # gh supports an asset label after #, not a renamed filename: use a temporary alias.
    if name!=file.name:
        with tempfile.TemporaryDirectory(prefix='star-racing-upload-') as tmp:
            target=Path(tmp)/name;shutil.copyfile(file,target);run(['gh','release','upload',tag,target,'-R',REPO])
    else:run(['gh','release','upload',tag,file,'-R',REPO])
def wizard(tag,source,folder,evidence):
    portable=next(folder.glob('*Portable.zip'));upload(tag,portable)
    started=time.time()
    run(['gh','workflow','run','windows-wizard.yml','-R',REPO,'-f','tag='+tag,'-f','source='+source,'-f','portable='+portable.name,'-f','sha256='+sha(portable)])
    deadline=time.monotonic()+1800
    while time.monotonic()<deadline:
        runs=api('actions/workflows/windows-wizard.yml/runs?event=workflow_dispatch&per_page=20')['workflow_runs']
        matches=[r for r in runs if r.get('display_title')=='Windows wizard '+tag and r['created_at']>=time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime(started-5))]
        if matches:
            job=max(matches,key=lambda x:x['id'])
            if job['status']=='completed':
                (evidence/'windows-workflow.json').write_text(json.dumps(job,indent=2)+'\n')
                if job['conclusion']!='success':raise RuntimeError('Windows compiler failed: '+job['html_url'])
                break
        time.sleep(10)
    else:raise TimeoutError('Windows compiler timed out; draft stays unpublished')
    setup=f'Star-Racing-{tag[1:]}-Windows-Setup.exe'
    run(['gh','release','download',tag,'-R',REPO,'-p',setup,'-p','installer-policy-win-x64.json','-D',folder])
    policy=folder/'installer-policy-win-x64.json'
    report=json.loads(policy.read_text())
    if report['sourceRevision']!=source or report['portableSHA256']!=sha(portable) or report['installerSHA256']!=sha(folder/setup):raise ValueError('CI wizard readback mismatch')
    policy.rename(folder/'installer-policy.json')
def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--show-policy',action='store_true',help='Print publication rules without building or contacting GitHub')
    p.add_argument('--track',choices=['production','test']);p.add_argument('--version');p.add_argument('--sequence',type=int)
    p.add_argument('--key',type=Path,default=os.environ.get('STAR_RACING_SIGNING_KEY'));p.add_argument('--unsigned-production',action='store_true');p.add_argument('--notes',default='Desktop updater candidate; physical platform acceptance pending.')
    a=p.parse_args()
    if a.show_policy:
        print(json.dumps(publication_policy(),indent=2,ensure_ascii=False));return
    if a.track is None or a.version is None or a.sequence is None:p.error('--track, --version and --sequence are required for publication')
    validate_track(a.track,a.version)
    if a.unsigned_production and a.track!='production':p.error('Unsigned metadata is production-only')
    if not a.unsigned_production and (not a.key or not a.key.is_file()):p.error('Set STAR_RACING_SIGNING_KEY to the private key outside the repository')
    key=a.key.resolve() if a.key else None
    if key and key.is_relative_to(ROOT):p.error('Private key must stay outside the repository')
    if not 0<a.sequence<2147483648:p.error('Sequence must be a positive Int32')
    if subprocess.check_output(['git','status','--porcelain'],cwd=ROOT,text=True).strip():raise ValueError('Commit and verify source before publication')
    source=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()
    api('commits/'+source) # Commit must already be published to this repository.
    tag='v'+a.version
    if optional_release(tag):raise ValueError('Version already exists, including draft. Inspect it; never overwrite a release')
    previous=optional_release('channel-'+a.track)
    if previous:
        catalog=json.loads(previous['body'])
        for envelope in catalog['platforms'].values():
            old=envelope if catalog.get('unsignedProductionCatalog') else verify(envelope)
            if old['releaseTrack']!=a.track or old['sequence']>=a.sequence or version_key(old['version'])>=version_key(a.version):raise ValueError('Non-monotonic channel sequence')
    lock='git/refs/tags/_publishing-'+a.track
    # Atomic ref creation serializes publishers across release Macs/hosts.
    # Never steal an existing reservation; inspect its owner after a crashed run.
    api('git/refs','POST',dict(ref='refs/tags/_publishing-'+a.track,sha=source))
    try:publish_candidate(a,key,source,tag,previous)
    finally:api(lock,'DELETE')
def publish_candidate(a,key,source,tag,previous):
    output=ROOT/'.local/releases'/a.version
    output.mkdir(parents=True,exist_ok=False);evidence=output/'evidence';evidence.mkdir()
    dotnet,vpk=toolchain();build_players(a.version,a.track,evidence,a.unsigned_production)
    if subprocess.check_output(['git','status','--porcelain'],cwd=ROOT,text=True).strip():raise ValueError('Build changed source; publication aborted')
    directories=[]
    for channel,player in [('osx-universal',ROOT/'unity-prototype/Builds/macOS/Star Racing.app'),('win-x64',ROOT/'unity-prototype/Builds/Windows')]:
        args=['python3',ROOT/'distribution/package.py','--track',a.track,'--channel',channel,'--version',a.version,'--sequence',a.sequence,'--player',player,'--output',output,'--dotnet',dotnet,'--vpk',vpk,'--notes',a.notes]
        if a.unsigned_production:args+=['--unsigned-production']
        else:args+=['--key',key]
        if channel=='win-x64':args+=['--defer-windows-installer']
        run(args);directories.append(output/channel/a.version)
    draft=api('releases','POST',dict(tag_name=tag,target_commitish=source,name='Star Racing '+a.version,body=a.notes,draft=True,prerelease=a.track=='test',make_latest='false'))
    wizard(tag,source,directories[1],evidence)
    if not a.unsigned_production:finalize(directories[1],a.track,key)
    remote={x['name']:x for x in api('releases/'+str(draft['id']))['assets']}
    expected={};catalog=dict(schema=2 if a.unsigned_production else 1,track=a.track,version=a.version,sourceRevision=source,platforms={})
    if a.unsigned_production:catalog['unsignedProductionCatalog']=True
    for folder in directories:
        identity=validate_identity(folder,allow_unsigned_production=a.unsigned_production);channel=identity['channel'];catalog['platforms'][channel]=json.loads((folder/('unsigned.json' if a.unsigned_production else 'signed.json')).read_text())
        for file in sorted(folder.iterdir()):
            if not file.is_file():continue
            name=file.name
            if name in ('signed.json','unsigned.json','legacy-signed.json','identity.json','notes.md','installer-policy.json'):
                name=file.stem+'-'+channel+file.suffix
            expected[name]=dict(size=file.stat().st_size,sha256=sha(file),local=str(file))
            if name not in remote:upload(tag,file,name)
            elif remote[name]['size']!=file.stat().st_size or remote[name].get('digest')!='sha256:'+sha(file):raise ValueError('Draft asset conflict')
    manifest=output/'manifest.json';manifest.write_text(json.dumps(dict(version=a.version,track=a.track,sourceRevision=source,assets=expected),indent=2)+'\n');upload(tag,manifest)
    expected[manifest.name]=dict(size=manifest.stat().st_size,sha256=sha(manifest),local=str(manifest))
    assets={x['name']:x for x in api('releases/'+str(draft['id']))['assets']}
    if set(assets)!=set(expected):raise ValueError('Incomplete or unexpected draft assets')
    readback=[]
    # Actual authenticated downloads, not just API digest comparisons.
    with tempfile.TemporaryDirectory(prefix='star-racing-draft-readback-') as tmp:
        for name,entry in expected.items():
            asset=assets[name]
            if asset['size']!=entry['size'] or asset.get('digest')!='sha256:'+entry['sha256']:raise ValueError('GitHub asset digest mismatch')
            run(['gh','release','download',tag,'-R',REPO,'-p',name,'-D',tmp])
            received=Path(tmp)/name
            if received.stat().st_size!=entry['size'] or sha(received)!=entry['sha256']:raise ValueError('Real download hash mismatch')
            readback.append(dict(name=name,size=entry['size'],sha256=entry['sha256'],url=asset['browser_download_url']))
            received.unlink()
    (evidence/'download-readback.json').write_text(json.dumps(readback,indent=2)+'\n')
    # Detect a concurrent channel publisher before changing public state.
    fresh=optional_release('channel-'+a.track)
    if (fresh or {}).get('body')!=(previous or {}).get('body'):raise ValueError('Channel changed during build; draft left for review')
    api('releases/'+str(draft['id']),'PATCH',dict(draft=False,prerelease=a.track=='test',make_latest='true' if a.track=='production' else 'false'))
    pointer=dict(body=json.dumps(catalog,separators=(',',':')),prerelease=a.track=='test',draft=False,make_latest='false',name='Star Racing '+a.track+' channel')
    if previous:api('releases/'+str(previous['id']),'PATCH',pointer)
    else:api('releases','POST',dict(pointer,tag_name='channel-'+a.track,target_commitish=source))
    (evidence/'channel.json').write_text(json.dumps(catalog,indent=2)+'\n')
    print('PUBLISHED',f'https://github.com/{REPO}/releases/tag/{tag}',evidence)
if __name__=='__main__':main()

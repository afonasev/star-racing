"""GitHub-only compiler worker. No signing secret and no published asset edits."""
import hashlib,json,os,re,subprocess,tempfile
from pathlib import Path
from build_installer import build
repo=os.environ['GITHUB_REPOSITORY'];tag=os.environ['INPUT_TAG'];source=os.environ['INPUT_SOURCE'];name=os.environ['INPUT_PORTABLE'];expected=os.environ['INPUT_SHA256']
if repo!='afonasev/star-racing' or not re.fullmatch(r'v\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?',tag) or not re.fullmatch('[0-9a-f]{40}',source) or not re.fullmatch('[0-9a-f]{64}',expected) or name!=Path(name).name or not name.endswith('Portable.zip'):raise ValueError('Invalid compiler inputs')
if subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()!=source:raise ValueError('Wrong source revision')
release=json.loads(subprocess.check_output(['gh','api',f'repos/{repo}/releases/tags/{tag}'],text=True))
if not release['draft']:raise ValueError('Compiler must only write draft assets')
version=tag[1:];setup=f'Star-Racing-{version}-Windows-Setup.exe'
if any(x['name']==setup for x in release['assets']):raise ValueError('Installer already exists; never overwrite')
with tempfile.TemporaryDirectory(prefix='star-racing-wizard-') as tmp:
    folder=Path(tmp)
    subprocess.run(['gh','release','download',tag,'-R',repo,'-p',name,'-D',tmp],check=True)
    portable=folder/name
    with portable.open('rb') as stream:actual=hashlib.file_digest(stream,'sha256').hexdigest()
    if actual!=expected:raise ValueError('Input portable hash mismatch')
    output=folder/setup;identity=build(portable,output,version=version)
    identity['compiler']=subprocess.check_output(['makensis','-VERSION'],text=True).strip();identity['sourceRevision']=source
    policy=folder/'installer-policy-win-x64.json';policy.write_text(json.dumps(identity,indent=2)+'\n')
    subprocess.run(['gh','release','upload',tag,str(output),str(policy),'-R',repo],check=True)

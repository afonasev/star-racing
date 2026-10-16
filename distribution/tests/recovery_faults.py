#!/usr/bin/env python3
"""Run actual external agent source with synthetic packages/processes and a test trust root.
No Unity/Player/installer is built or launched. The production trust root is never modified.
"""
import argparse,base64,hashlib,json,os,re,subprocess,tempfile,time
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]

def run(dotnet,output):
 output.mkdir(parents=True,exist_ok=True)
 with tempfile.TemporaryDirectory(prefix='star-racing-recovery-faults-') as tmp:
  tmp=Path(tmp);key=tmp/'key.pem'
  subprocess.run(['openssl','genpkey','-algorithm','RSA','-pkeyopt','rsa_keygen_bits:2048','-out',key],check=True,stderr=subprocess.DEVNULL)
  modulus=subprocess.check_output(['openssl','rsa','-in',key,'-noout','-modulus'],stderr=subprocess.DEVNULL).decode().strip().split('=')[1]
  source=ROOT/'unity-prototype/Assets/StarRacing/Runtime'
  testkey=re.sub(r'(const string Modulus\s*=\s*")[^"]+',lambda m:m[1]+base64.b64encode(bytes.fromhex(modulus)).decode(),(source/'DesktopUpdateKey.cs').read_text())
  (tmp/'DesktopUpdateKey.cs').write_text(testkey)
  files=[ROOT/'distribution/recovery/Program.cs',ROOT/'distribution/recovery/WindowsInstall.cs',source/'AuthenticatedUpdateSource.cs',source/'DesktopReleaseChannel.cs',source/'RecoveryTransaction.cs']
  project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><PackageReference Include="Velopack" Version="1.2.158"/><PackageReference Include="Newtonsoft.Json" Version="13.0.4"/>'
  project+=''.join('<Compile Include="'+str(f)+'"/>' for f in files+[tmp/'DesktopUpdateKey.cs'])+'</ItemGroup></Project>'
  (tmp/'Agent.csproj').write_text(project)
  env=dict(os.environ,DOTNET_ROOT=str(dotnet.parent))
  build=subprocess.run([dotnet,'build',tmp/'Agent.csproj','--nologo'],env=env,capture_output=True,text=True)
  (output/'fault-agent-compile.log').write_text(build.stdout+build.stderr);build.check_returncode()
  dll=tmp/'bin/Debug/net8.0/Agent.dll';results=[]
  for scenario in ['healthy','health-exit','crash','apply-error','interrupted-apply','interrupted-pending','bad-health','tampered-target','tampered-previous','stale-token','false-success','cleanup-error']:
   folder=tmp/scenario;packages=folder/'packages';packages.mkdir(parents=True)
   def descriptor(version,content):
    name='racing-'+version+'-full.nupkg';(packages/name).write_bytes(content)
    data=dict(schema=2,appId='tech.afonasev.star-racing.win-x64',channel='win-x64',releaseTrack='production',version=version,sequence=1 if version=='0.3.5' else 2,fileName=name,size=len(content),sha256=hashlib.sha256(content).hexdigest(),url='https://github.com/afonasev/star-racing/releases/download/v'+version+'/'+name,notes='fixture')
    payload=json.dumps(data,separators=(',',':')).encode();signature=subprocess.run(['openssl','dgst','-sha256','-sign',key],input=payload,capture_output=True,check=True).stdout
    return json.dumps(dict(keyId='star-racing-test-2026',payloadBase64=base64.b64encode(payload).decode(),signatureBase64=base64.b64encode(signature).decode()))
   old=descriptor('0.3.5',b'A');new=descriptor('0.3.6',b'B')
   helper=folder/'helper.py';game=folder/'game.py';journal=folder/'transaction.json'
   helper.write_text('''#!/usr/bin/env python3
import json,sys
from pathlib import Path
args=sys.argv;root=Path(args[args.index('--rootDir')+1]);package=Path(args[args.index('--package')+1]);version=package.read_text()
with (root/'applications').open('a') as f:f.write(version+'\\n')
if root.name=='apply-error' and version=='B':sys.exit(7)
if root.name=='false-success' and version=='B':sys.exit(0)
(root/'installed').write_text(version)
(root/'sq.version').write_text('<package><metadata><id>tech.afonasev.star-racing.win-x64</id><version>'+('0.3.5' if version=='A' else '0.3.6')+'</version><channel>win-x64</channel></metadata></package>')
''');helper.chmod(0o755)
   game.write_text('''#!/usr/bin/env python3
import json,os,sys,time
from pathlib import Path
root=Path(__file__).parent;journal=root/'transaction.json'
if (root/'installed').read_text()=='A':(root/'restored-game').write_text('A');sys.exit(0)
if root.name in ['crash','apply-error']:sys.exit(3)
for i in range(100):
 r=json.loads(journal.read_text())
 if r['gamePid']==os.getpid():break
 time.sleep(.02)
else:sys.exit(4)
if root.name=='cleanup-error':
 old=root/'packages/racing-0.3.5-full.nupkg';old.unlink();old.mkdir()
health=dict(schema=1,pid=os.getpid(),started=r['gameStarted'],version=r['targetVersion'],token=r['token'] if root.name!='bad-health' else 'foreign')
(journal.parent/'health.tmp').write_text(json.dumps(health));os.replace(journal.parent/'health.tmp',str(journal)+'.health')
if root.name=='health-exit':sys.exit(0)
for i in range(100):
 if root.name=='bad-health' and i>5:sys.exit(5)
 if json.loads(journal.read_text())['state']=='healthy':sys.exit(0)
 time.sleep(.05)
sys.exit(6)
''');game.chmod(0o755)
   record=dict(schema=1,state='prepared',appId='tech.afonasev.star-racing.win-x64',channel='win-x64',track='production',previousVersion='0.3.5',targetVersion='0.3.6',previousEnvelope=old,targetEnvelope=new,root=str(folder),packages=str(packages),helper=str(helper),executable=str(game),parentPid=2147483647,parentStarted=0,gamePid=0,gameStarted=0,token='a'*32,arguments=['-noaudio','-muted'],recoveryAttempted=False,restored=False)
   if scenario=='interrupted-apply':record['state']='applying';(folder/'installed').write_text('B')
   if scenario=='interrupted-pending':record['state']='pending-health';record['gamePid']=2147483647;(folder/'installed').write_text('B')
   if scenario=='tampered-target':(packages/'racing-0.3.6-full.nupkg').write_bytes(b'C')
   if scenario=='tampered-previous':(packages/'racing-0.3.5-full.nupkg').write_bytes(b'C')
   journal.write_text(json.dumps(record))
   result=subprocess.run([dotnet,dll,journal]+(['foreign-token'] if scenario=='stale-token' else []),env=env,capture_output=True,text=True,timeout=90 if scenario=='false-success' else 30)
   final=json.loads(journal.read_text());applications=(folder/'applications').read_text().splitlines() if (folder/'applications').exists() else []
   if scenario=='stale-token':assert result.returncode==2 and final['state']=='prepared' and not applications,'stale initiator must not alter a newer preparation'
   elif scenario in ('healthy','health-exit','cleanup-error'):
    assert result.returncode==0 and final['state']=='healthy' and applications==['B'],(scenario,result.stdout,result.stderr,final)
    if scenario!='cleanup-error':assert not (packages/'racing-0.3.5-full.nupkg').exists(),'previous removed only after exact ack'
    else:assert (packages/'racing-0.3.5-full.nupkg').is_dir(),'cleanup failure must not revoke health'
   elif scenario=='tampered-previous':assert result.returncode==1 and final['state']=='recovery-required' and not applications,(scenario,final,applications)
   else:
    assert result.returncode==1 and final['state']=='recovery-required' and final['restored'] and applications[-1:] == ['A'],(scenario,result.stdout,result.stderr,final,applications)
    assert (packages/'racing-0.3.5-full.nupkg').exists(),'previous retained after failed health'
    repeat=subprocess.run([dotnet,dll,journal],env=env,capture_output=True,text=True,timeout=10)
    assert (folder/'applications').read_text().splitlines()==applications,'recovery must not loop'
   results.append(dict(scenario=scenario,exitCode=result.returncode,state=final['state'],restored=final.get('restored'),applications=applications,success=True))
  (output/'recovery-faults.json').write_text(json.dumps(dict(success=True,source='actual distribution/recovery/Program.cs with test-only trust-root compilation',unityOrInstalledAcceptance=False,scenarios=results),indent=2)+'\n')
  print('RECOVERY_FAULTS_OK',len(results))
if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('--dotnet',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args();run(a.dotnet.resolve(),a.output.resolve())

"""Exercise the real universal .app entry with a fake child, without Unity or network."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import plistlib
import shutil
import signal
import subprocess
import sys
import time
import uuid

p=argparse.ArgumentParser();p.add_argument('--runtime',required=True,type=Path);p.add_argument('--entry',required=True,type=Path);p.add_argument('--output',required=True,type=Path);p.add_argument('--recovery',action='store_true');a=p.parse_args()
if sys.platform!='darwin':p.error('macOS packaged entry probe')
root=a.output.resolve();root.mkdir(parents=True,exist_ok=False)
app=root/'Star Racing.app';content=app/'Contents/MacOS';content.mkdir(parents=True)
resources=app/'Contents/Resources';resources.mkdir()
shutil.copytree(a.runtime.resolve(),content/'launcher/osx-arm64')
shutil.copyfile(a.entry.resolve(),content/'StarRacingLauncher');(content/'StarRacingLauncher').chmod(0o755)
# The pinned macOS locator requires the installed updater alongside sq.version.
(content/'UpdateMac').write_text('#!/bin/sh\nexit 99\n');(content/'UpdateMac').chmod(0o755)
version='0.0.0-test.1'
(app/'Contents/Info.plist').write_bytes(plistlib.dumps(dict(CFBundleExecutable='StarRacingLauncher',CFBundleIdentifier='local.starracing.launcher-probe',CFBundleName='Star Racing')))
(resources/'sq.version').write_text('<package><metadata><id>tech.afonasev.star-racing.osx-universal</id><version>'+version+'</version><channel>osx-universal</channel><mainExe>StarRacingLauncher</mainExe></metadata></package>')
(content/'launcher.json').write_text(json.dumps(dict(Schema=1,Version=version,Track='test',Channel='osx-universal',Player='Star Racing')))
player=content/'Star Racing'
player.write_text('''#!/usr/bin/env python3
import os,sys,json,time
from pathlib import Path
root=Path(__file__).parents[3]
with open(root/'argv.json','w') as f:json.dump(sys.argv[1:],f)
payload=dict(Version=os.environ['STAR_RACING_LAUNCHER_VERSION'],Token=os.environ['STAR_RACING_LAUNCHER_TOKEN'],Pid=os.getpid(),Started=621355968000000000+int(time.time()*10000000),DataPath=str(root))
with open(os.environ['STAR_RACING_LAUNCHER_READY'],'w') as f:json.dump(payload,f)
time.sleep(.4)
sys.exit(23)
''');player.chmod(0o755)
subprocess.run(['/usr/bin/codesign','--force','--deep','--sign','-',str(app)],check=True,capture_output=True)
# The real entry is used, so locator/config/runtime selection participate in this check.
environment=os.environ.copy()
if a.recovery:
 token=uuid.uuid4().hex
 journal=root/'transaction.json'
 journal.write_text(json.dumps(dict(schema=1,state='pending-health',appId='tech.afonasev.star-racing.osx-universal',channel='osx-universal',track='test',previousVersion='0.0.0-test.0',targetVersion=version,root=str(app),packages=str(root),helper=str(content/'UpdateMac'),executable=str(player),arguments=[],token=token)))
 environment['STAR_RACING_RECOVERY_LAUNCHER']=str(journal)
 environment['STAR_RACING_RECOVERY_TOKEN']=token
with (root/'entry.log').open('w') as log:
 child=subprocess.Popen([str(content/'StarRacingLauncher'),'-noaudio','-muted','argument with spaces'],stdout=log,stderr=subprocess.STDOUT,start_new_session=True,env=environment)
 try:result=child.wait(timeout=15)
 except subprocess.TimeoutExpired:
  os.killpg(child.pid,signal.SIGTERM);child.wait();raise
assert result==23, (result,(root/'entry.log').read_text())
if a.recovery:
 handshake=json.loads((root/'transaction.json.child').read_text())
 assert handshake['token']==token and handshake['version']==version and handshake['launcherPid']==child.pid and handshake['pid']!=child.pid
assert json.loads((root/'argv.json').read_text())==['-noaudio','-muted','argument with spaces']
# Match the actual .NET LocalApplicationData route on macOS.
identity=hashlib.sha256(str(content).encode()).hexdigest().upper()
state=Path.home()/'.local/share/StarRacing/launcher/test'/identity
# .NET's macOS LocalApplicationData uses Library/Application Support.
if not state.exists():state=Path.home()/'Library/Application Support/StarRacing/launcher/test'/identity
working=json.loads((state/'working.json').read_text())
assert working['Version']==version and working['DataPath']==str(root)
(root/'receipt.json').write_text(json.dumps(dict(status='PASS',recovery=a.recovery,childExit=result,arguments=['-noaudio','-muted','argument with spaces'],realUnity=False,entry=str(content/'StarRacingLauncher'),working=working),indent=2)+'\n')
shutil.rmtree(state)
print('PACKAGED_LAUNCHER_PROBE_PASS',root/'receipt.json')

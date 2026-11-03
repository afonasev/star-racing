#!/usr/bin/env python3
"""Keep the Unity exclusive guard for the original Player and updater restart.

Invoke only THROUGH tools/unity.sh player. It does not launch or own foreign apps.
Close the app normally; a restarted Player is tracked by its exact absolute path.
"""
import argparse,os,signal,subprocess,time,plistlib
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--app',type=Path,required=True);p.add_argument('--log',type=Path,required=True);p.add_argument('--installer',type=Path);a=p.parse_args()
def entry(app):
    info=plistlib.loads((app/'Contents/Info.plist').read_bytes())
    return (app/'Contents/MacOS'/info['CFBundleExecutable']).resolve()
exe=entry(a.app) if a.app.exists() else None
if a.installer:
    if a.app.exists():p.error('Refusing to overwrite an existing app during installer QA')
    result=subprocess.run(['/usr/sbin/installer','-pkg',str(a.installer.resolve()),'-target','CurrentUserHomeDirectory'])
    print('QA_INSTALLER_EXIT',result.returncode,flush=True)
    if result.returncode:raise SystemExit(result.returncode)
    exe=entry(a.app)
else:
    if exe is None or not exe.is_file():p.error('Missing packaged Player')
    child=subprocess.Popen([str(exe),'-logFile',str(a.log.resolve()),'-noaudio','-muted'])
    print('QA_PLAYER_STARTED',child.pid,exe,flush=True)
    child.wait();print('QA_ORIGINAL_EXIT',child.returncode,flush=True)
# The updater may restart after the original process exits. Keep the host guard
# while it does so, then until that exact restarted executable exits normally.
deadline=time.monotonic()+30;observed=False;last=[]
while True:
    output=subprocess.check_output(['ps','-axo','pid=,command='],text=True)
    matches=[]
    for line in output.splitlines():
        fields=line.strip().split(None,1)
        if len(fields)==2 and any(fields[1].startswith(str(path)) for path in [exe,(a.app/'Contents/MacOS/Star Racing').resolve(),(a.app/'Contents/MacOS/launcher/osx-arm64/StarRacingLauncher').resolve(),(a.app/'Contents/MacOS/launcher/osx-x64/StarRacingLauncher').resolve()]):
            matches.append(int(fields[0]))
    if matches:
        observed=True
        if matches!=last:print('QA_RESTARTED_PLAYER',matches,flush=True);last=matches
        deadline=time.monotonic()+30
    elif time.monotonic()>deadline:break
    time.sleep(1)
print('QA_ALL_PLAYERS_EXITED',flush=True)

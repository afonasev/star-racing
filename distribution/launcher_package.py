"""Add an independent entry to an exact Player; no build, signing or publication at import."""
import json
import os
from pathlib import Path
import plistlib
import subprocess
import shutil
import tempfile
import zipfile
import xml.etree.ElementTree as ET


def install(dotnet, distribution, payload, channel, version, track, env):
    distribution, payload = Path(distribution), Path(payload)
    content = payload/'Contents/MacOS' if channel == 'osx-universal' else payload
    player = 'Star Racing' if channel == 'osx-universal' else 'Star Racing.exe'
    if channel == 'osx-universal':
        info_path = payload/'Contents/Info.plist'
        info = plistlib.loads(info_path.read_bytes())
        player = info['CFBundleExecutable']
    if player != ('Star Racing' if channel == 'osx-universal' else 'Star Racing.exe') or not (content/player).is_file():
        raise ValueError('Expected unchanged Star Racing Player entry')
    for runtime in (['win-x64'] if channel == 'win-x64' else ['osx-arm64', 'osx-x64']):
        if runtime == 'win-x64':
            # Never publish CoreCLR's mscorlib/System assemblies into Unity's
            # executable directory. Stage separately and copy only a complete bundle.
            with tempfile.TemporaryDirectory(prefix='star-racing-launcher-') as staging:
                target = Path(staging)
                subprocess.run([str(dotnet), 'publish', str(distribution/'launcher/Launcher.csproj'), '-c', 'Release',
                                '-r', runtime, '--self-contained', 'true', '-p:PublishSingleFile=true',
                                '-p:IncludeNativeLibrariesForSelfExtract=true', '-o', str(target)], check=True, env=env)
                entry = target/'StarRacingLauncher.exe'
                if not entry.is_file():
                    raise ValueError('Independent launcher missing for '+runtime)
                unexpected = [item.name for item in target.iterdir() if item.name not in ('StarRacingLauncher.exe', 'StarRacingLauncher.pdb')]
                if unexpected:
                    raise ValueError('Windows launcher must be self-contained in one EXE; external dependencies: '+', '.join(sorted(unexpected)))
                if any(item.is_file() and (item.name.lower() in ('mscorlib.dll', 'netstandard.dll', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll')
                       or item.name.lower().startswith('system.')) for item in content.iterdir()):
                    raise ValueError('Windows Player directory contains loose .NET runtime files; use a clean Player input')
                shutil.copy2(entry, content/entry.name)
        else:
            target = content/'launcher'/runtime
            subprocess.run([str(dotnet), 'publish', str(distribution/'launcher/Launcher.csproj'), '-c', 'Release',
                            '-r', runtime, '--self-contained', 'true', '-o', str(target)], check=True, env=env)
            if not (target/'StarRacingLauncher').is_file():
                raise ValueError('Independent launcher missing for '+runtime)
    (content/'launcher.json').write_text(json.dumps(dict(Schema=1, Version=version, Track=track, Channel=channel, Player=player))+'\n')
    if channel == 'osx-universal':
        subprocess.run(['clang', '-arch', 'arm64', '-arch', 'x86_64', '-mmacosx-version-min=12.0',
                        str(distribution/'launcher/macos_entry.c'), '-o', str(content/'StarRacingLauncher')], check=True)
        subprocess.run(['clang', '-arch', 'arm64', '-arch', 'x86_64', '-mmacosx-version-min=12.0', '-fobjc-arc',
                        '-framework', 'AppKit', str(distribution/'launcher/progress.m'), '-o', str(content/'StarRacingProgress')], check=True)
        info['CFBundleExecutable'] = 'StarRacingLauncher'
        info_path.write_bytes(plistlib.dumps(info))
    return 'StarRacingLauncher.exe' if channel == 'win-x64' else 'StarRacingLauncher'


def windows_shortcut_entry(portable):
    """Retain the historical root entry; its new stub dispatches to the launcher."""
    portable = Path(portable)
    with zipfile.ZipFile(portable) as archive:
        if 'current/launcher.json' not in archive.namelist():
            return # Historical Player-only layout remains supported.
        if 'StarRacingLauncher.exe' not in archive.namelist():
            # The pinned SDK root stub invokes Update.exe start; Update reads mainExe
            # from sq.version. PE branding strings are not executable dispatch data.
            metadata=ET.fromstring(archive.read('current/sq.version'))
            main=next((node.text for node in metadata.iter() if node.tag.rsplit('}',1)[-1]=='mainExe'),None)
            if main!='StarRacingLauncher.exe' or 'current/StarRacingLauncher.exe' not in archive.namelist() or 'Star Racing.exe' not in archive.namelist() or 'Update.exe' not in archive.namelist():
                raise ValueError('Windows root stub does not dispatch to the independent launcher')
            return
        launcher = archive.read('StarRacingLauncher.exe')
        if 'Star Racing.exe' in archive.namelist() and archive.read('Star Racing.exe') == launcher:
            return
        entries = [(item, archive.read(item)) for item in archive.infolist() if item.filename != 'Star Racing.exe']
    temporary = portable.with_suffix('.launcher.tmp')
    try:
        with zipfile.ZipFile(temporary, 'w', compression=zipfile.ZIP_DEFLATED) as archive:
            for item, data in entries: archive.writestr(item, data)
            archive.writestr('Star Racing.exe', launcher)
        os.replace(temporary, portable)
    finally:
        if temporary.exists(): temporary.unlink()

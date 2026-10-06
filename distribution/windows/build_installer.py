"""Compile the wizard from a pinned Velopack portable bundle using NSIS.

NSIS owns ARP/uninstall/shortcuts; .portable keeps Velopack from resetting them.
"""
import argparse, hashlib, json, subprocess, tempfile, zipfile
from pathlib import Path, PurePosixPath

REQUIRED = {'.portable', 'Star Racing.exe', 'Update.exe', 'current/sq.version',
            'current/Star Racing.exe', 'current/UnityPlayer.dll', 'current/velopack_libc.dll'}

def validate_portable(archive):
    names = set(archive.namelist())
    if not REQUIRED.issubset(names):
        raise ValueError('Incomplete Velopack Windows portable layout')
    for entry in archive.infolist():
        path = PurePosixPath(entry.filename)
        if path.is_absolute() or '..' in path.parts or '\\' in entry.filename or ':' in entry.filename:
            raise ValueError('Unsafe portable archive path')
        if (entry.external_attr >> 16) & 0o170000 == 0o120000:
            raise ValueError('Symlinks are not supported in Windows payload')
    if '.msi-installed' in names:
        raise ValueError('Do not impersonate an MSI installation')

def build(portable, output, makensis='makensis', version=None):
    portable, output = Path(portable).resolve(), Path(output).resolve()
    if output.exists():
        raise ValueError('Refusing to overwrite an immutable installer')
    output.parent.mkdir(parents=True, exist_ok=True)
    script = Path(__file__).with_name('installer.nsi')
    import re
    if not version or not re.fullmatch(r'\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?', version):
        raise ValueError('An explicit SemVer is required')
    icon = script.parent.parent/'icons/Star-Racing.ico'
    with tempfile.TemporaryDirectory(prefix='star-racing-installer-') as folder:
        with zipfile.ZipFile(portable) as archive:
            validate_portable(archive)
            archive.extractall(folder)
        subprocess.run([makensis, '-V2', '-DPAYLOAD='+folder, '-DOUTPUT='+str(output), '-DVERSION='+version, '-DICON='+str(icon), str(script)], check=True)
    if output.open('rb').read(2) != b'MZ':
        raise ValueError('Compiler did not produce a Windows executable')
    def digest(path):
        with path.open('rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest()
    return dict(schema=1, installer=str(output), installerSHA256=digest(output),
                portableSHA256=digest(portable), scriptSHA256=digest(script),
                defaults=dict(directory='ProgramFiles64/Star Racing', desktopShortcut=True, launchOnFinish=True, optionsPage='finish', desktopOwner='original user'),
                installOwner='NSIS/HKLM', updateOwner='Velopack portable', windowsAcceptance='pending')

if __name__ == '__main__':
    p=argparse.ArgumentParser();p.add_argument('--portable',required=True,type=Path);p.add_argument('--output',required=True,type=Path);p.add_argument('--makensis',default='makensis');p.add_argument('--version',required=True)
    a=p.parse_args();identity=build(a.portable,a.output,a.makensis,a.version)
    a.output.with_suffix('.identity.json').write_text(json.dumps(identity,indent=2)+'\n')
    print(json.dumps(identity,indent=2))

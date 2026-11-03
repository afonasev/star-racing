import importlib.util
import json
from pathlib import Path
import plistlib
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import launcher_package
from windows.build_installer import validate_portable, REQUIRED


class LauncherPackaging(unittest.TestCase):
    def test_windows_stub_keeps_historical_shortcut_target(self):
        with tempfile.TemporaryDirectory() as temp:
            portable = Path(temp)/'Portable.zip'
            with zipfile.ZipFile(portable, 'w') as archive:
                for name in REQUIRED: archive.writestr(name, b'old' if name == 'Star Racing.exe' else b'payload')
                archive.writestr('StarRacingLauncher.exe', b'new-launcher-stub')
                archive.writestr('current/launcher.json', '{}')
                archive.writestr('current/StarRacingLauncher.exe', b'launcher')
            launcher_package.windows_shortcut_entry(portable)
            with zipfile.ZipFile(portable) as archive:
                self.assertEqual(archive.read('Star Racing.exe'), b'new-launcher-stub')
                self.assertEqual(archive.read('current/Star Racing.exe'), b'payload')
                self.assertEqual(archive.namelist().count('Star Racing.exe'), 1)
                validate_portable(archive)
            before = portable.read_bytes()
            launcher_package.windows_shortcut_entry(portable)
            self.assertEqual(before, portable.read_bytes())

    def test_pinned_sdk_packtitle_stub_dispatches_to_main_exe(self):
        with tempfile.TemporaryDirectory() as temp:
            portable=Path(temp)/'Portable.zip'
            with zipfile.ZipFile(portable,'w') as archive:
                for name in REQUIRED-{'Star Racing.exe','current/sq.version'}:archive.writestr(name,b'payload')
                archive.writestr('Star Racing.exe',b'MZ pinned SDK generic updater stub')
                archive.writestr('current/sq.version','<package xmlns="sdk"><metadata><mainExe>StarRacingLauncher.exe</mainExe></metadata></package>')
                archive.writestr('current/launcher.json','{}')
                archive.writestr('current/StarRacingLauncher.exe',b'MZ launcher')
            original=portable.read_bytes()
            launcher_package.windows_shortcut_entry(portable)
            self.assertEqual(portable.read_bytes(),original)
            with zipfile.ZipFile(portable) as archive:validate_portable(archive)

    def test_historical_layout_is_not_rewritten(self):
        with tempfile.TemporaryDirectory() as temp:
            portable = Path(temp)/'Portable.zip'
            with zipfile.ZipFile(portable, 'w') as archive: archive.writestr('Star Racing.exe', b'original')
            before = portable.read_bytes()
            launcher_package.windows_shortcut_entry(portable)
            self.assertEqual(before, portable.read_bytes())

    def test_install_preserves_player_and_product_identity_for_each_platform(self):
        for channel in ['win-x64', 'osx-universal']:
            with self.subTest(channel=channel), tempfile.TemporaryDirectory() as temp:
                payload=Path(temp)/'payload'
                content=payload/'Contents/MacOS' if channel == 'osx-universal' else payload
                content.mkdir(parents=True)
                player=content/('Star Racing' if channel == 'osx-universal' else 'Star Racing.exe')
                player.write_bytes(b'actual player')
                if channel == 'osx-universal':
                    (payload/'Contents/Info.plist').write_bytes(plistlib.dumps(dict(CFBundleExecutable='Star Racing', CFBundleIdentifier='local.starracing.prototype', CFBundleName='Star Racing')))
                def run(command, **kwargs):
                    if command[1] == 'publish':
                        out=Path(command[command.index('-o')+1]);out.mkdir(parents=True,exist_ok=True)
                        (out/('StarRacingLauncher.exe' if channel == 'win-x64' else 'StarRacingLauncher')).touch()
                with patch.object(launcher_package.subprocess,'run',side_effect=run) as calls:
                    entry=launcher_package.install('/dotnet','/distribution',payload,channel,'1.2.3-test.4','test',{})
                    commands=[x.args[0] for x in calls.call_args_list]
                    self.assertEqual(sum(c[1]=='publish' for c in commands),1 if channel == 'win-x64' else 2)
                    self.assertEqual(player.read_bytes(),b'actual player')
                    config=json.loads((content/'launcher.json').read_text())
                    self.assertEqual(config['Track'],'test')
                    self.assertEqual(config['Player'],player.name)
                    if channel == 'osx-universal':
                        info=plistlib.loads((payload/'Contents/Info.plist').read_bytes())
                        self.assertEqual(info['CFBundleIdentifier'],'local.starracing.prototype')
                        self.assertEqual(info['CFBundleExecutable'],entry)
                        self.assertIn('x86_64',commands[-1]);self.assertIn('arm64',commands[-1])

    def test_missing_self_contained_launcher_fails_packaging(self):
        with tempfile.TemporaryDirectory() as temp:
            payload=Path(temp);(payload/'Star Racing.exe').touch()
            with patch.object(launcher_package.subprocess,'run'), self.assertRaisesRegex(ValueError,'launcher missing'):
                launcher_package.install('/dotnet','/distribution',payload,'win-x64','1.2.3','production',{})

if __name__=='__main__': unittest.main()

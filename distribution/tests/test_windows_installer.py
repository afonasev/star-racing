import importlib.util, io, unittest, zipfile
from pathlib import Path

spec=importlib.util.spec_from_file_location('windows_installer',Path(__file__).parents[1]/'windows/build_installer.py')
installer=importlib.util.module_from_spec(spec);spec.loader.exec_module(installer)

class PortableContract(unittest.TestCase):
    def archive(self,names):
        stream=io.BytesIO()
        with zipfile.ZipFile(stream,'w') as out:
            for name in names:out.writestr(name,b'test')
        return zipfile.ZipFile(stream)
    def test_complete_layout(self):
        with self.archive(installer.REQUIRED) as archive:installer.validate_portable(archive)
    def test_missing_portable_marker(self):
        with self.archive(installer.REQUIRED-{'.portable'}) as archive:
            with self.assertRaises(ValueError):installer.validate_portable(archive)
    def test_reject_msi_impersonation(self):
        with self.archive(installer.REQUIRED|{'.msi-installed'}) as archive:
            with self.assertRaises(ValueError):installer.validate_portable(archive)
    def test_archive_cannot_escape_install_root(self):
        for path in ['../foreign.exe','/foreign.exe','C:/foreign.exe','current\\..\\foreign.exe']:
            with self.subTest(path=path),self.archive(installer.REQUIRED|{path}) as archive:
                with self.assertRaises(ValueError):installer.validate_portable(archive)

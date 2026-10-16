import importlib.util, io, unittest, zipfile, tempfile, subprocess, shutil
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
    def test_missing_external_recovery_is_rejected(self):
        with self.archive(installer.REQUIRED-{'current/recovery/win-x64/StarRacingRecovery.exe'}) as archive:
            with self.assertRaises(ValueError):installer.validate_portable(archive)
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

class ScriptPreprocess(unittest.TestCase):
    @unittest.skipUnless(shutil.which('makensis'),'NSIS unavailable')
    def test_transaction_wizard_preprocess_without_installer_build(self):
        with tempfile.TemporaryDirectory(prefix='sr-nsis-preprocess-') as tmp:
            root=Path(tmp);payload=root/'payload';helper=payload/'current/recovery/win-x64/StarRacingRecovery.exe'
            helper.parent.mkdir(parents=True);helper.touch();icon=root/'icon.ico';icon.touch();output=root/'NOT-BUILT.exe'
            result=subprocess.run(['makensis','-INPUTCHARSET','UTF8','-OUTPUTCHARSET','UTF8','-SAFEPPO','-DVERSION=1.1.0','-DPAYLOAD='+str(payload),'-DICON='+str(icon),'-DOUTPUT='+str(output),str(Path(__file__).parents[1]/'windows/installer.nsi')],capture_output=True,text=True,env=installer.compiler_env())
            self.assertEqual(result.returncode,0,result.stderr)
            self.assertFalse(output.exists(),'preprocess must not build an installer')

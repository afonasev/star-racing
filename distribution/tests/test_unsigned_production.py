import importlib.util,json,tempfile,unittest,sys,zipfile
from pathlib import Path

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from metadata import finalize,sha
from package import prepare_installer

spec=importlib.util.spec_from_file_location('publish',Path(__file__).resolve().parents[1]/'publish.py')
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)

class UnsignedProductionContracts(unittest.TestCase):
 def test_unsigned_requires_explicit_production_opt_in(self):
  with tempfile.TemporaryDirectory() as tmp:
   root=Path(tmp);(root/'identity.json').write_text(json.dumps({'version':'1.0.0','channel':'win-x64','sequence':1,'files':[]}))
   (root/'unsigned.json').write_text(json.dumps({'unsignedProductionCatalog':True,'releaseTrack':'production'}))
   with self.assertRaises(ValueError):m.validate_identity(root)

class CompletedUnsignedRelease(unittest.TestCase):
 def test_deferred_windows_removes_stock_installer_without_mac_package(self):
  with tempfile.TemporaryDirectory() as tmp:
   root=Path(tmp);(root/'stock-Setup.exe').write_bytes(b'stock');(root/'game-Portable.zip').write_bytes(b'portable')
   prepare_installer(root,'win-x64','0.3.0',True)
   self.assertFalse((root/'stock-Setup.exe').exists());self.assertTrue((root/'game-Portable.zip').exists())
 def test_finalized_unsigned_installer_and_identity_for_both_platforms(self):
  for platform,suffix in [('win-x64','Windows-Setup.exe'),('osx-universal','macOS-Setup.pkg')]:
   with self.subTest(platform=platform),tempfile.TemporaryDirectory() as tmp:
    root=Path(tmp);package=root/'game-0.3.0-full.nupkg'
    with zipfile.ZipFile(package,'w') as z:
     for name in ['Star Racing.exe','UnityPlayer.dll','velopack_libc.dll']:z.writestr('lib/app/'+name,b'payload')
    installer=root/('Star-Racing-0.3.0-'+suffix);installer.write_bytes(b'installer');(root/'notes.md').write_text('notes')
    (root/'identity.json').write_text(json.dumps(dict(version='0.3.0',channel=platform,sequence=1,files=[])))
    descriptor=finalize(root,'production',unsigned_production=True)
    self.assertTrue(descriptor['unsignedProductionCatalog']);self.assertEqual(descriptor['installer']['sha256'],sha(installer))
    identity=m.validate_identity(root,allow_unsigned_production=True)
    self.assertIn(installer.name,[f['name'] for f in identity['files']])
    with self.assertRaises(ValueError):m.validate_identity(root)
    installer.write_bytes(b'corrupt')
    with self.assertRaises(ValueError):m.validate_identity(root,allow_unsigned_production=True)
 def test_unsigned_test_finalize_rejected(self):
  with tempfile.TemporaryDirectory() as tmp:
   root=Path(tmp);(root/'identity.json').write_text(json.dumps(dict(version='0.3.0-test.1',channel='osx-universal',sequence=1)))
   (root/'game-full.nupkg').write_bytes(b'package');(root/'Star-Racing-0.3.0-test.1-macOS-Setup.pkg').write_bytes(b'installer');(root/'notes.md').write_text('notes')
   with self.assertRaises(ValueError):finalize(root,'test',unsigned_production=True)

if __name__=='__main__':unittest.main()

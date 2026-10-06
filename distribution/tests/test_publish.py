import importlib.util,json,unittest,tempfile,hashlib,base64,subprocess,zipfile
from pathlib import Path
spec=importlib.util.spec_from_file_location('publish',Path(__file__).resolve().parents[1]/'publish.py');m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
class PublicationContracts(unittest.TestCase):
 def setUp(self):
  self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name)
  (self.root/'racing-Setup.exe').write_bytes(b'test')
  with zipfile.ZipFile(self.root/'racing-full.nupkg','w') as archive:
   for name in ['Star Racing.exe','UnityPlayer.dll','velopack_libc.dll']:archive.writestr('lib/app/'+name,b'test')
  self.key=self.root/'private.pem';self.public=self.root/'public.pem'
  subprocess.run(['openssl','genpkey','-algorithm','RSA','-pkeyopt','rsa_keygen_bits:2048','-out',str(self.key)],check=True,capture_output=True)
  self.public.write_bytes(subprocess.check_output(['openssl','pkey','-in',str(self.key),'-pubout']))
  package=(self.root/'racing-full.nupkg').read_bytes()
  payload=dict(schema=1,appId='tech.afonasev.star-racing.win-x64',version='0.2.1-test.1',channel='win-x64',sequence=1,fileName='racing-full.nupkg',size=len(package),sha256=hashlib.sha256(package).hexdigest(),url='https://racing.afonasev.tech/releases/win-x64/0.2.1-test.1/racing-full.nupkg')
  serialized=json.dumps(payload).encode();signature=subprocess.run(['openssl','dgst','-sha256','-sign',str(self.key)],input=serialized,capture_output=True,check=True).stdout
  (self.root/'signed.json').write_text(json.dumps(dict(keyId='star-racing-test-2026',payloadBase64=base64.b64encode(serialized).decode(),signatureBase64=base64.b64encode(signature).decode())))
  self.identity=dict(version='0.2.1-test.1',channel='win-x64',sequence=1,files=[dict(name=f.name,size=f.stat().st_size,sha256=hashlib.sha256(f.read_bytes()).hexdigest()) for f in self.root.iterdir() if f.suffix!='.pem'])
  self.save()
 def save(self):(self.root/'identity.json').write_text(json.dumps(self.identity))
 def tearDown(self):self.temp.cleanup()
 def test_valid(self):self.assertEqual(m.validate_identity(self.root,self.public)['sequence'],1)
 def test_changed_installer(self):
  (self.root/'racing-Setup.exe').write_bytes(b'new content')
  with self.assertRaises(ValueError):m.validate_identity(self.root,self.public)
 def test_traversal(self):
  self.identity['files'][0]['name']='../other';self.save()
  with self.assertRaises(ValueError):m.validate_identity(self.root,self.public)
 def test_both_platforms_required(self):
  with self.assertRaises(ValueError):m.downloads([self.identity])
 def test_metadata_version(self):
  self.identity['version']='0.2.1-test.2';self.save()
  with self.assertRaises(ValueError):m.validate_identity(self.root,self.public)
 def test_vendor_archive_filename_is_not_runtime_dll_name(self):
  package=self.root/'bad-full.nupkg'
  with zipfile.ZipFile(package,'w') as archive:
   for name in ['Star Racing.exe','UnityPlayer.dll','velopack_libc_win_x64_msvc.dll']:archive.writestr('lib/app/'+name,b'test')
  with self.assertRaises(ValueError):m.validate_windows_payload(package)

import importlib.util,sys,unittest
from pathlib import Path
from unittest.mock import patch
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import release
class ChannelContract(unittest.TestCase):
 def test_tls_verification_remains_enabled(self):
  import ssl
  context=release.tls_context()
  self.assertEqual(context.verify_mode,ssl.CERT_REQUIRED)
  self.assertTrue(context.check_hostname)
 def test_explicit_track(self):
  release.validate_track('test','0.2.2-test.1');release.validate_track('production','0.2.2')
  for track,version in [('production','0.2.2-test.1'),('test','0.2.2'),('test','../other')]:
   with self.assertRaises(ValueError):release.validate_track(track,version)
 def test_version_order(self):
  self.assertLess(release.version_key('0.2.2-test.2'),release.version_key('0.2.2-test.10'))
  self.assertLess(release.version_key('0.2.2-test.10'),release.version_key('0.2.2'))
  self.assertGreater(release.version_key('0.2.3-test.1'),release.version_key('0.2.2-test.10'))
 def test_no_silent_api_error_as_missing(self):
  import subprocess
  with patch.object(release.subprocess,'run',return_value=subprocess.CompletedProcess([],1,'','HTTP 403 rate limited')):
   with self.assertRaises(RuntimeError):release.optional_release('channel-test')
 def test_only_404_absent(self):
  import subprocess
  with patch.object(release.subprocess,'run',side_effect=[subprocess.CompletedProcess([],1,'','HTTP 404'),subprocess.CompletedProcess([],0,'[[]]','')]):
   self.assertIsNone(release.optional_release('channel-production'))

 def test_draft_is_existing_version(self):
  import subprocess,json
  draft={'tag_name':'v0.2.2-test.1','draft':True,'id':123}
  with patch.object(release.subprocess,'run',side_effect=[subprocess.CompletedProcess([],1,'','HTTP 404'),subprocess.CompletedProcess([],0,json.dumps([[draft]]),'')]):
   self.assertEqual(release.optional_release(draft['tag_name']),draft)
 def test_draft_lookup_failure_is_not_missing(self):
  import subprocess
  with patch.object(release.subprocess,'run',side_effect=[subprocess.CompletedProcess([],1,'','HTTP 404'),subprocess.CompletedProcess([],1,'','HTTP 403')]):
   with self.assertRaises(RuntimeError):release.optional_release('v0.2.2-test.1')

class PolicyCliContract(unittest.TestCase):
 def test_read_only_policy_needs_no_release_arguments_or_key(self):
  import subprocess,json,os
  env=dict(os.environ);env.pop('STAR_RACING_SIGNING_KEY',None)
  result=subprocess.run([sys.executable,str(Path(release.__file__)),'--show-policy'],capture_output=True,text=True,env=env,check=True)
  policy=json.loads(result.stdout)
  self.assertEqual(policy['repository'],'afonasev/star-racing')
  self.assertFalse(policy['routineVpsUpload'])
  self.assertEqual(set(policy['tracks']),{'test','production'})
 def test_missing_publication_arguments_still_rejected(self):
  import subprocess
  result=subprocess.run([sys.executable,str(Path(release.__file__))],capture_output=True,text=True)
  self.assertEqual(result.returncode,2)
  self.assertIn('--track, --version and --sequence are required',result.stderr)

class ReleaseSourcePreservation(unittest.TestCase):
 def test_unity_import_metadata_is_restored_after_both_builds(self):
  import tempfile
  with tempfile.TemporaryDirectory() as folder:
   root=Path(folder);assets=root/'unity-prototype/Assets/StarRacing';assets.mkdir(parents=True)
   project=root/'unity-prototype/ProjectSettings/ProjectSettings.asset';project.parent.mkdir(parents=True);project.write_text('project')
   generated=assets/'Generated/Prototype.unity';generated.parent.mkdir();generated.write_text('scene')
   meta=assets/'Resources/Track.png.meta';meta.parent.mkdir();meta.write_text('texture:\n  userData:\n')
   evidence=root/'evidence';evidence.mkdir()
   def fake_run(args,**kwargs):
    meta.write_text('texture:\n  userData: \n')
    Path(args[args.index('-logFile')+1]).write_text('PROTOTYPE_BUILD_OK')
   with patch.object(release,'ROOT',root),patch.object(release,'run',side_effect=fake_run) as build:
    release.build_players('0.2.2-test.3','test',evidence)
   self.assertEqual(meta.read_text(),'texture:\n  userData:\n')
   self.assertEqual(project.read_text(),'project')
   self.assertEqual(generated.read_text(),'scene')
   self.assertEqual(build.call_count,2)

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

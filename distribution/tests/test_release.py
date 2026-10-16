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
   plugin=root/'unity-prototype/Assets/Plugins.meta';plugin.write_text('plugin')
   quality=project.parent/'QualitySettings.asset';quality.write_text('quality')
   evidence=root/'evidence';evidence.mkdir()
   def fake_run(args,**kwargs):
    meta.write_text('texture:\n  userData: \n')
    plugin.write_text('imported plugin');quality.write_text('build quality')
    Path(args[args.index('-logFile')+1]).write_text('PROTOTYPE_BUILD_OK')
   with patch.object(release,'ROOT',root),patch.object(release,'run',side_effect=fake_run) as build:
    release.build_players('0.2.2-test.3','test',evidence)
   self.assertEqual(meta.read_text(),'texture:\n  userData:\n')
   self.assertEqual(project.read_text(),'project')
   self.assertEqual(plugin.read_text(),'plugin');self.assertEqual(quality.read_text(),'quality')
   self.assertEqual(generated.read_text(),'scene')
   self.assertEqual(build.call_count,2)

class ChangelogAdmission(unittest.TestCase):
 def test_invalid_review_never_creates_publisher_reservation(self):
  import json,tempfile
  with tempfile.TemporaryDirectory() as tmp:
   key=Path(tmp)/'key.pem';key.write_text('unused fixture key');review=Path(tmp)/'review.json';review.write_text('{}')
   source='a'*40;previous=dict(body=json.dumps(dict(sourceRevision='b'*40,unsignedProductionCatalog=True,platforms={'win-x64':dict(releaseTrack='production',version='0.3.5',sequence=1)})))
   args=['release.py','--track','production','--version','0.3.6','--sequence','2','--key',str(key),'--changelog-review',str(review)]
   with patch.object(sys,'argv',args),patch.object(release.subprocess,'check_output',side_effect=['',source]),patch.object(release,'api') as api,patch.object(release,'optional_release',side_effect=[None,previous]),patch.object(release,'generate_changelog',side_effect=ValueError('Unreviewed Git commits')):
    with self.assertRaises(ValueError):release.main()
    api.assert_called_once_with('commits/'+source)

class MenuSmokeContract(unittest.TestCase):
 def test_menu_smoke_is_silent_and_requires_its_own_receipt(self):
  import tempfile,json
  with tempfile.TemporaryDirectory() as tmp:
   root=Path(tmp);folder=root/'release';folder.mkdir();(folder/'candidate-Portable.zip').write_bytes(b'zip');evidence=root/'evidence';evidence.mkdir()
   commands=[]
   def fake_run(args,**kw):
    commands.append([str(a) for a in args])
    if '--menu-release-smoke-dir' in commands[-1]:
     proof=Path(commands[-1][commands[-1].index('--menu-release-smoke-dir')+1]);(proof/'success.json').write_text(json.dumps({'success':True,'scope':'menu','installed_queue_untouched':True}))
   with patch.object(release,'run',side_effect=fake_run):release.smoke_menu_macos(folder,evidence)
   args=commands[-1];self.assertIn('-noaudio',args);self.assertIn('-muted',args);self.assertNotIn('-batchmode',args);self.assertIn('--menu-release-smoke-dir',args);self.assertNotIn('--cloudline-proof-dir',args)
   self.assertEqual((evidence/'macos-menu-smoke/candidate.json').exists(),True)

 def test_menu_smoke_rejects_a_different_scope_receipt(self):
  import tempfile,json
  with tempfile.TemporaryDirectory() as tmp:
   root=Path(tmp);folder=root/'release';folder.mkdir();(folder/'candidate-Portable.zip').write_bytes(b'zip');evidence=root/'evidence';evidence.mkdir()
   def fake_run(args,**kw):
    args=[str(a) for a in args]
    if '--menu-release-smoke-dir' in args:
     proof=Path(args[args.index('--menu-release-smoke-dir')+1]);(proof/'success.json').write_text(json.dumps({'success':True,'scope':'race','natural_ai_finish':True}))
   with patch.object(release,'run',side_effect=fake_run),self.assertRaises(ValueError):release.smoke_menu_macos(folder,evidence)

class PreparedIdentityContract(unittest.TestCase):
 def test_resume_preserves_bytes_and_rejects_game_source_changes(self):
  import tempfile,json,hashlib,subprocess
  with tempfile.TemporaryDirectory() as tmp:
   root=Path(tmp)
   def git(*args):return subprocess.check_output(['git','-C',str(root),*args],text=True).strip()
   git('init','-q');git('config','user.name','fixture');git('config','user.email','fixture@example.invalid')
   installer=root/'distribution/windows/installer.nsi';installer.parent.mkdir(parents=True);installer.write_text('old')
   game=root/'unity-prototype/game.cs';game.parent.mkdir();game.write_text('game');git('add','.');git('commit','-qm','source');base=git('rev-parse','HEAD')
   for channel in ['osx-universal','win-x64']:
    folder=root/'output'/channel/'1.0.0';folder.mkdir(parents=True);(folder/'package').write_bytes(b'exact')
    (folder/'identity.json').write_text(json.dumps(dict(version='1.0.0',channel=channel,sequence=7,sourceStatus='',sourceRevision=base,files=[dict(name='package',size=5,sha256=hashlib.sha256(b'exact').hexdigest())])))
   installer.write_text('fixed');git('add','distribution');git('commit','-qm','installer tooling')
   with patch.object(release,'ROOT',root):
    self.assertEqual(release.verify_prepared(root/'output','1.0.0',7),base)
    package=root/'output/osx-universal/1.0.0/package';package.write_bytes(b'tampered')
    with self.assertRaises(ValueError):release.verify_prepared(root/'output','1.0.0',7)
    package.write_bytes(b'exact');game.write_text('different game');git('add','unity-prototype');git('commit','-qm','game changed')
    with self.assertRaises(ValueError):release.verify_prepared(root/'output','1.0.0',7)

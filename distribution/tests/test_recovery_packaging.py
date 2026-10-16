import hashlib,io,json,sys,tempfile,unittest
from pathlib import Path
from unittest.mock import patch
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import release,package
class Response(io.BytesIO):
 def geturl(self):return 'https://release-assets.githubusercontent.com/package'
class RecoveryPackaging(unittest.TestCase):
 def descriptor(self):return dict(schema=2,appId='tech.afonasev.star-racing.win-x64',channel='win-x64',releaseTrack='production',version='0.3.5',sequence=6,fileName='game-0.3.5-full.nupkg',size=3,sha256=hashlib.sha256(b'old').hexdigest(),url='https://github.com/afonasev/star-racing/releases/download/v0.3.5/game-0.3.5-full.nupkg',notes='old',unsignedProductionCatalog=True)
 def previous(self,old):return dict(body=json.dumps(dict(unsignedProductionCatalog=True,platforms={'win-x64':old})))
 def test_unsigned_baseline_signed_only_after_exact_download(self):
  with patch.object(release.urllib.request,'urlopen',return_value=Response(b'old')),patch.object(release,'sign',return_value={'signature':'fixture'}) as sign:
   result=release.recovery_baseline(self.previous(self.descriptor()),'win-x64','production',Path('/unused'))
   self.assertEqual(result,{'signature':'fixture'});self.assertNotIn('unsignedProductionCatalog',sign.call_args.args[0])
 def test_tampered_or_partial_baseline_not_signed(self):
  for data in [b'bad',b'ol',b'oldX']:
   with patch.object(release.urllib.request,'urlopen',return_value=Response(data)),patch.object(release,'sign') as sign:
    with self.assertRaises(ValueError):release.recovery_baseline(self.previous(self.descriptor()),'win-x64','production',None)
    sign.assert_not_called()
 def test_foreign_baseline_not_downloaded(self):
  old=self.descriptor();old['channel']='osx-universal'
  with patch.object(release.urllib.request,'urlopen') as download:
   with self.assertRaises(ValueError):release.recovery_baseline(self.previous(old),'win-x64','production',None)
   download.assert_not_called()
 def test_each_mac_architecture_and_windows_have_self_contained_agent(self):
  for channel,runtimes in [('win-x64',['win-x64']),('osx-universal',['osx-arm64','osx-x64'])]:
   with tempfile.TemporaryDirectory() as tmp:
    content=Path(tmp)
    for runtime in runtimes:
     folder=content/'recovery'/runtime;folder.mkdir(parents=True);(folder/('StarRacingRecovery.exe' if runtime=='win-x64' else 'StarRacingRecovery')).touch()
    with patch.object(package.subprocess,'run') as process:
     package.package_recovery(Path('/sdk/dotnet'),Path('/source/distribution'),content,channel,{})
     self.assertEqual(process.call_count,len(runtimes))
     for call,runtime in zip(process.call_args_list,runtimes):
      args=call.args[0];self.assertIn(runtime,args);self.assertIn('--self-contained',args);self.assertIn('-p:PublishSingleFile=true',args)

import importlib.util,json,tempfile,unittest
from pathlib import Path

spec=importlib.util.spec_from_file_location('publish',Path(__file__).resolve().parents[1]/'publish.py')
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)

class UnsignedProductionContracts(unittest.TestCase):
 def test_unsigned_requires_explicit_production_opt_in(self):
  with tempfile.TemporaryDirectory() as tmp:
   root=Path(tmp);(root/'identity.json').write_text(json.dumps({'version':'1.0.0','channel':'win-x64','sequence':1,'files':[]}))
   (root/'unsigned.json').write_text(json.dumps({'unsignedProductionCatalog':True,'releaseTrack':'production'}))
   with self.assertRaises(ValueError):m.validate_identity(root)

if __name__=='__main__':unittest.main()

import json,subprocess,sys,tempfile,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from changelog import generate
class ChangelogContracts(unittest.TestCase):
 def setUp(self):
  self.tmp=tempfile.TemporaryDirectory();self.repo=Path(self.tmp.name)
  self.git('init','-q');self.git('config','user.name','fixture');self.git('config','user.email','fixture@example.invalid')
  (self.repo/'data').write_text('a');self.git('add','data');self.git('commit','-qm','internal task identifier Q42');self.base=self.git('rev-parse','HEAD')
  (self.repo/'data').write_text('b');self.git('commit','-am','technical subject never copied','-q');self.head=self.git('rev-parse','HEAD')
  self.review=dict(schema=1,base=self.base,head=self.head,changes=[dict(commits=[self.head],category='fixed',ru='Настройки сохраняются после перезапуска.',en='Settings persist after restarting.')])
 def tearDown(self):self.tmp.cleanup()
 def git(self,*args):return subprocess.check_output(['git','-C',str(self.repo),*args],text=True).strip()
 def test_same_outcomes_both_languages_no_subjects_or_ids(self):
  text=generate(self.repo,self.base,self.head,self.review)
  self.assertIn('## Русский',text);self.assertIn('## English',text);self.assertIn('### Исправления',text);self.assertIn('### Fixed',text)
  self.assertNotIn('technical subject',text);self.assertNotIn(self.head,text);self.assertNotIn('### Новое',text)
 def test_no_missing_translation(self):
  self.review['changes'][0]['en']=''
  with self.assertRaises(ValueError):generate(self.repo,self.base,self.head,self.review)
 def test_no_unreviewed_or_duplicate_commits(self):
  for value in [[],[self.head,self.head],['a'*40]]:
   self.review['changes'][0]['commits']=value
   with self.assertRaises(ValueError):generate(self.repo,self.base,self.head,self.review)
 def test_exact_range_identity(self):
  self.review['head']=self.base
  with self.assertRaises(ValueError):generate(self.repo,self.base,self.head,self.review)
 def test_first_release_includes_all_git_history(self):
  self.review['base']=None;self.review['internal']=[dict(commits=[self.base],reason='Initial developer scaffolding')]
  self.assertIn('Settings persist',generate(self.repo,None,self.head,self.review))

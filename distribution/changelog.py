#!/usr/bin/env python3
"""Generate bilingual player notes from an exact, reviewed Git revision range.
Review groups commits into player outcomes; commit subjects/IDs never become release text.
"""
import argparse,json,subprocess
from pathlib import Path
CATEGORIES={'added':('Новое','Added'),'improved':('Улучшения','Improved'),'fixed':('Исправления','Fixed')}
def git(repo,*args):return subprocess.check_output(['git','-C',str(repo),*args],text=True).strip()
def commits(repo,base,head):
 head=git(repo,'rev-parse','--verify',head+'^{commit}')
 if base:
  base=git(repo,'rev-parse','--verify',base+'^{commit}')
  if subprocess.run(['git','-C',str(repo),'merge-base','--is-ancestor',base,head],capture_output=True).returncode:raise ValueError('Previous channel source is not an ancestor; review source mapping before release')
 return base,head,git(repo,'rev-list','--reverse',head if not base else base+'..'+head).splitlines()
def generate(repo,base,head,review):
 base,head,history=commits(repo,base,head)
 if review.get('schema')!=1 or review.get('base')!=base or review.get('head')!=head:raise ValueError('Changelog review must identify exact channel baseline and source revision')
 seen=[];groups={k:[] for k in CATEGORIES}
 for item in review.get('changes',[]):
  if item.get('category') not in groups or not item.get('commits'):raise ValueError('Each outcome needs a category and real Git commits')
  ru=item.get('ru','').strip();en=item.get('en','').strip()
  if not ru or not en or '\n' in ru or '\n' in en:raise ValueError('Each outcome needs reviewed single-paragraph RU and EN text')
  if any(sha in ru or sha in en for sha in history):raise ValueError('Commit identifiers are not player-facing notes')
  seen+=item['commits'];groups[item['category']].append((ru,en))
 for item in review.get('internal',[]):
  if not item.get('reason','').strip() or not item.get('commits'):raise ValueError('Internal changes need an explicit review reason')
  seen+=item['commits']
 if len(seen)!=len(set(seen)) or set(seen)!=set(history):raise ValueError('Review must cover every Git commit exactly once, including merges; no invented or unreviewed commits')
 if not any(groups.values()):raise ValueError('No player-facing change in the reviewed range')
 sections=[]
 for language,title in enumerate(['Русский','English']):
  lines=['## '+title]
  for category,names in CATEGORIES.items():
   if groups[category]:lines+=['','### '+names[language]]+['- '+text[language] for text in groups[category]]
  sections.append('\n'.join(lines))
 return '\n\n'.join(sections)+'\n'
def main():
 p=argparse.ArgumentParser();p.add_argument('--repo',type=Path,default=Path.cwd());p.add_argument('--base');p.add_argument('--head',default='HEAD');p.add_argument('--review',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
 a.output.write_text(generate(a.repo,a.base,a.head,json.loads(a.review.read_text())),encoding='utf-8')
if __name__=='__main__':main()

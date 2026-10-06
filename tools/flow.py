#!/usr/bin/env python3
"""Small OpenSpec lifecycle ledger. No daemon, network or destructive Git actions."""
import argparse
import contextlib
import datetime
import fcntl
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile

STAGES = {'draft','ready','implementing','verified','merged','deployed','published','finalizing','awaiting-acceptance','rework-required','accepted','archived','cancelled'}
EDGES = {'draft':{'ready'},'ready':{'implementing'},'implementing':{'verified'},'verified':{'merged','published'},'merged':{'deployed','finalizing'},'deployed':{'finalizing'},'published':{'finalizing'},'finalizing':{'awaiting-acceptance'},'awaiting-acceptance':{'accepted','rework-required'},'rework-required':{'implementing'},'accepted':{'archived'}}

def now():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()

def atomic(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, tmp = tempfile.mkstemp(dir=path.parent, prefix='.flow-tmp-')
    try:
        with os.fdopen(fd, 'w') as f:
            json.dump(value, f, ensure_ascii=False, indent=2); f.write('\n'); f.flush(); os.fsync(f.fileno())
        os.replace(tmp, path)
    finally:
        if os.path.exists(tmp): os.unlink(tmp)

class Flow:
    def __init__(self, root):
        self.root = Path(root).resolve()
        if not (self.root/'openspec/changes').is_dir():
            raise ValueError('Planning root must contain openspec/changes (initialize OpenSpec first)')

    @contextlib.contextmanager
    def locked(self):
        with (self.root/'.flow.lock').open('a') as f:
            fcntl.flock(f, fcntl.LOCK_EX)
            yield

    def records(self):
        result = {}
        for path in sorted((self.root/'openspec/changes').glob('*/delivery.json')) + sorted((self.root/'openspec/changes/archive').glob('*/delivery.json')):
            d = json.loads(path.read_text())
            if d['id'] in result: raise ValueError('Duplicate change ID: '+d['id'])
            result[d['id']] = (path,d)
        return result

    def get(self, cid):
        try: return self.records()[cid]
        except KeyError: raise ValueError('Unknown change: '+cid)

    def save(self, path, d, event, **data):
        d['history'].append({'at':now(),'event':event,**data})
        atomic(path,d)
        return d

    def init(self, cid, route='standard', kind='software', parent=None):
        if not re.fullmatch('[a-z0-9]+(?:-[a-z0-9]+)*',cid): raise ValueError('Invalid change ID')
        directory=self.root/'openspec/changes'/cid
        if not (directory/'.openspec.yaml').is_file(): raise ValueError('Scaffold with openspec new change first')
        if cid in self.records(): raise ValueError('Already initialized')
        if parent and self.get(parent)[1]['kind']!='initiative':
            raise ValueError('Parent must be an initiative')
        if route not in {'quick','standard','initiative'} or kind not in {'software','report','initiative'}: raise ValueError('Invalid route/kind')
        if (route=='initiative') != (kind=='initiative'): raise ValueError('Initiative route requires initiative kind')
        d={'version':1,'id':cid,'route':route,'kind':kind,'parent':parent,'stage':'draft','owner':None,'paused':False,'dependencies':[],'children':[],'questions':[],'evidence':{},'runs':[],'history':[]}
        self.save(directory/'delivery.json',d,'created')
        if parent:
            pp,pd=self.get(parent)
            if pd['kind']!='initiative': raise ValueError('Parent must be an initiative')
            pd['children'].append(cid); self.save(pp,pd,'child-added',child=cid)
        return d

    def blocks(self, d, target):
        errors=[]
        if d['paused']: errors.append('paused')
        for q in d['questions']:
            if q['status']!='resolved' and ('all' in q['blocks'] or target in q['blocks']): errors.append('question:'+q['id'])
        if target in {'ready','implementing'}:
            for edge in d['dependencies']:
                try: _,dep=self.get(edge['id'])
                except ValueError: errors.append('missing:'+edge['id']); continue
                threshold=edge['threshold']; stage=dep['stage']
                ok = stage in {'accepted','archived'} if threshold=='accepted' else stage in ({'deployed','finalizing','awaiting-acceptance','accepted','archived'} if dep['kind']=='software' else {'published','finalizing','awaiting-acceptance','accepted','archived'})
                if threshold=='delivered' and dep['kind']=='software' and dep['evidence'].get('scope', {}).get('delivery_mode')=='local':
                    ok = ok and bool(dep['evidence'].get('release') and dep['evidence'].get('smoke'))
                if not ok: errors.append('dependency:'+edge['id'])
        return errors

    def claim(self, cid, owner):
        p,d=self.get(cid)
        if d['stage'] in {'accepted','archived','cancelled','awaiting-acceptance'}: raise ValueError('Not claimable at this stage')
        if d['owner'] not in {None,owner}: raise ValueError('Already claimed by '+d['owner'])
        if self.blocks(d,'implementing'): raise ValueError('Blocked: '+str(self.blocks(d,'implementing')))
        d['owner']=owner; return self.save(p,d,'claimed',owner=owner)

    def release(self,cid,owner):
        p,d=self.get(cid)
        if d['owner']!=owner: raise ValueError('Not the owner')
        d['owner']=None; return self.save(p,d,'released',owner=owner)

    def question(self,cid,text,blocks):
        p,d=self.get(cid)
        if any(s not in STAGES|{'all'} for s in blocks): raise ValueError('Unknown blocking stage')
        qid='Q'+str(len(d['questions'])+1)
        d['questions'].append({'id':qid,'text':text,'blocks':blocks,'status':'open','answer':None,'source':None,'resolution':None})
        return self.save(p,d,'question-created',question=qid)

    def answer(self,cid,qid,answer,source,resolution=None):
        p,d=self.get(cid)
        q=next((q for q in d['questions'] if q['id']==qid),None)
        if q is None: raise ValueError('Unknown question')
        if not answer.strip() or not source.strip(): raise ValueError('Answer and human source required')
        q.update(answer=answer,source=source,status='resolved' if resolution else 'answered',resolution=resolution)
        return self.save(p,d,'question-answered',question=qid,source=source)

    def evidence(self,cid,key,value):
        p,d=self.get(cid)
        if key in {'acceptance','cancellation'}: raise ValueError('Use explicit human transition')
        if not value: raise ValueError('Empty evidence')
        d['evidence'][key]=value
        return self.save(p,d,'evidence',key=key)

    def dependency(self,cid,dep,threshold):
        p,d=self.get(cid); self.get(dep)
        if threshold not in {'delivered','accepted'}: raise ValueError('Invalid threshold')
        def reaches(node,target,seen):
            if node==target:return True
            if node in seen:return False
            seen.add(node); _,n=self.get(node)
            return any(reaches(x['id'],target,seen) for x in n['dependencies']) or any(reaches(x,target,seen) for x in n['children'])
        if reaches(dep,cid,set()): raise ValueError('Dependency cycle')
        if any(e['id']==dep for e in d['dependencies']): raise ValueError('Dependency already exists')
        d['dependencies'].append({'id':dep,'threshold':threshold}); return self.save(p,d,'dependency-added',dependency=dep)

    def delivery_profile(self, d):
        merge = d['evidence'].get('merge', {})
        if not isinstance(merge, dict): raise ValueError('Missing merge proof')
        profile_path = Path(merge.get('repo', '')) / 'workflow/project.json'
        if not profile_path.is_file(): raise ValueError('Delivery project profile missing')
        return json.loads(profile_path.read_text())

    def check_merge(self, d, feature_ancestry=False):
        e = d['evidence'].get('merge')
        if not isinstance(e, dict): raise ValueError('Missing merge proof')
        subprocess.run(['git','-C',e['repo'],'merge-base','--is-ancestor',e['commit'],e['main_ref']],check=True,capture_output=True)
        commit = d['evidence'].get('commit')
        feature = commit.get('commit') if isinstance(commit, dict) else commit
        if not feature: raise ValueError('Missing implementation commit')
        if feature_ancestry:
            subprocess.run(['git','-C',e['repo'],'merge-base','--is-ancestor',feature,e['commit']],check=True,capture_output=True)
        return feature

    def check_local_delivery(self, d):
        if d['kind'] != 'software' or d['evidence'].get('scope', {}).get('delivery_mode') != 'local':
            raise ValueError('Explicit local software delivery scope required')
        if self.delivery_profile(d).get('deploy_authorized') is not False:
            raise ValueError('Local delivery requires explicit no-deploy project profile')
        feature = self.check_merge(d, feature_ancestry=True)
        v = d['evidence'].get('verification', {})
        if v.get('success') is not True or v.get('commit') != feature or v.get('criticalFindings') != []:
            raise ValueError('Successful exact-commit verification without critical findings required')
        def failures(value):
            if isinstance(value, dict):
                return any((k in {'exit','exit_code'} and x != 0) or (k == 'success' and x is False) or failures(x) for k,x in value.items())
            return isinstance(value, list) and any(failures(x) for x in value)
        if failures(v): raise ValueError('Failed verification evidence')
        local = d['evidence'].get('local_delivery', {})
        if local.get('success') is not True or local.get('commit') != feature:
            raise ValueError('Exact local delivery proof required')
        for key in ('candidate', 'identity'):
            path = local.get(key)
            if not isinstance(path, str) or not Path(path).is_absolute() or not Path(path).exists():
                raise ValueError('Durable local delivery path missing: '+key)
        if not Path(local['identity']).is_file(): raise ValueError('Local identity must be a file')
        identity = json.loads(Path(local['identity']).read_text())
        if identity.get('commit') != feature: raise ValueError('Local candidate identity commit mismatch')

    def transition(self,cid,target,owner=None,decision=None,source=None):
        p,d=self.get(cid); old=d['stage']
        if target not in STAGES: raise ValueError('Unknown stage')
        if target=='cancelled':
            if old in {'archived','cancelled'}: raise ValueError('Terminal stage')
            if not decision or not source: raise ValueError('Explicit cancellation decision/source required')
        elif target not in EDGES.get(old,set()): raise ValueError('Invalid transition: '+old+' → '+target)
        if d['owner'] and d['owner']!=owner: raise ValueError('Change belongs to another session')
        if target=='implementing' and not d['owner']: raise ValueError('Claim first')
        errors=self.blocks(d,target)
        if errors and target!='cancelled': raise ValueError('Blocked: '+str(errors))
        needed={'ready':['scope'],'verified':['verification'],'merged':['merge'],'deployed':['release','smoke'],'published':['publication'],'finalizing':[],'awaiting-acceptance':['spec_sync','cleanup','acceptance_guide'],'archived':['archive']}.get(target,[])
        for key in needed:
            if not d['evidence'].get(key): raise ValueError('Missing evidence: '+key)
        if target=='verified' and d['kind']=='software' and not d['evidence'].get('commit'): raise ValueError('Missing implementation commit')
        if target in {'merged','deployed'} and d['kind']!='software': raise ValueError('Non-software uses publication')
        if target=='published' and d['kind']=='software': raise ValueError('Software requires merge/deploy')
        if target=='merged': self.check_merge(d)
        if target=='deployed' and self.delivery_profile(d).get('deploy_authorized') is not True:
            raise ValueError('Deployment not authorized by project profile')
        if old=='merged' and target=='finalizing':
            self.check_local_delivery(d)
            d['history'].append({'at':now(),'event':'local-delivery','commit':d['evidence']['local_delivery']['commit']})
        if target=='awaiting-acceptance':
            if d['kind']=='software' and d['evidence'].get('scope', {}).get('delivery_mode')=='local':
                self.check_local_delivery(d)
                if d['evidence'].get('cleanup', {}).get('resourcesComplete') is not True:
                    raise ValueError('Owned cleanup incomplete')
            guide=d['evidence']['acceptance_guide']
            if not (p.parent/guide).is_file(): raise ValueError('Acceptance guide missing')
            if d['kind']=='initiative':
                for child in d['children']:
                    _,c=self.get(child)
                    if c['stage'] not in {'awaiting-acceptance','accepted','archived'}: raise ValueError('Child not finalized: '+child)
        if target=='accepted':
            if not decision or not source: raise ValueError('Explicit human decision/source required')
            if any(q['status']!='resolved' for q in d['questions']): raise ValueError('Unresolved human questions')
            for child in d['children']:
                if self.get(child)[1]['stage'] not in {'accepted','archived'}: raise ValueError('Child not accepted: '+child)
            d['evidence']['acceptance']={'decision':decision,'source':source,'at':now()}
        if target=='rework-required':
            if not decision or not source: raise ValueError('Feedback and source required')
            d['history'].append({'event':'previous-delivery','at':now(),'evidence':d['evidence']})
            d['evidence']={k:v for k,v in d['evidence'].items() if k=='scope'}
        if target=='archived' and p.parent.parent.name!='archive': raise ValueError('Archive through OpenSpec first')
        d['stage']=target
        if target in {'awaiting-acceptance','accepted','archived','cancelled'}: d['owner']=None
        return self.save(p,d,'transition',previous=old,stage=target,decision=decision,source=source)

    def inbox(self):
        result=[]
        for _,d in self.records().values():
            qs=[q for q in d['questions'] if q['status']!='resolved']
            stage=d['stage']
            result.append({'id':d['id'],'stage':stage,'owner':d['owner'],'parent':d['parent'],'paused':d['paused'],'questions':qs,'ready':stage in {'ready','rework-required'} and not d['owner'] and not self.blocks(d,'implementing'),'acceptance':stage=='awaiting-acceptance','finalization_debt':stage in {'verified','merged','deployed','published','finalizing','accepted'},'blocks':self.blocks(d,'implementing')})
        return result

    def lease(self,action,name,owner,reason=None):
        if name == 'unity-runtime' and action != 'release':
            raise ValueError('Unity runtime leases were replaced: use ~/.local/bin/unity-run --shared|--exclusive -- COMMAND; no coordinator ACK required')
        if not re.fullmatch('[a-z][a-z0-9-]*',name): raise ValueError('Invalid lease name')
        p=self.root/'.flow-leases.json'; data=json.loads(p.read_text()) if p.exists() else {}
        current=data.get(name)
        if action=='acquire':
            if current and current['owner']!=owner: raise ValueError('Lease held by '+current['owner'])
            data[name]={'owner':owner,'at':now()}
        elif action=='release':
            if not current or current['owner']!=owner: raise ValueError('Not lease owner')
            del data[name]
        else:
            if not reason: raise ValueError('Recovery requires inspected-state reason')
            log=self.root/'openspec/lease-recovery.jsonl'
            with log.open('a') as f: f.write(json.dumps({'at':now(),'lease':name,'previous':current,'owner':owner,'reason':reason})+'\n')
            data[name]={'owner':owner,'at':now()}
        atomic(p,data); return data

def main():
    p=argparse.ArgumentParser(description=__doc__); p.add_argument('--root',required=True)
    sub=p.add_subparsers(dest='cmd',required=True)
    sub.add_parser('inbox')
    a=sub.add_parser('show'); a.add_argument('id')
    a=sub.add_parser('init'); a.add_argument('id'); a.add_argument('--route',default='standard'); a.add_argument('--kind',default='software'); a.add_argument('--parent')
    for cmd in ['claim','release']:
        a=sub.add_parser(cmd); a.add_argument('id'); a.add_argument('--owner',required=True)
    a=sub.add_parser('question'); a.add_argument('id'); a.add_argument('text'); a.add_argument('--blocks',nargs='*',default=[])
    a=sub.add_parser('answer'); a.add_argument('id'); a.add_argument('question'); a.add_argument('--answer',required=True); a.add_argument('--source',required=True); a.add_argument('--resolution')
    a=sub.add_parser('evidence'); a.add_argument('id'); a.add_argument('key'); a.add_argument('--json-file',required=True)
    a=sub.add_parser('transition'); a.add_argument('id'); a.add_argument('stage'); a.add_argument('--owner'); a.add_argument('--decision'); a.add_argument('--source')
    a=sub.add_parser('dependency'); a.add_argument('id'); a.add_argument('dependency'); a.add_argument('--threshold',choices=['delivered','accepted'],required=True)
    a=sub.add_parser('pause'); a.add_argument('id'); a.add_argument('--reason',required=True)
    a=sub.add_parser('resume'); a.add_argument('id'); a.add_argument('--reason',required=True)
    a=sub.add_parser('lease'); a.add_argument('action',choices=['acquire','release','recover']); a.add_argument('name'); a.add_argument('--owner',required=True); a.add_argument('--reason')
    a=sub.add_parser('usage'); a.add_argument('id'); a.add_argument('--json-file',required=True)
    args=p.parse_args(); f=Flow(args.root)
    try:
        with f.locked():
            c=args.cmd
            if c=='inbox': out=f.inbox()
            elif c=='show': out=f.get(args.id)[1]
            elif c=='init':out=f.init(args.id,args.route,args.kind,args.parent)
            elif c in {'claim','release'}:out=getattr(f,c)(args.id,args.owner)
            elif c=='question':out=f.question(args.id,args.text,args.blocks)
            elif c=='answer':out=f.answer(args.id,args.question,args.answer,args.source,args.resolution)
            elif c=='evidence':out=f.evidence(args.id,args.key,json.loads(Path(args.json_file).read_text()))
            elif c=='transition':out=f.transition(args.id,args.stage,args.owner,args.decision,args.source)
            elif c=='dependency':out=f.dependency(args.id,args.dependency,args.threshold)
            elif c=='lease':out=f.lease(args.action,args.name,args.owner,args.reason)
            elif c=='usage':
                path,d=f.get(args.id); d['runs'].append(json.loads(Path(args.json_file).read_text()));out=f.save(path,d,'usage-recorded')
            else:
                path,d=f.get(args.id);d['paused']=c=='pause';out=f.save(path,d,c,reason=args.reason)
        print(json.dumps(out,ensure_ascii=False,indent=2))
    except (ValueError,KeyError,TypeError,subprocess.CalledProcessError) as e:
        p.exit(2,str(e)+'\n')
if __name__=='__main__':main()

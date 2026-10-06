#!/usr/bin/env python3
"""Fail-closed QA plan and complete Editor suite receipt. Native acceptance stays explicit."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import time
import uuid

REPO = Path(__file__).resolve().parent.parent
EDITOR_CHECKS = {
    'unity-prototype/Assets/StarRacing/Editor/PrototypeChecks.cs',
    'unity-prototype/Assets/StarRacing/Editor/RecoveryGhostChecks.cs',
    'unity-prototype/Assets/StarRacing/Editor/RosterFixtureEquivalenceChecks.cs',
}
TOOLING = {'tools/test_qa.py', '.agents/references/qa-scope.md'}
GATES = {
    'documentation': ['review-links', 'openspec-strict'],
    'tooling': ['tooling-regressions', 'openspec-strict'],
    'editor-checks': ['tooling-regressions', 'openspec-strict', 'unity-compile-and-all-checks', 'fixture-equivalence'],
    'full': ['tooling-regressions', 'openspec-strict', 'unity-compile-and-all-checks', 'fixture-equivalence', 'native-build', 'affected-player-playtest'],
}
BROAD = ['menu-countdown-natural-AI-finish-Results-menu', 'repeat-pause', 'both-themes', 'recovery']


def scope_for(paths, profile_safe=False):
    scopes = []
    for path in paths:
        if path == 'tools/qa.py':
            scopes.append('editor-checks')
        elif path == 'workflow/project.json' and profile_safe:
            scopes.append('tooling')
        elif path in TOOLING:
            scopes.append('tooling')
        elif path in EDITOR_CHECKS or path.removesuffix('.meta') in EDITOR_CHECKS:
            scopes.append('editor-checks')
        elif path.startswith(('docs/', 'openspec/')) and path.endswith('.md'):
            scopes.append('documentation')
        else:
            return 'full'
    return max(scopes, key=lambda s: list(GATES).index(s)) if scopes else 'full'


def git(*args):
    return subprocess.check_output(['git', *args], cwd=REPO).decode().strip()


def source_fingerprint():
    paths = set(git('ls-files').splitlines()) | set(git('ls-files', '--others', '--exclude-standard').splitlines())
    digest = hashlib.sha256()
    for name in sorted(paths):
        # Prepare regenerates this owned output; it is not an Editor suite input.
        if name == 'unity-prototype/Assets/StarRacing/Generated/Prototype.unity':
            continue
        path = REPO/name
        digest.update(name.encode()+b'\0')
        digest.update(path.read_bytes() if path.is_file() else b'<missing>')
    return digest.hexdigest()


def local_profile_diff(before, after):
    # Only QA metadata additions are local. Build/deploy/runtime authorization/config is full.
    def strip(value):
        value = json.loads(json.dumps(value))
        for key in ('qa_plan', 'editor_checks'):
            value.get('commands', {}).pop(key, None)
        for key in ('tooling', 'editor-checks', 'local-ui'):
            value.get('check_scopes', {}).pop(key, None)
        return value
    return strip(before) == strip(after)


def plan(base):
    baseline = git('rev-parse', '--verify', base + '^{commit}')
    # Rename detection disabled: old and new paths must BOTH satisfy the allowlist.
    paths = set(filter(None, git('diff', '--no-renames', '--name-only', baseline).splitlines()))
    paths.update(filter(None, git('ls-files', '--others', '--exclude-standard').splitlines()))
    profile = json.loads((REPO/'workflow/project.json').read_text())
    # Public source snapshots omit private planning profiles. Missing historical
    # policy cannot prove a local scope: select the complete gate.
    historical = subprocess.run(['git','show',baseline+':workflow/project.json'],cwd=REPO,capture_output=True,text=True)
    old_profile = json.loads(historical.stdout) if historical.returncode == 0 else None
    scope = scope_for(sorted(paths), profile_safe=old_profile is not None and local_profile_diff(old_profile, profile)) if old_profile is not None else 'full'
    return {'base': baseline, 'commit': git('rev-parse', 'HEAD'), 'paths': sorted(paths),
            'scope': scope, 'required': GATES[scope], 'broad_integration_if_affected': BROAD,
            'human_acceptance': 'pending', 'deploy_authorized': profile.get('deploy_authorized')}


def validate_receipt(log, token, profile):
    starts = re.findall(r'^PROTOTYPE_QA_BEGIN (\{.*\})$', log, re.M)
    ends = re.findall(r'^PROTOTYPE_QA_END (\{.*\})$', log, re.M)
    if len(starts) != 1 or len(ends) != 1:
        raise ValueError('Missing, duplicated or foreign complete suite receipt')
    begin, end = json.loads(starts[0]), json.loads(ends[0])
    if begin['token'] != token or end['token'] != token or end['balanceHash'] != profile:
        raise ValueError('Foreign token/profile')
    if (end['assertions'], end['rosterCases'], end['ghostAssertions']) != (450, 6, 25):
        raise ValueError('Missing/skipped suite assertions or roster cases')
    if log.find(starts[0]) > log.find(ends[0]):
        raise ValueError('Receipt order')
    if re.search(r'error CS\d+|Exception:|PROTOTYPE_QA_FAILED', log):
        raise ValueError('Compilation or check failure')
    return end


def validate_equivalence(log, token):
    markers = re.findall(r'^ROSTER_FIXTURE_EQUIVALENCE_OK cases=(\d+) token=(\S+)$', log, re.M)
    if markers != [('6', token)]:
        raise ValueError('Missing/foreign/skipped equivalence cases')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['plan', 'checks'])
    parser.add_argument('--base', required=True, help='Explicit reviewed baseline, including its full diff')
    parser.add_argument('--output', type=Path, help='New owned evidence directory (checks)')
    args = parser.parse_args()
    selection = plan(args.base)
    if args.action == 'plan':
        print(json.dumps(selection, indent=2)); return
    if not args.output:
        parser.error('checks requires --output')
    out = args.output.resolve(); out.mkdir(parents=True, exist_ok=False)
    profile = hashlib.sha256((REPO/'unity-prototype/Assets/StarRacing/Resources/balance-config.json').read_bytes()).hexdigest()
    token = str(uuid.uuid4()); env = dict(os.environ, STAR_RACING_QA_TOKEN=token)
    log = out/'editor.log'
    command = [str(REPO/'tools/unity.sh'), 'shared', '-batchmode', '-nographics', '-disableManagedDebugger', '-quit',
               '-executeMethod', 'StarRacingPrototype.PrototypeChecks.RunWithFixtureEquivalence', '-logFile', str(log)]
    source = source_fingerprint()
    started = time.time()
    result = subprocess.run(command, cwd=REPO, env=env)
    receipt = {'plan': selection, 'command': command, 'exit': result.returncode,
               'wrapper_wall_seconds': time.time()-started, 'token': token, 'profile': profile,
               'source_fingerprint': source, 'success': False, 'remaining': selection['required'], 'human_acceptance': False}
    try:
        if source_fingerprint() != source:
            raise ValueError('Source changed during checks')
        if result.returncode:
            raise ValueError('Unity wrapper failed')
        receipt['suite'] = validate_receipt(log.read_text(), token, profile)
        validate_equivalence(log.read_text(), token)
        receipt['editor_checks_success'] = True
        receipt['remaining'] = [g for g in selection['required'] if g not in {'unity-compile-and-all-checks', 'fixture-equivalence'}]
    except (ValueError, KeyError, OSError) as error:
        receipt['error'] = str(error)
    (out/'receipt.json').write_text(json.dumps(receipt, indent=2)+'\n')
    print(json.dumps(receipt, indent=2))
    # This command ONLY certifies the complete Editor suite; plan's remaining gates still apply.
    if not receipt.get('editor_checks_success'):
        raise SystemExit(1)


if __name__ == '__main__':
    main()

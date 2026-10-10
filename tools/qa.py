#!/usr/bin/env python3
"""Reviewed UI or explicitly authorized complete Editor QA receipt. Native acceptance stays explicit."""
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
TOOLING = {'tools/test_qa.py', 'tools/test_local_workflow.py', 'tools/test_ui_route.py', 'tools/qa.py', '.agents/references/qa-scope.md'}
# A candidate UI route still requires review of behavior and dependencies.
UI_FILES = {f'unity-prototype/Assets/StarRacing/Runtime/{name}.cs'
            for name in ('RaceHud', 'RaceHudLayout', 'RaceMenu', 'CloudlineSkin')}
UI_FILES.add('unity-prototype/Assets/StarRacing/Editor/RaceHudChecks.cs')
GATES = {
    'documentation': ['review-links', 'review-changed-planning-if-any'],
    'tooling': ['tooling-regressions', 'review-changed-planning-if-any'],
    'local-ui': ['tooling-regressions', 'openspec-strict', 'unity-compile-and-ui-checks', 'affected-editor-playmode'],
    'review-required': ['review-behavior-and-dependencies', 'select-affected-checks'],
    'full': ['tooling-regressions', 'openspec-strict', 'unity-compile-and-all-checks', 'fixture-equivalence'],
}
BROAD = ['menu-countdown-natural-AI-finish-Results-menu', 'repeat-pause', 'both-themes', 'recovery']


def scope_for(paths, profile_safe=False):
    scopes = []
    for path in paths:
        if path == 'workflow/project.json' and profile_safe:
            scopes.append('tooling')
        elif path in TOOLING:
            scopes.append('tooling')
        elif path.removesuffix('.meta') in UI_FILES:
            scopes.append('local-ui')
        elif path == 'AGENTS.md' or (path.startswith('.agents/references/') and path.endswith('.md')):
            scopes.append('documentation')
        elif path in EDITOR_CHECKS or path.removesuffix('.meta') in EDITOR_CHECKS:
            scopes.append('review-required')
        elif path.startswith(('docs/', 'openspec/')) and path.endswith('.md'):
            scopes.append('documentation')
        else:
            return 'review-required'
    return max(scopes, key=lambda s: list(GATES).index(s)) if scopes else 'review-required'


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
    # Only these QA metadata fields are local. Other profile changes require dependency review.
    def strip(value):
        value = json.loads(json.dumps(value))
        for key in ('qa_plan', 'editor_checks'):
            value.get('commands', {}).pop(key, None)
        for key in ('tooling', 'editor-checks', 'local-ui'):
            value.get('check_scopes', {}).pop(key, None)
        return value
    return strip(before) == strip(after)


def plan(base, requested_scope="auto", reason=None):
    baseline = git('rev-parse', '--verify', base + '^{commit}')
    # Rename detection disabled: old and new paths must BOTH satisfy the allowlist.
    paths = set(filter(None, git('diff', '--no-renames', '--name-only', baseline).splitlines()))
    paths.update(filter(None, git('ls-files', '--others', '--exclude-standard').splitlines()))
    profile = json.loads((REPO/'workflow/project.json').read_text())
    old_profile = json.loads(git('show', baseline+':workflow/project.json'))
    scope = scope_for(sorted(paths), profile_safe=local_profile_diff(old_profile, profile))
    if requested_scope == 'local-ui' and scope not in {'documentation', 'tooling', 'local-ui'}:
        raise ValueError('UI scope cannot hide shared runtime/build/check changes')
    if requested_scope != 'auto':
        scope = requested_scope
    return {'base': baseline, 'commit': git('rev-parse', 'HEAD'), 'paths': sorted(paths),
            'scope': scope, 'scope_reason': reason, 'required': GATES[scope], 'broad_integration_if_affected': BROAD if scope in {'full', 'review-required'} else [],
            'full_test_policy': 'Separate explicit human confirmation, including production; never automatic',
            'human_acceptance': 'pending', 'deploy_authorized': profile.get('deploy_authorized'),
            'deferred_player_gates': ['native-build', 'affected-player-playtest'] if scope == 'full' else [],
            'player_gate_policy': 'Explicit request or justified minimal QA build; no automatic build/deploy'}


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


def validate_ui_receipt(log, token):
    markers = re.findall(r'^RACE_HUD_CHECKS_OK assertions=(\d+) token=(\S+)$', log, re.M)
    if markers != [('221', token)] or re.search(r'error CS\d+|Exception:|HUD_CHECK\s', log):
        raise ValueError('Missing/foreign/incomplete UI receipt or compilation failure')
    return {'assertions': 221, 'token': token, 'scope': 'local-ui', 'match_simulations': False}


def check_method(scope):
    if scope == 'local-ui':
        return 'StarRacingPrototype.RaceHudChecks.Run'
    if scope == 'full':
        return 'StarRacingPrototype.PrototypeChecks.RunWithFixtureEquivalence'
    raise ValueError('Review dependencies and select affected checks; no automatic full suite')


def validate_equivalence(log, token):
    markers = re.findall(r'^ROSTER_FIXTURE_EQUIVALENCE_OK cases=(\d+) token=(\S+)$', log, re.M)
    if markers != [('6', token)]:
        raise ValueError('Missing/foreign/skipped equivalence cases')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['plan', 'checks'])
    parser.add_argument('--scope', choices=['auto', 'local-ui', 'full'], default='auto')
    parser.add_argument('--confirm-full', action='store_true',
                        help='Record separate explicit human authorization for this full run')
    parser.add_argument('--reason', help='Reviewed behavior/dependency reason for the chosen scope')
    parser.add_argument('--base', required=True, help='Explicit reviewed baseline, including its full diff')
    parser.add_argument('--output', type=Path, help='New owned evidence directory (checks)')
    args = parser.parse_args()
    try:
        selection = plan(args.base, args.scope, args.reason)
    except ValueError as error:
        parser.error(str(error))
    if args.action == 'plan':
        print(json.dumps(selection, indent=2)); return
    if selection['scope'] in {'documentation', 'tooling'}:
        parser.error('Selected scope uses diff/tooling checks; no Unity suite is required')
    if selection['scope'] == 'review-required':
        parser.error('Review changed behavior and use affected Editor methods; full needs separate human confirmation')
    if selection['scope'] == 'full' and not args.confirm_full:
        parser.error('Full tests require separate human confirmation; then pass --confirm-full')
    if not args.reason:
        parser.error('checks requires --reason: record affected behavior; do not auto-escalate to full')
    if not args.output:
        parser.error('checks requires --output')
    out = args.output.resolve(); out.mkdir(parents=True, exist_ok=False)
    profile = hashlib.sha256((REPO/'unity-prototype/Assets/StarRacing/Resources/balance-config.json').read_bytes()).hexdigest()
    token = str(uuid.uuid4()); env = dict(os.environ, STAR_RACING_QA_TOKEN=token)
    if selection['scope'] == 'full':
        env['STAR_RACING_CONFIRM_FULL_TESTS'] = 'yes'
    log = out/'editor.log'
    command = [str(REPO/'tools/unity.sh'), 'shared', '-batchmode', '-nographics', '-quit',
               '-executeMethod', check_method(selection['scope']), '-logFile', str(log)]
    # Unity may serialize these generated/import settings during an otherwise
    # read-only QA run. Keep their pre-run bytes so the receipt can reject real
    # source drift while restoring only the known editor collateral.
    restored_paths = [REPO/'unity-prototype/Assets/StarRacing/Generated/Prototype.unity',
                      REPO/'unity-prototype/Assets/StarRacing/Resources/Vehicle/Cloudline.fbx.meta',
                      REPO/'unity-prototype/ProjectSettings/ProjectSettings.asset']
    original = {path: path.read_bytes() for path in restored_paths if path.is_file()}
    source = source_fingerprint()
    started = time.time()
    try:
        result = subprocess.run(command, cwd=REPO, env=env)
    finally:
        for path, content in original.items():
            if path.exists() and path.read_bytes() != content:
                path.write_bytes(content)
    receipt = {'plan': selection, 'command': command, 'exit': result.returncode,
               'wrapper_wall_seconds': time.time()-started, 'token': token, 'profile': profile,
               'source_fingerprint': source, 'success': False, 'remaining': selection['required'], 'human_acceptance': False}
    try:
        if source_fingerprint() != source:
            raise ValueError('Source changed during checks')
        if result.returncode:
            raise ValueError('Unity wrapper failed')
        if selection['scope'] == 'local-ui':
            receipt['suite'] = validate_ui_receipt(log.read_text(), token)
            receipt['ui_checks_success'] = True
            completed = {'unity-compile-and-ui-checks'}
        else:
            receipt['suite'] = validate_receipt(log.read_text(), token, profile)
            validate_equivalence(log.read_text(), token)
            receipt['editor_checks_success'] = True
            completed = {'unity-compile-and-all-checks', 'fixture-equivalence'}
        receipt['checks_success'] = True
        receipt['remaining'] = [g for g in selection['required'] if g not in completed]
    except (ValueError, KeyError, OSError) as error:
        receipt['error'] = str(error)
    (out/'receipt.json').write_text(json.dumps(receipt, indent=2)+'\n')
    print(json.dumps(receipt, indent=2))
    # This command ONLY certifies the selected UI or complete Editor suite; plan's remaining gates still apply.
    if not receipt.get('checks_success'):
        raise SystemExit(1)


if __name__ == '__main__':
    main()

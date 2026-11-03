"""Exercise reviewed routes and invalid evidence with a fake runner only."""
import contextlib
import io
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

import qa
from qa_fixtures import AFFECTED, AGGREGATE_ENTRYPOINTS, selected_fixtures, validate_affected


class AffectedRouteTests(unittest.TestCase):
    def test_cli_plan_and_real_wrapper_with_fake_runner(self):
        # Use the real CLI/Git/source fingerprint/wrapper boundary in an owned temp repo.
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); (root/'tools').mkdir(); (root/'workflow').mkdir()
            for name in ('qa.py', 'qa_fixtures.py', 'unity.sh'):
                path = root/'tools'/name; path.write_bytes((qa.REPO/'tools'/name).read_bytes())
                if name.endswith('.sh'): path.chmod(0o755)
            (root/'workflow/project.json').write_text('{"deploy_authorized":false}')
            balance = root/'unity-prototype/Assets/StarRacing/Resources/balance-config.json'
            balance.parent.mkdir(parents=True); balance.write_text('{}')
            (root/'.gitignore').write_text('.local/\n__pycache__/\n')
            runner = root/'fake-runner'
            runner.write_text('''#!/usr/bin/env python3
import json,os,sys
from pathlib import Path
a=sys.argv[1:]
assert "-noaudio" in a and "-muted" in a
assert "STAR_RACING_CONFIRM_FULL_TESTS" not in os.environ
assert a[a.index("-executeMethod")+1]=="StarRacingPrototype.WorldReusePlayModeChecks.RunLastAssets"
assert "-quit" not in a
assert "--exclusive" in a
Path(a[a.index("-logFile")+1]).write_text("LAST_WORLD_ASSETS_OK assertions=1134\\n")
Path(os.environ["STAR_RACING_WORLD_QA_DIR"],"success.json").write_text(json.dumps({"success":True,"assertions":1134}))
''')
            runner.chmod(0o755)
            def git(*args):
                return subprocess.check_output(['git', '-C', str(root), *args], stderr=subprocess.PIPE)
            git('init', '-q'); git('config', 'user.name', 'QA test'); git('config', 'user.email', 'qa@example.invalid')
            git('config', 'commit.gpgsign', 'false'); git('add', '.'); git('commit', '-qm', 'fixture baseline')
            # An unknown runtime path stays review-required; explicit affected selection is reviewed.
            changed = balance.parent/'new-runtime.cs'; changed.write_text('// changed dependency')
            cli = [sys.executable, str(root/'tools/qa.py')]
            env = dict(os.environ, UNITY_RUNNER=str(runner), UNITY_EDITOR='/fake/editor',
                       PYTHONDONTWRITEBYTECODE='1')
            def run(*args):
                return subprocess.run(cli+list(args), cwd=root, env=env, capture_output=True, text=True)
            auto = run('plan', '--base', 'HEAD'); self.assertEqual(auto.returncode, 0, auto.stderr)
            self.assertEqual(json.loads(auto.stdout)['scope'], 'review-required')
            plan = run('plan', '--base', 'HEAD', '--scope', 'affected', '--fixture', 'world-reuse')
            self.assertEqual(plan.returncode, 0, plan.stderr)
            self.assertEqual(json.loads(plan.stdout)['entrypoints'], [AFFECTED['world-reuse']['method']])
            result = run('checks', '--base', 'HEAD', '--scope', 'affected', '--fixture', 'world-reuse',
                         '--reason', 'World cache dependency reviewed', '--output', str(root/'.local/proof'))
            self.assertEqual(result.returncode, 0, result.stderr+result.stdout)
            receipt = json.loads(result.stdout)
            self.assertTrue(receipt['checks_success']); self.assertEqual(receipt['fixtures'][0]['status'], 'PASS')
            self.assertFalse(receipt['success']); self.assertIn('review-affected-coverage', receipt['remaining'])
            # A result directory cannot be reused as fresh evidence.
            duplicate = run('checks', '--base', 'HEAD', '--scope', 'affected', '--fixture', 'world-reuse',
                            '--reason', 'Reviewed', '--output', str(root/'.local/proof'))
            self.assertNotEqual(duplicate.returncode, 0)

    def invoke(self, root, names, runner, fingerprints=None):
        balance = root/'unity-prototype/Assets/StarRacing/Resources/balance-config.json'
        balance.parent.mkdir(parents=True, exist_ok=True); balance.write_text('{}')
        selection = dict(scope='affected', required=qa.GATES['affected'],
                         selected_fixtures=selected_fixtures(names))
        output = root/'proof'
        with patch.object(qa, 'REPO', root), patch.object(qa, 'plan', return_value=selection), \
             patch.object(qa, 'source_fingerprint', side_effect=fingerprints,
                          return_value='unchanged'), \
             patch.object(qa.subprocess, 'run', side_effect=runner), \
             patch('sys.argv', ['qa.py', 'checks', '--scope', 'affected', '--base', 'HEAD',
                               '--reason', 'Reviewed changed contracts and dependencies',
                               '--output', str(output)]), contextlib.redirect_stdout(io.StringIO()):
            try:
                qa.main()
            except SystemExit as error:
                self.assertEqual(error.code, 1)
        return json.loads((output/'receipt.json').read_text())

    def fake(self, command, **kwargs):
        method = command[command.index('-executeMethod')+1]
        spec = next(s for s in AFFECTED.values() if s['method'] == method)
        self.assertEqual(command[1], spec['mode'])
        self.assertEqual('-quit' in command, spec['quit'])
        for arg in ('-batchmode', '-noaudio', '-muted', '-nographics'):
            self.assertIn(arg, command)
        self.assertNotIn('STAR_RACING_CONFIRM_FULL_TESTS', kwargs['env'])
        self.assertNotIn('STAR_RACING_FORCE_CALIBRATION', kwargs['env'])
        log = Path(command[command.index('-logFile')+1]); output = log.parent
        if spec['output_env']:
            self.assertEqual(kwargs['env'][spec['output_env']], str(output))
        count = spec.get('exact_count', 123)
        field = spec.get('count_field', 'checks')
        line = f"{spec['marker']} {field}={count}"
        log.write_text(line+'\n')
        if spec['result']:
            result = output/spec['result']
            result.write_text(json.dumps(dict(success=True, **{field: count}))
                              if result.suffix == '.json' else line+'\n')
        return SimpleNamespace(returncode=0)

    def test_all_reviewed_fixtures_preserve_launch_contracts(self):
        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {
            'STAR_RACING_CONFIRM_FULL_TESTS': 'yes', 'STAR_RACING_FORCE_CALIBRATION': '1'
        }):
            receipt = self.invoke(Path(directory), list(AFFECTED), self.fake)
        self.assertTrue(receipt['checks_success'])
        self.assertFalse(receipt['success'])  # Integration/review gates remain open.
        self.assertFalse(receipt['human_acceptance'])
        self.assertEqual(len(receipt['fixtures']), len(AFFECTED))
        self.assertIn('review-affected-coverage', receipt['remaining'])
        self.assertIn('affected-integration-if-needed', receipt['remaining'])
        for row in receipt['fixtures']:
            self.assertEqual(row['status'], 'PASS')
            self.assertFalse(row['result']['fixture_token_validated'])
            self.assertGreaterEqual(row['wrapper_wall_seconds'], 0)

    def test_failure_and_admission_timeout_preserve_notrun_for_later_fixtures(self):
        for exitcode, expected in ((1, 'FAIL'), (75, 'NotRun')):
            with self.subTest(exit=exitcode), tempfile.TemporaryDirectory() as directory:
                receipt = self.invoke(Path(directory), ['basis-selection', 'world-reuse'],
                                      lambda *a, **kw: SimpleNamespace(returncode=exitcode))
                self.assertFalse(receipt['checks_success'])
                self.assertEqual([r['status'] for r in receipt['fixtures']], [expected, 'NotRun'])
                self.assertEqual(receipt['remaining'], qa.GATES['affected'])

    def test_missing_log_source_drift_and_spawn_error_never_pass(self):
        for scenario in ('missing-log', 'drift', 'spawn'):
            def runner(*args, **kwargs):
                if scenario == 'spawn':
                    raise OSError('Cannot launch wrapper')
                if scenario == 'drift':
                    return self.fake(*args, **kwargs)
                return SimpleNamespace(returncode=0)
            with self.subTest(scenario=scenario), tempfile.TemporaryDirectory() as directory:
                receipt = self.invoke(Path(directory), ['basis-selection'], runner,
                                      ['before', 'after'] if scenario == 'drift' else None)
                self.assertFalse(receipt['checks_success'])
                self.assertNotEqual(receipt['fixtures'][0]['status'], 'PASS')

    def test_invalid_selections_never_launch(self):
        for names in ([], ['world-reuse']*2, ['PrototypeChecks.RunWithFixtureEquivalence'], ['../Run']):
            with self.subTest(names=names), patch.object(qa.subprocess, 'run') as run:
                with self.assertRaises(ValueError):
                    qa.plan('HEAD', 'affected', 'reviewed', names)
                run.assert_not_called()
        with self.assertRaises(ValueError):
            qa.plan('HEAD', 'local-ui', 'reviewed', ['world-reuse'])

    def test_empty_or_foreign_duplicate_and_failed_outputs_rejected(self):
        spec = AFFECTED['vector-runtime']
        good = 'VECTOR_RUNTIME_CHECKS_OK assertions=123\n'
        for log, payload in (
            ('', dict(success=True, assertions=123)),
            (good*2, dict(success=True, assertions=123)),
            (good.replace('123', '0'), dict(success=True, assertions=0)),
            (good.replace('VECTOR_RUNTIME', 'FOREIGN'), dict(success=True, assertions=123)),
            (good, dict(success=False, assertions=123)),
            (good, dict(success=True, assertions=122)),
            (good+'error CS1234', dict(success=True, assertions=123)),
            (good+'Exception: failure', dict(success=True, assertions=123)),
        ):
            with self.subTest(log=log, payload=payload), tempfile.TemporaryDirectory() as directory:
                root = Path(directory); (root/'contracts.json').write_text(json.dumps(payload))
                with self.assertRaises(ValueError): validate_affected(log, spec, root)

    def test_missing_symlink_and_failure_artifacts_rejected(self):
        spec = AFFECTED['world-reuse']; log = 'LAST_WORLD_ASSETS_OK assertions=1134\n'
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            with self.assertRaises(ValueError): validate_affected(log, spec, root)
            other = root/'foreign.json'; other.write_text('{"success":true,"assertions":1134}')
            result = root/'success.json'; result.symlink_to(other)
            with self.assertRaises(ValueError): validate_affected(log, spec, root)
            result.unlink(); result.write_text(other.read_text()); (root/'failure.txt').write_text('fail')
            with self.assertRaises(ValueError): validate_affected(log, spec, root)

    def test_registry_matches_existing_source_and_aggregate_inventory(self):
        # Fail on source contract drift; adding files must never silently grow the aggregate.
        for spec in AFFECTED.values():
            cls, method = spec['method'].removeprefix('StarRacingPrototype.').split('.')
            source = (qa.REPO/f'unity-prototype/Assets/StarRacing/Editor/{cls}.cs').read_text()
            self.assertIn('public static void '+method+'()', source)
            self.assertIn('"'+spec['marker']+' ', source)
        source = (qa.REPO/'unity-prototype/Assets/StarRacing/Editor/PrototypeChecks.cs').read_text()
        body = source.split('public static void RunWithFixtureEquivalence(){', 1)[1].split('\n  }', 1)[0]
        calls = re.findall(r'(?<![\w.])([A-Z][\w.]*)\(\);', body)
        methods = ['StarRacingPrototype.' + (c if '.' in c else 'PrototypeChecks.'+c) for c in calls]
        self.assertEqual(methods, AGGREGATE_ENTRYPOINTS)


if __name__ == '__main__':
    unittest.main()

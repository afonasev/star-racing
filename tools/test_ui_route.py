import contextlib
import io
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import qa

class UiRouteTests(unittest.TestCase):
    def test_aggregate_receipt_names_entrypoints_without_claiming_every_editor_fixture(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            balance = root/'unity-prototype/Assets/StarRacing/Resources/balance-config.json'
            balance.parent.mkdir(parents=True); balance.write_text('{}')
            output = root/'proof'
            selection = dict(scope='full', required=qa.GATES['full'], entrypoints=qa.AGGREGATE_ENTRYPOINTS)
            def record(command, **kwargs):
                self.assertEqual(command[command.index('-executeMethod')+1], qa.check_method('full'))
                self.assertEqual(kwargs['env']['STAR_RACING_CONFIRM_FULL_TESTS'], 'yes')
                token = kwargs['env']['STAR_RACING_QA_TOKEN']
                end = dict(token=token, balanceHash=qa.hashlib.sha256(b'{}').hexdigest(),
                           assertions=450, rosterCases=6, ghostAssertions=25)
                Path(command[command.index('-logFile')+1]).write_text(
                    'PROTOTYPE_QA_BEGIN '+json.dumps(dict(token=token))+'\n'+
                    'PROTOTYPE_QA_END '+json.dumps(end)+'\n'+
                    'ROSTER_FIXTURE_EQUIVALENCE_OK cases=6 token='+token+'\n')
                return SimpleNamespace(returncode=0)
            with patch.object(qa, 'REPO', root), patch.object(qa, 'plan', return_value=selection), \
                 patch.object(qa, 'source_fingerprint', return_value='unchanged'), \
                 patch.object(qa.subprocess, 'run', side_effect=record), \
                 patch('sys.argv', ['qa.py', 'checks', '--base', 'HEAD', '--scope', 'full',
                                   '--confirm-full', '--reason', 'Fake runner contract only',
                                   '--output', str(output)]), contextlib.redirect_stdout(io.StringIO()):
                qa.main()
            receipt = json.loads((output/'receipt.json').read_text())
            self.assertEqual(receipt['schema_version'], 2)
            self.assertEqual(receipt['entrypoints'], qa.AGGREGATE_ENTRYPOINTS)
            self.assertTrue(receipt['entrypoints_success'])
            self.assertIn('review-affected-coverage', receipt['remaining'])
            self.assertNotIn('unity-compile-and-legacy-aggregate', receipt['remaining'])
            self.assertFalse(receipt['success'])
            self.assertNotIn('StarRacingPrototype.OrbitalRiftChecks.Run', receipt['entrypoints'])

    def test_hud_route_never_executes_or_certifies_full_suite(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            balance = root/'unity-prototype/Assets/StarRacing/Resources/balance-config.json'
            balance.parent.mkdir(parents=True); balance.write_text('{}')
            output = root/'proof'
            selected = {'scope':'local-ui', 'required':qa.GATES['local-ui']}
            def record(command, **kwargs):
                self.assertIn('-noaudio', command)
                self.assertEqual(command[command.index('-executeMethod')+1], qa.check_method('local-ui'))
                self.assertNotIn(qa.check_method('full'), command)
                Path(command[command.index('-logFile')+1]).write_text(
                    'RACE_HUD_CHECKS_OK assertions=221 token='+kwargs['env']['STAR_RACING_QA_TOKEN']+'\n')
                return SimpleNamespace(returncode=0)
            with patch.object(qa,'REPO',root), patch.object(qa,'plan',return_value=selected), \
                 patch.object(qa,'source_fingerprint',return_value='unchanged'), \
                 patch.object(qa.subprocess,'run',side_effect=record), \
                 patch('sys.argv',['qa.py','checks','--base','HEAD','--scope','local-ui',
                                   '--reason','HUD layout only','--output',str(output)]), \
                 contextlib.redirect_stdout(io.StringIO()):
                qa.main()
            receipt = json.loads((output/'receipt.json').read_text())
            self.assertTrue(receipt['ui_checks_success'])
            self.assertNotIn('editor_checks_success',receipt)
            self.assertEqual(receipt['suite']['assertions'],221)
            self.assertIn('affected-editor-playmode',receipt['remaining'])

    def test_missing_reason_never_launches_checks(self):
        with patch.object(qa,'plan',return_value={'scope':'local-ui'}), \
             patch.object(qa.subprocess,'run') as run, \
             patch('sys.argv',['qa.py','checks','--base','HEAD','--output','unused']), \
             contextlib.redirect_stderr(io.StringIO()):
            with self.assertRaises(SystemExit):qa.main()
            run.assert_not_called()

    def test_unauthorized_full_and_unknown_scope_never_launch_unity(self):
        for scope in ('full', 'review-required'):
            with self.subTest(scope=scope), patch.object(qa, 'plan', return_value={'scope': scope}), \
                 patch.object(qa.subprocess, 'run') as run, \
                 patch('sys.argv', ['qa.py', 'checks', '--base', 'HEAD',
                                    '--reason', 'production release', '--output', 'unused']), \
                 contextlib.redirect_stderr(io.StringIO()):
                with self.assertRaises(SystemExit):
                    qa.main()
                run.assert_not_called()

    def test_unknown_scope_has_no_aggregate_method(self):
        with self.assertRaises(ValueError):
            qa.check_method('review-required')

    def test_visual_probe_has_no_full_game_call(self):
        source = (qa.REPO/'unity-prototype/Assets/StarRacing/Editor/RaceHudChecks.cs').read_text()
        self.assertNotIn('PrototypeChecks.',source)
        self.assertIn('ui-suite-success.json',source)

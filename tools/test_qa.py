import json
import unittest
from qa import UI_FILES, check_method, validate_ui_receipt, GATES, scope_for, validate_receipt, validate_equivalence, local_profile_diff

class ScopeTests(unittest.TestCase):
    def test_full_test_gate_does_not_implicitly_build_or_deploy(self):
        self.assertIn("unity-compile-and-all-checks", GATES["full"])
        self.assertIn("fixture-equivalence", GATES["full"])
        self.assertNotIn("native-build", GATES["full"])
        self.assertNotIn("affected-player-playtest", GATES["full"])

    def test_ui_candidate_and_shared_dependency_boundary(self):
        for path in UI_FILES:
            self.assertEqual(scope_for([path]), 'local-ui')
            self.assertEqual(scope_for([path + '.meta']), 'local-ui')
            self.assertEqual(scope_for([path, 'unity-prototype/Assets/StarRacing/Runtime/MagneticVehicle.cs']), 'full')
        self.assertEqual(check_method('local-ui'), 'StarRacingPrototype.RaceHudChecks.Run')
        self.assertNotIn('unity-compile-and-all-checks', GATES['local-ui'])
        self.assertNotIn('fixture-equivalence', GATES['local-ui'])

    def test_local(self):
        self.assertEqual(scope_for(['docs/qa.md']), 'documentation')
        self.assertEqual(scope_for(['tools/qa.py', '.agents/references/qa-scope.md']), 'editor-checks')
        self.assertEqual(scope_for(['unity-prototype/Assets/StarRacing/Editor/PrototypeChecks.cs']), 'editor-checks')

    def test_full_dependency_boundary(self):
        for path in ['unity-prototype/Assets/StarRacing/Runtime/RaceDirector.cs',
                     'unity-prototype/Assets/StarRacing/Editor/PrototypeBuilder.cs',
                     'unity-prototype/Assets/StarRacing/Resources/balance-config.json',
                     'tools/unity.sh', 'unity-prototype/Packages/manifest.json', 'README.md', '../tools/qa.py']:
            with self.subTest(path=path):
                self.assertEqual(scope_for(['tools/qa.py', path]), 'full')
        self.assertEqual(scope_for([]), 'full')

    def test_profile_semantics(self):
        before = {'commands': {'build': 'build'}, 'check_scopes': {'unity': 'full'}, 'deploy_authorized': False}
        after = json.loads(json.dumps(before))
        after['commands']['qa_plan'] = 'plan'
        self.assertTrue(local_profile_diff(before, after))
        self.assertEqual(scope_for(['workflow/project.json']), 'full')
        self.assertEqual(scope_for(['workflow/project.json'], profile_safe=True), 'tooling')
        after['commands']['build'] = 'weakened'
        self.assertFalse(local_profile_diff(before, after))
        after = json.loads(json.dumps(before));after['deploy_authorized'] = True
        self.assertFalse(local_profile_diff(before, after))

    def test_rename_both_paths(self):
        self.assertEqual(scope_for(['docs/qa.md', 'unity-prototype/Assets/StarRacing/Runtime/qa.md']), 'full')

class UiReceiptTests(unittest.TestCase):
    def test_complete_ui_receipt(self):
        value = validate_ui_receipt('RACE_HUD_CHECKS_OK assertions=221 token=t\n', 't')
        self.assertEqual(value['assertions'], 221)
        self.assertFalse(value['match_simulations'])

    def test_invalid_ui_receipts(self):
        good = 'RACE_HUD_CHECKS_OK assertions=221 token=t\n'
        for log in ['', good.replace('221', '0'), good.replace('221', '220'),
                    good.replace('token=t', 'token=foreign'), good*2,
                    good+'error CS1234', good+'Exception: failure', good+'HUD_CHECK failed']:
            with self.subTest(log=log), self.assertRaises(ValueError):
                validate_ui_receipt(log, 't')

class ReceiptTests(unittest.TestCase):
    def log(self, **changes):
        end = dict(token='t', balanceHash='p', assertions=450, rosterCases=6, ghostAssertions=25)
        end.update(changes)
        return 'PROTOTYPE_QA_BEGIN '+json.dumps({'token':'t'})+'\nPROTOTYPE_QA_END '+json.dumps(end)+'\n'

    def test_equivalence_receipt(self):
        validate_equivalence('ROSTER_FIXTURE_EQUIVALENCE_OK cases=6 token=t\n', 't')
        for log in ['', 'ROSTER_FIXTURE_EQUIVALENCE_OK cases=0 token=t',
                    'ROSTER_FIXTURE_EQUIVALENCE_OK cases=6 token=foreign',
                    'ROSTER_FIXTURE_EQUIVALENCE_OK cases=6 token=t\n'*2]:
            with self.assertRaises(ValueError):validate_equivalence(log, 't')

    def test_complete(self):
        self.assertEqual(validate_receipt(self.log(), 't', 'p')['rosterCases'], 6)

    def test_reject_missing_foreign_zero_skipped(self):
        for log in ['', self.log(token='foreign'), self.log(balanceHash='foreign'), self.log(assertions=0),
                    self.log(rosterCases=5), self.log(ghostAssertions=0), self.log()*2,
                    self.log()+'Exception: failure', self.log()+'error CS1234',
                    '\n'.join(reversed(self.log().splitlines()))]:
            with self.subTest(log=log):
                with self.assertRaises((ValueError, KeyError)):
                    validate_receipt(log, 't', 'p')

if __name__ == '__main__': unittest.main()

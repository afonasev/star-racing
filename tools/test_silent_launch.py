import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest


class SilentLaunchTests(unittest.TestCase):
    def test_real_wrapper_forwards_startup_mode_without_launching_unity(self):
        wrapper = Path(__file__).resolve().parent / 'unity.sh'
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            capture = root / 'arguments.json'
            runner = root / 'runner'
            runner.write_text('#!/usr/bin/env python3\nimport json,os,sys\n'
                              'open(os.environ["CAPTURE"],"w").write(json.dumps(sys.argv[1:]))\n')
            runner.chmod(0o755)
            env = dict(os.environ, UNITY_RUNNER=str(runner), UNITY_EDITOR='/fake/editor',
                       CAPTURE=str(capture))
            cases = [
                (['shared', '-noaudio'], '/fake/editor', 1),
                (['shared', '-batchmode', '-executeMethod', 'Fixture.Run'], '/fake/editor', 1),
                (['shared', '-batchmode', '-noaudio'], '/fake/editor', 1),
                (['shared', '-executeMethod', 'Fixture.Run'], '/fake/editor', 1),
                (['shared', '-runTests'], '/fake/editor', 1),
                (['shared'], '/fake/editor', 0),
                (['shared', '-muted'], '/fake/editor', 1),
                (['shared', '--muted'], '/fake/editor', 1),
                (['shared', '-noaudio', '-muted', '--muted'], '/fake/editor', 1),
                (['shared', '-noaudio', '-muted'], '/fake/editor', 1),
                (['player', '/fake/Player', '-muted'], '/fake/Player', 1),
                (['player', '/fake/Player', '-noaudio', '--seed', '77'], '/fake/Player', 1),
                (['player', '/fake/Player', '-batchmode'], '/fake/Player', 1),
            ]
            for args, executable, count in cases:
                with self.subTest(args=args):
                    subprocess.run(['sh', str(wrapper), *args], env=env, check=True,
                                   capture_output=True, text=True)
                    forwarded = json.loads(capture.read_text())
                    command = forwarded[forwarded.index('--') + 1:]
                    self.assertEqual(command[0], executable)
                    self.assertEqual(command.count('-noaudio'), count)
                    self.assertEqual(command.count('-muted'), count)
                    if count:
                        self.assertGreater(command.index('-noaudio'), 0)
                    if '--seed' in args:
                        at = command.index('--seed')
                        self.assertEqual(command[at:at + 2], ['--seed', '77'])


if __name__ == '__main__':
    unittest.main()

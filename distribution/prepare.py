#!/usr/bin/env python3
"""Download the pinned native import library, verify before using it."""
import hashlib,urllib.request
from pathlib import Path
root=Path(__file__).resolve().parents[1]
target=root/'.local/desktop/velopack-native.zip'
target.parent.mkdir(parents=True,exist_ok=True)
if not target.exists():
    with urllib.request.urlopen('https://github.com/velopack/velopack/releases/download/1.2.158/velopack_libc_1.2.158.zip',timeout=60) as response:
        target.write_bytes(response.read())
expected=(root/'distribution/dependencies.json').read_text()
import json
if hashlib.sha256(target.read_bytes()).hexdigest()!=json.loads(expected)['nativeArchiveSHA256']:
    raise RuntimeError('Native SDK hash mismatch')
print('NATIVE_SDK_VERIFIED',target)

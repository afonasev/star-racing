#!/bin/bash
set -euo pipefail
TASK_TEMPO_DIR="$(cd "$(dirname "$0")" && pwd)"
TASK_TEMPO_REPO="$(cd "$TASK_TEMPO_DIR/../.." && pwd)"
TASK_TEMPO_TMP="$(mktemp -d -t star-racing-tempo)"
trap 'rm -rf "$TASK_TEMPO_TMP"' EXIT
for TASK_TEMPO_ARCH in arm64 x86_64; do
 clang++ -std=c++17 -O3 -fvisibility=hidden -dynamiclib -arch "$TASK_TEMPO_ARCH" -mmacosx-version-min=11.0 "$TASK_TEMPO_DIR/tempo.cpp" -o "$TASK_TEMPO_TMP/$TASK_TEMPO_ARCH.dylib"
done
lipo -create "$TASK_TEMPO_TMP/arm64.dylib" "$TASK_TEMPO_TMP/x86_64.dylib" -output "$TASK_TEMPO_REPO/unity-prototype/Assets/Plugins/macOS/libstarracing_tempo.dylib"
codesign --force --sign - "$TASK_TEMPO_REPO/unity-prototype/Assets/Plugins/macOS/libstarracing_tempo.dylib"

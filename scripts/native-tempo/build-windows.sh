#!/bin/bash
# Pinned LLVM-MinGW UCRT cross build. Does not download/install a toolchain.
set -euo pipefail
TASK_TEMPO_DIR="$(cd "$(dirname "$0")" && pwd)"
TASK_TEMPO_REPO="$(cd "$TASK_TEMPO_DIR/../.." && pwd)"
TASK_TEMPO_TOOLCHAIN="${1:?Usage: build-windows.sh /path/to/llvm-mingw-20260616-ucrt-macos-universal [output-directory]}"
TASK_TEMPO_OUTPUT="${2:-$TASK_TEMPO_REPO/unity-prototype/Assets/Plugins/Windows/x86_64}"
TASK_TEMPO_COMPILER="$TASK_TEMPO_TOOLCHAIN/bin/x86_64-w64-mingw32-clang++"
# Refuse an accidentally substituted compiler/runtime.
"$TASK_TEMPO_COMPILER" --version | head -1 | grep -F 'clang version 22.1.8' | grep -F 'ca7933e47d3a3451d81e72ac174dcb5aa28b59d1'
test -f "$TASK_TEMPO_TOOLCHAIN/x86_64-w64-mingw32/lib/libucrt.a"
mkdir -p "$TASK_TEMPO_OUTPUT"
"$TASK_TEMPO_COMPILER" -std=c++17 -O3 -fvisibility=hidden -D_WIN32_WINNT=0x0A00 \
 -shared -static -Wl,--no-insert-timestamp -Wl,--exclude-all-symbols \
 "$TASK_TEMPO_DIR/tempo.cpp" -o "$TASK_TEMPO_OUTPUT/starracing_tempo.dll"
"$TASK_TEMPO_TOOLCHAIN/bin/llvm-readobj" --file-headers --coff-exports --coff-imports \
 "$TASK_TEMPO_OUTPUT/starracing_tempo.dll"

# Windows native audio plugin

`Assets/Plugins/Windows/x86_64/starracing_tempo.dll` provides the same tempo DSP API as the macOS library. Its source and dependencies are kept here for rebuilding.

Use `build-windows.sh /absolute/path/to/llvm-mingw-20260616-ucrt-macos-universal` with the pinned toolchain described in `dependency-revisions.json`. The script checks compiler identity and UCRT before producing the DLL. Building the DLL does not verify a Windows Unity Player; that platform needs its own installation and runtime test.

Dependency licenses are in `third_party` and the packaged Resources notices.

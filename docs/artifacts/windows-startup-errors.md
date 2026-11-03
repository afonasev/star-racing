# Системные сообщения при сбоях запуска — 2026-11-03

Baseline: `e4eeba65649097972db92ec6cdbb39f37928b9e6`. Owner: chat `01a1a24d-2f38-7931-9ec0-10c6d28cbee7`; branch `codex/fix-windows-startup`; worktree `/Users/eaafonasev/.codex/worktrees/912b/star-racing`.

Small direct fix: launcher/bootstrap diagnostics only, without Unity runtime/gameplay changes. Launcher exceptions have actionable Russian messages, error codes and a support-log path. Before ready-menu exit (including zero), abnormal exit after readiness, and missing readiness after two minutes are visible. Timeout warns once, keeps the live Player and continues readiness observation. Native bootstrap reports installation gate, path, process creation, engine load and missing entry failures. OS loader failures before these entry points remain outside their handlers.

## Affected verification

- PASS: `/Users/eaafonasev/.dotnet/dotnet run --project distribution/launcher-tests/LauncherChecks.csproj` — `LAUNCHER_CHECKS_PASS 46`. Original startup/update/recovery checks retained; real fake-child scenarios cover normal exit, abnormal exit, early zero exit and timeout without killing/replacing the child. Only the added timeout fixture supplies a short observation interval; production remains two minutes.
- PASS: `python3 -m unittest distribution.tests.test_launcher_packaging distribution.tests.test_windows_dpi` — 10 tests. Covers existing packaging and native Windows manifest resources.
- PASS: `x86_64-w64-mingw32-g++ -std=c++17 -O2 -static -municode -mwindows -c distribution/windows/bootstrap.cpp -o .local/windows-startup/bootstrap.o`. Existing GPU-export `extern` warnings only; no new link dependencies.
- PASS: `git diff --check`.
- NotRun: real Windows dialogs, installed-client startup, Unity/game suite, Player build, installer and publication. No release permission inferred.

## Original incident and continuation

The reported 0.3.16 Windows hang remains unconfirmed and unresolved. The user reports `Star Racing.exe` stays alive; full executable path and Player.log presence/content are pending. These diagnostics do not establish its cause. The retained published package manifest targets the launcher and its UnityPlayer exports both UnityMain entries.

Keep this worktree and `.local/windows-startup` (approximately 38 MiB of copies extracted from the retained release, plus the bootstrap object) for the original startup investigation. Remove the diagnostic copies after the cause is established and necessary evidence is preserved. Disposable .NET build outputs are removed after verification. Foreign main/planning edits and processes are preserved. Next step: inspect the Windows process path/log, repair the demonstrated cause, and perform affected checks; installing/releasing the diagnostic change remains separately authorized work.

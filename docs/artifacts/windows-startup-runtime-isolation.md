# Изоляция Windows-лаунчера от Unity Mono — 2026-11-03

Scope: follow-up to the reported Windows launch failure. Small direct packaging fix. Baseline `84fa5b3581c610b1d16da9a6700f94a7fc7dd3f0`; branch `codex/isolate-windows-launcher-runtime`; owner chat `01a1a24d-2f38-7931-9ec0-10c6d28cbee7` in `/Users/eaafonasev/.codex/worktrees/912b/star-racing`.

## Evidence and interpretation

The user's photographed Player.log reaches Unity6000.3.23f1, D3D12 initialization and `Begin MonoManager ReloadAssembly`. It does not identify a failing assembly or prove a graphics failure. Physical Windows startup remains FAIL as reported; no physical fix verification is available.

The retained published0.3.16 portable archive contains both root `current/mscorlib.dll` (59216 bytes, CoreCLR facade) and `current/Star Racing_Data/Managed/mscorlib.dll` (4632064 bytes, Mono), plus conflicting System assemblies. Publishing the launcher directly into current created this overlap.

A minimal C# probe was compiled with Unity's mcs and executed using its Mono CLI on macOS with `MONO_PATH` pointing to the extracted Windows Managed assemblies. The clean directory binds Managed/System.Net.Http4; the polluted directory binds root System.Net.Http8 instead. The type-load-only probe returns0 for the expected version and2 for the wrong version. This proves assembly shadowing in that probe, **not** the exact Windows Player hang. These are CLI probes, not a Unity Editor/Player or Windows acceptance run.

The initial probe attempted Console/HttpClient use: clean exit1 from Windows BCL calling SetThreadErrorMode on macOS; polluted exit1 from a TypeLoadException involving System.Private.CoreLib. These original probe FAILs are preserved. The corrected probe uses metadata only to avoid cross-platform BCL native calls; it does not erase the first result.

## Change

Windows publishing stages a self-contained single-file executable separately, rejects unexpected external dependencies, then copies only StarRacingLauncher.exe. Packaging also rejects an already contaminated Player input without deleting files. Configuration/mainExe, Player names, update/recovery identities, and macOS layout are retained. Existing installed0.3.16 files are not modified by source integration.

The .NET8 documented single-file properties are `PublishSingleFile=true` and `IncludeNativeLibrariesForSelfExtract=true`: https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview . No trimming or compression is enabled. AppContext.BaseDirectory remains the launcher's existing way to find adjacent configuration.

## Affected QA

- PASS: `python3 -m unittest distribution.tests.test_launcher_packaging distribution.tests.test_recovery_packaging distribution.tests.test_windows_installer` —17 tests. Existing contracts retained; added loose-dependency and contaminated-input refusal cases.
- PASS: real Windows-targeted .NET publish through `launcher_package.install` into an owned QA directory with an explicitly fake Player placeholder. Actual StarRacingLauncher.exe is69272813 bytes, with no adjacent runtime DLLs. This is a tooling artifact, not a Unity build or installable client.
- PASS: static bundle inspection finds173 embedded files, including mscorlib.dll, System.Private.CoreLib.dll, Velopack.dll and StarRacingLauncher.dll; hash/offset manifest is retained. An initial inspection incorrectly required a separate coreclr.dll bundle entry and failed; that assumption is withdrawn (the single-file host can carry runtime code itself). This was a diagnostic-script failure, not a product result. Native execution is NotRun.
- PASS: corrected Mono assembly-selection probe detects the expected version difference as described above.
- PASS: `git diff --check`; strict canonical desktop-launcher specification validation.
- NotRun: real Windows launcher/Unity startup, dialogs, physical install/update/repair, Unity suite, Player build, installer/release/publication. The two-minute startup diagnostics exist in baseline but have not been delivered to the installed client.

## Preservation and continuation

Durable evidence: `/Users/eaafonasev/Projects/star-racing/planning/evidence/windows-startup-runtime-isolation/` (user screenshot, both original probe logs, corrected logs/source, bundle manifest). Published0.3.16 archive remains unchanged in its retained release location. Disposable .NET outputs, copied probe assemblies and the synthetic single-file probe binary are removed after inspection. Original `.local/windows-startup` copies remain for incident investigation.

Retain the owned worktree for the unresolved Windows incident; next step is separately authorized installer/release preparation and physical Windows startup verification. After the incident is resolved, remove the diagnostic copies and finalize the retained worktree. No process was killed and no system audio or foreign main/planning edits were changed.

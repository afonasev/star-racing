# Production 0.3.3 preparation

Human instruction (2026-10-13): build and publish the latest game to production.

Game main snapshot: `ad0f76a8da07473a9b77e852eeb1e38be33a6bc3`. Publication baseline: `e995f5a5e65deb933190d27046252939bc6d49ba` (production 0.3.2, sequence 3). Export committed Unity source, distribution icons and update tests; retain public-only installer workflow and publishing tools. Native11 remains the shipped catalog; unmerged opt-in Native12 work is excluded.

Selected affected checks: CloudlineVehicleChecks, DaylightVisualChecks, VehicleContactShadowsChecks, EnvironmentTextureChecks, DisplaySettingsChecks, DesktopUpdateChecks, FairAiChecks.RunContracts, RecoveryGhostChecks, TrackCompositionChecks and BroadphaseAffectedChecks.Run. These cover the changed visuals/settings, updater, AI resources, track generation and physics/recovery; this is not the aggregate full suite. Compile and packaged macOS smoke additionally cover release lifecycle, 1-4 human viewport configurations, recovery, pause/repeat and natural AI finish.

Distribution: 34 Python tests passed; C# UpdateContracts passed 70 assertions. All Editor/Player automation uses `-noaudio` from startup and shared admission. Initial temporary QA aggregator had a wrong method name and failed compilation; corrected before successful affected checks.

Production follows the existing explicit unsigned-production catalog route, version 0.3.3, sequence 4. Both platform builds, exact-source Windows wizard, API size/digest and real full downloads are required before channel promotion. Physical Windows/macOS, gamepad, listening and human visual acceptance remain separate; full tests were not requested.

Durable release evidence/artifacts will be preserved under `/Users/eaafonasev/Projects/star-racing-planning/evidence/production-0.3.3/`.

Affected Editor gate: exit 0. Broadphase PlayMode: exit 0, 83374 live-clock assertions; recovery fixture 1148 assertions; road 115 impacts / 6315 probes. Import-only metadata serialization restored to committed game snapshot before release.

First native builds both exited 0; release stopped before draft because URP global settings auto-serialization was outside the restoration list. Restored the committed settings and added Assets/Settings to the existing preservation contract with a regression assertion. No production/draft/channel mutation occurred. Repeat builds use the corrected publishing revision.

Packaged smoke attempt was interrupted after diagnosing idle human input in the older local QA harness. Retain the stronger existing public CloudlinePlaytest harness from production 0.3.2: virtual gamepad through ordinary input/physics, unchanged 600-second deadline, natural human and AI finish assertions. Gameplay source remains the latest game-main snapshot; only this opt-in public QA harness is retained. No draft/channel mutation occurred. Batchmode screenshots are NotRun; no visual acceptance is claimed.

## Published

Production 0.3.3 / sequence 4 published from `6d70db5801537f21e129fdc8e382d186c72509f2`: https://github.com/afonasev/star-racing/releases/tag/v0.3.3 . Both native builds exited 0. Windows wizard run 38417826741 succeeded from the exact source/portable identity. Packaged macOS smoke exited 0 with 78 assertions, natural human and AI finish, and restored preferences. All 20 assets passed actual download size/SHA256 readback before switching channel-production. Browser-visible landing links show 0.3.3 for both Windows and macOS.

Exact artifacts and complete logs: `/Users/eaafonasev/Projects/star-racing-planning/evidence/production-0.3.3/release/`. Selection/failed-attempt logs: `/Users/eaafonasev/Projects/star-racing-planning/evidence/production-0.3.3/qa/`. Full suite, installed Windows/macOS update/device/gamepad, audible/visual human acceptance were not performed. Batchmode screenshot capture failed and is not visual evidence.

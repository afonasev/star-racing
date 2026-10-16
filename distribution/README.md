# Star Racing desktop delivery

Актуальный срез стандартизации, recovery и формат Git changelog: [DESKTOP-DELIVERY.md](../docs/template-standards/DESKTOP-DELIVERY.md). До завершения standardize-desktop-delivery это исходники ветки, а не новая опубликованная поставка.

The game uses Unity Mono and pinned Velopack 1.2.158. Updates are offered in the main menu. Startup discovers signed metadata once, without a manual retry. Download starts after a click and suspends before a race. A verified prepared update is applied by the external recovery agent after **Перезапустить**, or at the next ordinary launch after exit; both use the Lab dirty guard. Settings remain in Unity's persistent user directory.

`AuthenticatedUpdateSource` verifies RSA/SHA-256 metadata before exposing an asset to Velopack. It checks channel, app ID, monotonic sequence, exact immutable HTTPS URL, size and SHA-256. The full package is verified before Velopack receives it and immediately before activation. Full packages are used. A separately retained signed previous package supports external recovery until health ack; binary deltas remain outside scope. Installed A→B→A is a separate open platform gate.

## Toolchain and builds

Use Unity 6000.3.23f1 with Windows/macOS build support, .NET SDK 8 and `vpk` 1.2.158. Windows packaging also needs x86_64-w64-mingw32-g++. Resolve native SDK inputs with `python3 distribution/prepare.py`; `dependencies.json` pins their hashes. Build sequentially in this worktree through `tools/unity.sh`:

```sh
STAR_RACING_VERSION=0.2.1-test.2 BEE_BUILD_THREADS=2 tools/unity.sh shared -batchmode -nographics -disableManagedDebugger -quit -executeMethod StarRacingPrototype.PrototypeBuilder.BuildMac -logFile /absolute/evidence/build-mac.log
STAR_RACING_VERSION=0.2.1-test.2 BEE_BUILD_THREADS=2 tools/unity.sh shared -batchmode -nographics -disableManagedDebugger -quit -executeMethod StarRacingPrototype.PrototypeBuilder.BuildWindows -logFile /absolute/evidence/build-windows.log
```

`-disableManagedDebugger` avoids a debugger-listener shutdown stall on a host with several Unity projects. Build completion markers do not replace exit zero.

Package each exact Player with `package.py --channel osx-universal|win-x64 --version VERSION --sequence INTEGER --player ABSOLUTE_PLAYER --output ABSOLUTE_NEW_OUTPUT --key ABSOLUTE_PRIVATE_KEY --dotnet ABSOLUTE_DOTNET --vpk ABSOLUTE_VPK --notes TEXT`. Versions/output directories are immutable. Keep the RSA private key outside Git and the VPS. It must match the committed public key and the Player's `DesktopUpdateKey.cs`. The signing key protects the update feed; it is separate from OS code signing.

Windows bootstrap invokes the native Velopack hook before UnityMain. macOS ships a universal Player and helper; `--signAppIdentity -` seals the modified app with an ad-hoc signature. Installers have no paid Windows Authenticode or Apple Developer ID/notarization. Record actual warning/install behavior during platform acceptance.

## Publication

`github_publish.py --release ABSOLUTE_RELEASE_DIR [--release ...] --readback ABSOLUTE_EVIDENCE.json` verifies signed local identities and GitHub asset SHA256/size, then publishes immutable binary assets to public `afonasev/star-racing` release tag `vVERSION`. Never overwrite a published asset or send the private key. Promote the landing and signed latest metadata only after complete HTTPS download/hash readback. `publish.py` retains identity validation and historical VPS tooling; its binary uploader is not the current publication path.

Serve signed `latest.json` from the origin as no-store; the landing uses GitHub installer URLs. Supported old `/releases/CHANNEL/VERSION/*-full.nupkg` paths go through the no-disk compatibility relay. Missing paths return 404. The landing is static `landing/` with real game imagery and both platform links. Former `/opt/star-racing` was removed at explicit user request on 2026-10-04; origin binaries were removed on 2026-10-06 after GitHub verification. Caddy changes are confined to the racing host.

Windows packaging now requires NSIS (`makensis`). `windows/installer.nsi` provides a directory picker defaulting to the Windows x64 Program Files folder + `Star Racing`, two checked finish-page options for the desktop shortcut and launch. The unelevated wizard runs an elevated install worker, then launches from the original user. NSIS installs the complete SDK portable layout, keeps `.portable`, owns one HKLM uninstall entry and its elevated uninstaller, and creates an original-user desktop shortcut on the final screen and a common start-menu shortcut targeting the stable root stub. Velopack portable updates preserve that ownership and the shortcut choice; Program Files ACLs are not relaxed. The wizard records DisplayVersion for install/repair; the running game remains the authority after an in-app update. Recognized same-version installations offer Repair; newer versions install over the original root transactionally. Downgrades, mixed release tracks and unrelated nonempty targets are rejected. Uninstall preserves user preferences.

For an existing exact Player release, build only its wizard with `python3 distribution/windows/build_installer.py --version VERSION --portable ABSOLUTE_PORTABLE_ZIP --output ABSOLUTE_NEW_SETUP_EXE`. Keep the sidecar identity and the source portable SHA. Installer UI, both checkbox states, UAC cancellation/alternate admin, protected-folder update, and uninstall-after-update need actual Windows verification. Cross-compilation is not that acceptance.

## Verification and acceptance

Select affected C#/Python/Unity fixtures against a recorded baseline; commands and actual results for this change are in its QA plan. Full tests require separate human authorization. Run strict OpenSpec validation for changed planning artifacts. Probe results do not replace an installed Player.

Launch macOS UI QA through `tools/unity.sh player python3 distribution/run_packaged_qa.py --app /absolute/Star\ Racing.app --log /absolute/evidence/player.log` so the exclusive Unity guard covers helper-driven restart too. Record the exact package identity, native build exit, main menu and offline behavior, A→B download/activation, preserved settings and ordinary restart without activation. Physical Windows acceptance is performed by the user on Windows with the same A→B candidate pair. Keep the exact candidates until acceptance; do not archive the change while these gates remain open.

## GitHub distribution (2026-10-06)

Public source: https://github.com/afonasev/star-racing. Binary release tags are `vVERSION`; installers are `Star-Racing-VERSION-Windows-Setup.exe` and `Star-Racing-VERSION-macOS-Setup.pkg`. `github_publish.py --release DIR --readback EVIDENCE.json` verifies local signed identity and GitHub asset SHA256/size before landing promotion. No clobber uploads. Game, bootstrap, installer and shortcut use the existing landing/favicon.svg game mark (raster exports in icons/).

Small signed latest descriptors stay on racing.afonasev.tech; this preserves existing pinned clients. New Players map authenticated identity to the exact public GitHub repository and accept only its HTTPS release-assets redirect, then enforce signed size/SHA256. Old Players use legacy_relay.py on the origin: exact allowlisted package paths stream GitHub objects without storing binaries on VPS. Never delete binary objects until GitHub hash readback and relay smoke pass. The relay has no arbitrary URL API.

# Star Racing desktop delivery

The game uses Unity Mono and pinned Velopack 1.2.158. Updates are offered in the main menu. Startup checks signed metadata only. Download starts after a click; activation requires a separate **Перезапустить и установить** click in the preparation menu. A normal exit or launch does not activate a staged update. Settings remain in Unity's persistent user directory.

`AuthenticatedUpdateSource` verifies RSA/SHA-256 metadata before exposing an asset to Velopack. It checks channel, app ID, monotonic sequence, exact immutable HTTPS URL, size and SHA-256. The full package is verified before Velopack receives it and immediately before activation. First releases use full packages, without manual reinstallation; incremental binary deltas and recovery from a successfully installed broken Player are outside this release.

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

`publish.py --release ABSOLUTE_RELEASE_DIR [--release ...] --host gfe` verifies descriptor signatures and all local/remote hashes, then publishes immutable objects under `/opt/star-racing-desktop/public/releases/CHANNEL/VERSION/`. Add `--latest` only for a verified candidate: it atomically promotes the signed channel descriptor with monotonic sequence checks. `--downloads-output landing/downloads.json` generates links only when both installers are present. Never overwrite a published version or send the private key.

Serve `/releases/*` directly, missing objects as 404, `latest.json` as no-store, versioned objects as immutable. The landing is static `landing/`, uses real baseline game imagery and has both platform links. The former `/opt/star-racing` browser root was removed at the user’s explicit request on 2026-10-04; root now serves the download landing. A Caddy root switch changes only `racing.afonasev.tech`, after config validation; preserve its exact former configuration.

Windows packaging now requires NSIS (`makensis`). `windows/installer.nsi` provides a directory picker defaulting to the Windows x64 Program Files folder + `Star Racing`, two checked finish-page options for the desktop shortcut and launch. The unelevated wizard runs an elevated install worker, then launches from the original user. NSIS installs the complete SDK portable layout, keeps `.portable`, owns one HKLM uninstall entry and its elevated uninstaller, and creates an original-user desktop shortcut on the final screen and a common start-menu shortcut targeting the stable root stub. Velopack portable updates preserve that ownership and the shortcut choice; Program Files ACLs are not relaxed. ARP intentionally omits DisplayVersion because Velopack does not update NSIS registration. Nonempty targets and duplicate NSIS installations are rejected; existing games update through their main menu. Uninstall preserves user preferences.

For an existing exact Player release, build only its wizard with `python3 distribution/windows/build_installer.py --version VERSION --portable ABSOLUTE_PORTABLE_ZIP --output ABSOLUTE_NEW_SETUP_EXE`. Keep the sidecar identity and the source portable SHA. Installer UI, both checkbox states, UAC cancellation/alternate admin, protected-folder update, and uninstall-after-update need actual Windows verification. Cross-compilation is not that acceptance.

## Verification and acceptance

Run `distribution/tests/UpdateContracts.csproj` with .NET 8, `python3 -m unittest discover -s distribution/tests -p 'test_*.py'`, repository tooling tests, strict OpenSpec validation and the full `tools/qa.py checks` receipt against the reviewed baseline. Probe results do not replace an installed Player.

Launch macOS UI QA through `tools/unity.sh player python3 distribution/run_packaged_qa.py --app /absolute/Star\ Racing.app --log /absolute/evidence/player.log` so the exclusive Unity guard covers helper-driven restart too. Record the exact package identity, native build exit, main menu and offline behavior, A→B download/activation, preserved settings and ordinary restart without activation. Physical Windows acceptance is performed by the user on Windows with the same A→B candidate pair. Keep the exact candidates until acceptance; do not archive the change while these gates remain open.

## GitHub distribution (2026-10-06)

Public source: https://github.com/afonasev/star-racing. Binary release tags are `vVERSION`; installers are `Star-Racing-VERSION-Windows-Setup.exe` and `Star-Racing-VERSION-macOS-Setup.pkg`. `github_publish.py --release DIR --readback EVIDENCE.json` verifies local signed identity and GitHub asset SHA256/size before landing promotion. No clobber uploads. Game, bootstrap, installer and shortcut use the existing landing/favicon.svg game mark (raster exports in icons/).

Small signed latest descriptors stay on racing.afonasev.tech; this preserves existing pinned clients. New Players map authenticated identity to the exact public GitHub repository and accept only its HTTPS release-assets redirect, then enforce signed size/SHA256. Old Players use legacy_relay.py on the origin: exact allowlisted package paths stream GitHub objects without storing binaries on VPS. Never delete binary objects until GitHub hash readback and relay smoke pass. The relay has no arbitrary URL API.

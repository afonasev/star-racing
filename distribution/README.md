# Star Racing desktop delivery

Обязательный регламент публикации: [docs/PUBLISHING.md](../docs/PUBLISHING.md). Машиночитаемые правила: `python3 distribution/release.py --show-policy` (без сборки, ключа и сетевых запросов).

Unity Mono remains the game engine. Velopack 1.2.158 provides full-package updates without reinstalling the wizard. `AuthenticatedUpdateSource` authenticates RSA/SHA256 metadata before exposing a full asset to the SDK; it checks app ID, platform, version, release track, monotonic sequence, exact GitHub URL, size and SHA256. It rechecks the complete package before SDK helper extraction and before activation. The private key stays outside the repository, Player, CI and VPS.

New Players use only GitHub: fixed REST releases `channel-production` / `channel-test` contain signed per-platform envelopes. Production builds accept stable versions; test builds accept prereleases. Each track has independent sequence state and receipts. The compile-time track is set using `BuildPlayerOptions.extraScriptingDefines`, not inferred from GitHub's latest release.

Launch and “Проверить” request metadata. Only “Обновить” starts background download, with progress/cancellation while gameplay remains available. A verified downloaded receipt authorizes activation after exit: “Перезапустить” from the preparation menu or the next normal launch. Both stock SDK auto-apply paths stay disabled; our startup code authenticates the receipt and cached bytes first. An external attempt journal is saved before starting the helper. If the old version starts again after failure/UAC cancellation, it stays playable and requires an explicit retry, avoiding repeated UAC prompts. macOS activation is restricted to a writable app parent (normally `~/Applications`), avoiding the SDK's non-atomic elevated move fallback.

Profiles/Unity preferences and updater receipts live in user storage outside the payload. Uninstall leaves them intact. Windows downloads under a protected Program Files installation use Velopack's LocalAppData cache, without relaxing installation ACLs.

## Publish with one command

On the release Mac, install Unity **6000.3.23f1** with Windows/macOS build support, Python 3.11+, OpenSSL, GitHub CLI authenticated with repository/actions/workflow permissions, and MinGW (`x86_64-w64-mingw32-g++`, windres). The default-branch workflow `.github/workflows/windows-wizard.yml` must be present once. The script prepares its own pinned .NET SDK **8.0.414 osx-arm64** (SHA512 checked), `vpk` **1.2.158**, and hash-pinned native SDK. Signing key must match `update-public.pem`.

From a clean, verified source revision already pushed to the public repository:

```sh
STAR_RACING_SIGNING_KEY=/absolute/private/update-private.pem python3 distribution/release.py --track test --version 0.2.2-test.3 --sequence 11 --notes 'GitHub-only updater candidate'
```

For the next test use a new immutable version and increasing sequence (for example `0.2.2-test.4`, sequence `12`). Production publication uses `--track production --version 0.2.2` and its own increasing sequence. Physical acceptance must precede production promotion; this task authorizes test publication only.

The command builds both native Players, restores owned generated source files, packages full updates, and creates a draft. GitHub Actions compiles the branded Windows wizard from the exact portable SHA and source commit, uploading only to that draft. Local signing then authenticates both full packages and installers. Every asset, identity, SDK feed, signed envelope and manifest is uploaded; the command checks GitHub digest/size and actually downloads every draft asset to verify SHA256 before publication. An atomic temporary GitHub ref serializes publishers of each track across hosts; a fresh channel read also detects external promotion. Never steal a stale reservation after a crash without checking its owner. Only after publishing the complete version release does it update the chosen channel pointer; pointers never become GitHub's “latest”. The release stays unpublished on any earlier failure. Existing versions/drafts and assets are never overwritten: inspect a failed draft and use a new version after correcting the failure.

Outputs/evidence are under `.local/releases/VERSION`. Record both `identity.json` files, `manifest.json`, download readback, workflow revision/compiler and build logs in durable evidence before cleaning intermediates. Signing the update metadata is distinct from Windows Authenticode / Apple Developer ID; current test installers are unsigned by those OS certificate systems (macOS bundle is ad-hoc sealed).

## Installation

Names: `Star-Racing-VERSION-Windows-Setup.exe` and `Star-Racing-VERSION-macOS-Setup.pkg`. Existing game icon is used by both apps, wizard and shortcuts.

Windows: Program Files + `Star Racing` by default, UAC install worker, one HKLM uninstall registration, no user/all-users choice. The outer wizard remains unelevated and presents two checked finish-page options together: desktop shortcut and launch. Shortcut is created by the original user's finish callback, including redirected/OneDrive Desktop; launch runs from that same original wizard. NSIS installs the complete portable layout, `.portable` prevents SDK takeover of uninstall/shortcut ownership. Uninstaller forwards its original `$INSTDIR` to its elevated copy; it does not replace it with temporary `$EXEDIR`. User settings are never deleted. Nonempty unowned folders / a duplicate installation are rejected; use in-game updates.

macOS: universal package installs in user Applications. Protected app parents remain playable but require moving the app to `~/Applications` before safe updates.

## Landing and old-client migration

Landing reads the GitHub channel REST endpoint directly with browser CORS and verifies both metadata signatures using WebCrypto before enabling versioned installer links. It never fetches release assets with browser JavaScript, so GitHub's asset redirects do not need to supply fetch CORS headers. Normal anchor downloads follow GitHub redirects. Default is production; `/?channel=test#download` explicitly selects test. No per-release VPS landing edit is needed.

Old installed clients use the historical origin-only schema1 feed. One-time migration preserves this interface and all old relay paths:

```sh
python3 distribution/migrate_legacy.py --release /absolute/osx-universal/VERSION --release /absolute/win-x64/VERSION --evidence /absolute/new-bridge-evidence
```

Run only for the first published test bridge after its actual download/signature checks. It uploads small signed descriptors and extends the no-disk GitHub relay catalog; it refuses non-increasing legacy sequences and production descriptors. Retain the old endpoint until existing clients are confirmed migrated. Subsequent new-client releases need only the GitHub publication command. `publish.py` / `github_publish.py` are historical tooling, not the new release pipeline.

## Checks and device acceptance

Automated contracts: `dotnet run --project distribution/tests/UpdateContracts.csproj`, Python distribution/tooling tests, strict OpenSpec, full Editor suite/equivalence, and both native build exits. Real GitHub transport through the exact updater source can be tested with:

```sh
dotnet run --project distribution/tests/UpdateContracts.csproj -- --live test win-x64 /absolute/new-full-package.nupkg
```

Run for both platforms; it verifies live metadata signature then downloads exact signed bytes directly from GitHub. Preserve its output separately from build/API digest checks.

Device acceptance remains distinct: physical Windows installer icon, Program Files/UAC (including cancel/alternate admin), both checked finish options, original-user desktop shortcut/normal unelevated launch, A→B background download while racing, both activation routes, corrupted/truncated cache rejection, failed UAC retry, settings retention, uninstall after update. On actual Apple Silicon and Intel Macs check package install/Gatekeeper/icon, both activation routes, offline launch and profile retention. Automated code/probe/build checks do not establish these outcomes. Launch Player QA through `tools/unity.sh player` so the Unity guard covers owned activity; never kill a foreign Player.

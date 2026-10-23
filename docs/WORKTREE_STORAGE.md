# Lightweight worktrees and evidence storage

Create an owned worktree with `python3 tools/worktree_storage.py create <absolute-path> --branch codex/<change> --ref main`. It uses --no-checkout and a per-project sparse selection before initial materialization. The selection is recorded in `tools/worktree_storage.json`; authored source, specifications, templates and source-directory exceptions stay available.

Codex local setup and the installed post-checkout hook prepare new managed worktrees after checkout. They reduce retained size but do not prevent its initial peak. Existing active/dirty/primary worktrees, custom sparse selections, local-only evidence and foreign hooks are preserved. No browser or Unity process is involved.

Keep compact text reports/manifests/links in docs/artifacts. Use ignored `.local/<task>/` for raw logs, XML/CSV, frames, video, archives and intermediate builds. Durable evidence goes once to shared planning evidence/, in the canonical Star Racing development planning checkout (shared with the main Star Racing repository); do not create planning copies. Source exceptions require an explicit path and reason in the storage profile. Product assets outside docs/artifacts remain unaffected.

`make storage-check`, the pre-commit hook and PR guard examine only added/changed docs/artifacts, reject raw/binary payloads and documents over1MiB, and preserve unchanged historical evidence. Human/device acceptance and FAIL/NotRun are not changed by this storage check.

Historic binaries are not deleted by this adoption. Any later cleanup must verify owner, integration, acceptance and exact recovery hashes first. Preserve pending builds/evidence. Install hooks after main integration with `python3 tools/worktree_storage.py install-hooks`; existing custom hooks/core.hooksPath are not overwritten.

Adoption baseline: `main@c179427560284dd2376eb0b574a1e0afa769fd2d`. Only tooling/settings/docs are changed. Verify with `PYTHONPATH=tools python3 -m unittest tools.test_worktree_storage` and `make storage-check`; no Unity/full/build/deploy is required.

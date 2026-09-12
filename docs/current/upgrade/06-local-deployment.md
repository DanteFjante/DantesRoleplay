# Local deployment and database activation

The user explicitly authorized finishing the implementation, loading the changes onto master and into the database, cleanup, and starting the system so they can try it on 2026-09-12. This authorizes the existing supported backup, rehearsal, migration, catalog synchronization, application/page activation, and local release-selection operations needed for this upgrade. Preserve live gameplay and runtime-authored records; do not reset the database or replace it with fixtures.

## Ownership and order

Coordinator owns master, shared decisions, this plan, and final browser verification. Agent `website_integration` prepares database/release operations and receives exclusive deployment write ownership from the coordinator before live mutations. Agent `workflow_publication_finish` investigates the website/application publication path read-only. One writer performs release and database changes.

1. Merge the accepted combined implementation onto master and verify its source matches the tested revision.
2. Resolve the saved runtime launch profile, actual running process, database/blob paths, selected application/page, and existing audience policy. Do not guess a release from the newest directory or development defaults.
3. Use consistent SQLite backup and blob preservation; export corresponding database-authored records and compare them with reviewed catalog files before synchronization. Keep an independently verified recovery point and use a separate copy for rehearsal.
4. Build/package the exact accepted source, rehearse migrations and reviewed catalog/application/page changes against copied state, and verify retained state and application readiness.
5. Apply the proven operations to the live database, activate the intended application/state-space/page revisions where necessary, select the new pinned runtime release, and restart only its identified managed process.
6. Verify the normal launcher, actual site/API/MCP readiness, navigation, theme asset, DND page, and at least one real authorized read. Preserve existing external access settings; do not expand them.
7. Record exact release/source/database/backup paths, selected revisions, results, known limits, and recovery steps. Keep previous release and matching backup. Remove only proven disposable task artifacts; never remove active release directories, blobs, work, or needed toolchains.

## Current checkpoint

Combined implementation is accepted at `0a445b54d4f78b98332b789adfbb7ed2833f2e25`: full suite 3,456 passed, zero failed/skipped; opt-in protocol eight passed with two intentional retired cases skipped; build and browser/catalog checks passed. It was merged onto actual master at `ef73bab1a84ee11abf002f7c8d496500b04f1c4c`, with all non-documentation files matching the accepted implementation.

Local branch cleanup is already complete: only master exists; remote refs, both stashes, and all worktree directories were preserved. Read-only deployment inventory is assigned. No live migration, import, page publication, or release selection for this upgrade has happened yet.

Next: record the resolved live profile and a concrete backup/rehearsal/deployment sequence, then have the coordinator hand exclusive write ownership to the deployment worker. Workers may update this checkpoint in the original checkout but must not stage or commit there; the coordinator serializes Git changes.

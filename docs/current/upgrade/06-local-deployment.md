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

Local branch cleanup is already complete: only master exists; remote refs, both stashes, and all worktree directories were preserved. Root granted agent `website_integration` exclusive deployment write ownership. No live migration, import, page publication, or release selection for this upgrade has happened yet.

The byte-verified saved profile is `DantesRoleplay.MCPServer/data/runtime-launch.json` (schema 1, SHA-256 `F13DD997E09301C44462F8800D08C803BABF1F4D7C37F657F886FF585EC1B765`). It selects the live database `DantesRoleplay.MCPServer/data/dantesroleplay.db`, sibling `blobs`, application `dnd2024`, port 6217, and its existing localhost/public origins and anonymous-public policy. Its receipt points to an older host/profile; no MCP server process or port-6217 listener is running. The database passed integrity and foreign-key checks. Kernel schema has 75 migrations through `20260911000100_ApplicationEventSourceContext` with ten pending; web schema is through `20260907023758_DurableWebAssetContent` with two pending. DND is application revision 2 / activation 65; legacy `dnd2024-play` is active/latest revision 77, `home` 8, and `control-center` 10.

Recovery root `DantesRoleplay.MCPServer/data/backups/platform-upgrade-20260912T113843Z` contains the verified 91,033,600-byte online backup (SHA-256 `46BA82A3A765D1D2C413F40BD1A283F906CD2AFDD3452A8BFF5FDA3372DB0BEE`), a full 603-record live catalog export, and a byte-verified copy of all 152 blobs. Rehearsal uses a separate database/blob copy beneath that root. Exact-master deployables are `DantesRoleplay.MCPServer/data/releases/platform-0a445b54-20260912-host` and `...-source`; the host includes shared BrowserComponents and the source includes the rebuilt DND website.

Initial rehearsal catalog comparison preserves two live-authored item edits and three live-only records, adds 18 new platform manuals, updates `system.web.page`, and reports one `procedure.system.use` conflict. Resolve that manual conflict from the retained export without overwriting live item deviations, then migrate/import only the rehearsal copy, start it on a non-live port, perform the reviewed page-identity migration and DND bundle CAS publication, and verify readiness/readback before repeating the proven mutations on live state. Workers may update this checkpoint in the original checkout but must not stage or commit there; the coordinator serializes Git changes.

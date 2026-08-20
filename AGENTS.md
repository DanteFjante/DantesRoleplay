# Repository development workflow

This file governs agents that can edit this checkout. Runtime agents connected only through MCP
follow the procedure contracts stored in the running database instead.

## Authority

- Repository files are authoritative for C#, JavaScript mechanics, procedure contracts, component
  definitions, event types, subscriptions, schemas, and catalog fixtures during development.
- SQLite is authoritative for a running game's campaigns, world state, events, notifications,
  operation history, and content authored in an MCP-only session.
- Do not author the same catalog record in files and the live database concurrently. Export live
  changes before editing their files; import reviewed files only at an explicit synchronization
  boundary.
- `catalog/` is the single authored catalog. Core contracts are embedded from it at build time;
  do not recreate `DantesRoleplay/Bootstrap/` copies.

## Development loop

1. Inspect the relevant code and search `catalog/` for an existing owner before creating an id.
2. Read only the relevant contract files. Filesystem edits do not require MCP `orient`, contract
   citations, dry-run commits, query-back calls, or operation IDs.
3. Plan proportionally. Write a dependency plan for cross-subsystem or multi-slice work; a small
   change needs only a clear boundary, tests, and an exit condition.
4. Implement one coherent reviewable slice. Keep stable ids, schemas, mechanical results, and
   failure behavior explicit.
5. Run focused tests while iterating. Run `roleplay validate catalog` after catalog changes; it
   imports a disposable copy into a fresh migrated database and runs the write-side checks without
   touching the live database.
6. Run the full suite once at feature acceptance, and the protocol walk only when the MCP surface
   or its dependency registration changed. Run `roleplay import catalog` against the persistent
   database only when preparing it for integration play or release.

## Quality gates

- Confirmation is required at semantic boundaries: new permanent ids, schema meaning changes,
  migrations, public surface changes, destructive operations, or a completed feature. Routine
  edits inside an approved boundary do not require a pause after every file or dependency leaf.
- Tests replace repeated manual confirmation only when they assert the same invariant. Never
  remove a safety check merely to reduce calls.
- Keep plans prospective and concise. Put durable behavior in contracts and tests; put completed
  evidence in a short receipt when one is useful, not back into every plan and contract.

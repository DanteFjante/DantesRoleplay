# Repository working agreement

This file governs agents editing this checkout.

## Start here

Read this file and [docs/current/README.md](docs/current/README.md). Then follow that page's task routing and read only the one additional current document needed for the work.

Do not preload planning history, generated evidence, world-building workspaces, media, or rulebook PDFs. `_to_delete/` is quarantined historical material; do not search, read, cite, or use it unless the user explicitly asks to review it.

## Authority and placement

- `catalog/` is the authored development catalog. Its procedures, schemas, fixtures, and JavaScript mechanics are authoritative during development.
- SQLite is authoritative for a running game's campaigns, world state, events, notifications, operation history, and MCP-only authored content.
- Export live database changes before editing the same records in files. Import reviewed files only at an explicit synchronization boundary.
- **C# is the generic kernel.** It may store and version records, materialize declared context, sandbox JavaScript, validate generic envelopes, apply typed effects, transact, audit, retrieve, and expose protocol operations.
- **JavaScript catalog mechanics own game rules.** Rule calculations, game-specific eligibility, outcomes, and rule branching belong under `catalog/mechanics/` or the relevant catalog application.
- C# must not contain game-specific IDs, rule vocabulary, formulas, or special-case outcome logic. A generic host-safety invariant is the exception.
- Catalog procedure Markdown explains capabilities; component JSON Schemas own state shape; catalog JSON owns authored fixtures. Documentation and UI state are never game-state authority.

## Development loop

1. Find the existing owner in code, `catalog/`, and focused tests before adding an ID or abstraction.
2. Read only the relevant current guide plus the exact implementation files and contracts involved.
3. Make one coherent change. Keep ruleset behavior in catalog data/JavaScript and infrastructure behavior in the generic C# host.
4. Run focused tests while iterating. After catalog changes, run `roleplay validate catalog`; it uses a disposable database and does not touch the live database.
5. Run the full suite for feature acceptance. Run the protocol walk only when the MCP surface or dependency registration changed.

Keep temporary plans in the task or issue. Do not create permanent implementation plans, dependency trees, handoffs, receipts, or status diaries unless the user explicitly asks for a durable document.

## Confirmation and evidence

Confirmation is required for new permanent IDs, schema-meaning changes, migrations, public-surface changes, destructive operations, cross-owner semantic changes, and completed feature acceptance. Routine edits inside an approved boundary do not need repeated pauses.

Tests may replace manual confirmation only when they assert the same invariant. Report the delivered boundary, commands/results, and deliberate exclusions in the task response rather than creating a receipt by default.

## Documentation

- `docs/current/` is the only LLM-facing project documentation.
- Keep its entry page short and route readers to one topic guide.
- `docs/world/` contains optional world-building workspaces and media; `docs/pdfs/` contains source references. Read them only for a task about that world or source.
- Update a current guide when a durable architectural or operational rule changes. Implementation history belongs in version control.

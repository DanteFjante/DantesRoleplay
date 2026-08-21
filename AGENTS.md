# Repository working agreement

This file governs agents editing this checkout. Runtime-only MCP agents follow the procedure
contracts stored in the running database.

## Read boundary

Read this file, then follow [the implementation-document reading protocol](docs/IMPLEMENTATION_DOCUMENT_READING.md).
Read only the catalog owners and one active plan needed for the task. Use receipts to verify a
prerequisite, not as general background. Do not load every roadmap, feature plan, handoff, or
receipt.

## Authority and placement

- `catalog/` is the single authored catalog. Its procedures, schemas, fixtures, and JavaScript
  mechanics are authoritative during development; do not recreate bootstrap copies.
- SQLite is authoritative for a running game's campaigns, world state, events, notifications,
  operation history, and MCP-only authored content.
- Export live database changes before editing the same records in files. Import reviewed files only
  at an explicit synchronization boundary.
- **C# is the generic kernel.** It may store/version records, materialize declared context, sandbox
  JavaScript, validate generic envelopes, apply typed effects, transact, audit, retrieve, and expose
  protocol operations.
- **JavaScript catalog mechanics own game rules.** Rule calculations, game-specific eligibility,
  outcomes, and rule branching belong under `catalog/mechanics/`.
- C# must not contain game-specific IDs, rule vocabulary, formulas, or special-case outcome logic.
  If behavior could vary by ruleset or campaign, keep it in catalog data/JavaScript. A generic host
  safety invariant is the exception.
- Catalog procedure Markdown explains how capabilities are used; component JSON Schemas own state
  shape; catalog JSON owns authored fixtures. The UI and planning documents are never game-state
  authority.

## Development loop

1. Search code and `catalog/` for the existing owner before creating an ID.
2. Read the relevant contracts and one current plan. Treat plans as prospective; treat code,
   catalog records, tests, and receipts as implementation evidence.
3. For cross-subsystem work, author the dependency tree using
   [DEPENDENCY_TREE_AUTHORING.md](docs/DEPENDENCY_TREE_AUTHORING.md). For a small change, state the
   same boundary directly in its implementation document.
4. Before feature work, author one active slice using
   [FEATURE_IMPLEMENTATION_AUTHORING.md](docs/FEATURE_IMPLEMENTATION_AUTHORING.md). Implement only
   that coherent slice. Keep ruleset alignment, IDs, schemas, derived inputs, effects, failure
   behavior, and transaction ownership explicit.
5. Run focused tests while iterating. After catalog changes run `roleplay validate catalog`, which
   uses a fresh disposable database and does not touch the live database.
6. Run the full suite at feature acceptance. Run the protocol walk only when the MCP surface or its
   dependency registration changed.

## Confirmation and evidence

Confirmation is required for new permanent IDs, schema-meaning changes, migrations, public-surface
changes, destructive operations, cross-owner semantic changes, and completed feature acceptance.
Routine edits inside an approved boundary do not need repeated pauses.

Tests may replace manual confirmation only when they assert the same invariant. A completion receipt
records the delivered boundary, commands/results, and deliberate exclusions; do not copy the whole
plan into it.

## Document lifecycle

- One roadmap owns each subsystem; `STATUS.md` is only a compact cross-system summary.
- A feature plan describes remaining work. Remove completed prose once receipts and authoritative
  contracts preserve the result.
- Receipts, confirmations, validations, and ratifications are durable evidence and are not deleted
  during routine cleanup.
- `KNOWN_ISSUES.md` contains only current reproducible problems, their evidence, owner, and close
  condition. Resolved history belongs in the fixing receipt or version control.

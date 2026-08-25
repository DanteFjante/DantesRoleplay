# Application kernel Slice 1 receipt — read-only legacy inventory

Status: **accepted**  
Completed: 2026-08-23  
Delivered report: [Legacy application-kernel inventory](../inventory/LEGACY-APPLICATION-KERNEL-INVENTORY.json)

## Delivered

- One deterministic machine-readable inventory of current catalog component definitions, fixtures,
  mechanics, procedures, event types, subscriptions, catalog/source seams, and the MCP public
  surface.
- Complete coverage of 109 in-scope authored IDs: 39 directly evidenced generic-system records and
  70 unresolved records. No record was assigned to `dnd2024` or another application without direct
  evidence.
- The current component boundary: unscoped mutable definitions, no schema enforcement, object-only
  values, 32 definitions with schema sidecars, and one unqualified `stats` definition without one.
- Legacy catalog integrity findings: 127 manifest records versus 144 observed authored records; 10
  stale manifest procedure paths and 18 in-scope authored records omitted from the manifest.
- The existing flat public surface: three verbs, 22 query kinds, and 17 commit kinds, with their
  governing procedure evidence and no invented aliases.
- The distinction between the legacy `catalog/` authored root and the standalone local-AI
  file/directory/glob scanner. Neither is an application/source registry today.

## Evidence

- Inventory SHA-256:
  `BD2157F577E8C75330A0491D3C66D1C87057802D8A87B3A344E24B159A53BB4D`.
- Report JSON parsed successfully.
- Coverage verification found exactly one owner-coverage entry for every in-scope authored record:
  109 total (39 system, 70 unresolved).
- Public-surface verification found all `VerbSurface` kinds: 22 queries and 17 commits.
- Catalog manifest fingerprint matched the report snapshot.
- `roleplay validate catalog` passed against a disposable database: 144 records valid, 17 advisory
  near-duplicate warnings, and no live data touched.
- `git diff --check` passed for Slice 1 paths; Git emitted only line-ending notices for existing
  modified shared files.

## Deliberate exclusions

No runtime code, catalog content, database, source/application/state-space registration, schema,
migration, alias, public kind, projection, or AI behavior changed. The inventory does not decide
the owner of any `game.core.*` record, unqualified `stats`, generic executable mechanics, or
game-workflow procedure.

## Next gate

Before Slice 2 can turn this report into pure contracts and validators, an authorized decision must
assign each unresolved record to an explicit non-system application, retirement, or migration
outcome. Reconcile manifest path/coverage findings before treating it as an effective application
manifest. Alias mapping remains a separate compatibility/public-surface gate.

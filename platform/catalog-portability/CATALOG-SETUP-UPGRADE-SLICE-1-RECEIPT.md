# Catalog setup/upgrade Slice 1 receipt — reconstructable runtime database

Status: accepted
Date: 2026-08-30
Ruleset alignment: ruleset-neutral

## Delivered boundary

- Added `roleplay setup <catalog-directory> [--database <path>]` to create a missing SQLite file,
  apply EF migrations, validate and import the complete portable catalog, and verify agreement.
- Added `roleplay upgrade <catalog-directory> [--database <path>]` to require an existing database,
  create a consistent timestamped backup, migrate, apply filesystem authority, verify agreement,
  and restore the backup automatically if the operation fails.
- Added missing-target resolution without weakening the existing runtime database precedence.
- Kept runtime databases and generated upgrade backups out of source control.
- Made Markdown export deterministic at exactly one trailing newline.

## Filesystem recovery boundary

- The active generic catalog now validates as 355 records: 14 mechanics, 54 procedures,
  39 component definitions, 10 event types, 2 subscriptions, and 236 entities.
- 197 compatible live world entities and the compatible relationship union were added to the
  active `catalog/` tree.
- The complete portable live database export (346 records plus relationships) is retained at
  `ruleset/dnd2024/evidence/state-exports/2026-08-30-live-database-portability-export/`.
- Seven legacy/application-owned entities remain in that evidence export rather than creating
  duplicate D&D or campaign component owners in the generic catalog.
- Operation history and other runtime-only tables remain deliberately outside filesystem import.

## Verification

- `roleplay validate catalog`: passed, 355 records, 0 warnings.
- Fresh `roleplay setup catalog`: passed; created, migrated, and imported 355 records plus the
  relationship set.
- Immediate `roleplay upgrade catalog`: passed; created a backup and reported that the database
  already matched the catalog.
- `roleplay verify catalog`: passed with 356 unchanged entries and no differences.
- `CatalogDatabaseLifecycleTests` plus `CatalogExportTests`: 17/17 passed.
- `CatalogValidationTests`: 4/4 passed.
- `ComponentTypeAdministrationTests`: 6/6 passed.
- Focused total: 27/27 passed; build completed with 0 warnings and 0 errors.

## Full-suite baseline exclusion

The full suite was started after the focused gates. It reproduces unrelated pre-existing D&D 2024
failures, including a missing
`catalog/applications/dnd2024/content/entities/character-creation/rest/` fixture and the existing
weapon-damage contract assertion. The run was stopped after those baseline failures recurred because
the missing fixture causes a long cascade. The catalog portability, generic catalog-count, setup,
upgrade, export, and component-owner gates above all pass.

## Deliberate exclusions

- No database operation-history export or replay.
- No new component schema, permanent catalog ID, migration, game rule, or MCP surface.
- No automatic merge of live edits during upgrade; reviewed filesystem records remain authoritative.

# Catalog setup/upgrade dependency tree — reconstructable runtime database

Status: delivered
Ruleset alignment: ruleset-neutral
Source: not applicable

## Outcome and non-goals

A checkout can create a new migrated SQLite database from the complete filesystem catalog, or
upgrade an existing SQLite database from that catalog after preserving a backup. The live database
file, operation history, generated backups, and automatic conflict merging are not repository
artifacts.

## Existing owners and evidence

| Concern | Owner | State | Evidence |
| --- | --- | --- | --- |
| Catalog file format and full fresh import | `catalog` | verified | `CATALOG_HANDOVER.md`, `CatalogValidator`, `CatalogImporter` tests |
| Developer command surface | `catalog-tools` | verified | `CommandLine`, `ITool`, `Program`, existing import/export/validate/verify tools |
| SQLite schema evolution | `sqlite-hosting` / `DantesRoleplayDbContext` | verified | EF Core migrations and host `InitialiseDantesRoleplayAsync` |
| Runtime database location | `catalog-tools` | verified | `DatabaseLocator` and `DANTESROLEPLAY_DB` precedence |
| Current live-only authored records | running SQLite database | ready for export | `roleplay import catalog --dry-run` reports database-only records |

## Dependency tree

```text
Reconstructable runtime database [delivered]
├─ Complete reviewed filesystem catalog [delivered]
│  ├─ Export all portable live records as dated evidence [delivered]
│  └─ Merge generic live world records with file-authored records [delivered]
├─ Fresh setup command [delivered]
│  ├─ Resolve a target path that may not exist [delivered]
│  ├─ Apply EF migrations [verified]
│  └─ Import the complete catalog transactionally [verified]
└─ Existing-database upgrade command [delivered]
   ├─ Require an existing target [verified]
   ├─ Preserve a pre-upgrade backup [verified]
   ├─ Apply EF migrations [verified]
   └─ Apply reviewed filesystem records as authority [verified]
```

## Conflicts and decisions

- The user confirmed the new public developer commands `setup` and `upgrade`.
- The filesystem catalog is authoritative during setup/upgrade. Live-only records are exported
  before this boundary so applying filesystem authority does not discard them.
- `setup` refuses an existing database; `upgrade` refuses a missing database.
- Operation history is not imported from files and remains deliberately outside portable catalog
  state.

## Ordered leaves

| Order | Leaf | Depends on | Exit gate |
| --- | --- | --- | --- |
| 1 | Setup/upgrade orchestration | existing importer and migrations | focused command tests pass |
| 2 | Complete catalog synchronization | leaf 1 behavior and current live copy | fresh validation and clean round-trip |
| 3 | Runtime database exclusion | leaf 2 | database is ignored and no oversized blob remains in unpushed history |

## Delivered leaf

Ruleset-neutral CLI orchestration now wraps existing EF migrations and `CatalogImporter` without
changing catalog schemas, game rules, MCP verbs, or import record semantics.

## Confirmation gates

The user explicitly requested both command names, filesystem authority for new content, and removal
of the runtime database as a repository dependency. No schema-meaning or ruleset confirmation is
required.

## Planning receipt

- Runtime artifacts created: none.

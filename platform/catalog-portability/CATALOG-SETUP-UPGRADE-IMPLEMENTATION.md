# Catalog setup/upgrade Slice 1 implementation — reconstructable runtime database

Status: accepted (scoped gates passed; unrelated full-suite baseline failures recorded in receipt)
Owner/roadmap: Catalog portability (`CATALOG_HANDOVER.md`)
Dependency tree/leaf: `CATALOG-SETUP-UPGRADE-DEPENDENCY-PLAN.md` / setup-upgrade orchestration
Ruleset alignment: ruleset-neutral
Source ID and locator: not applicable
Outcome: create or upgrade the runtime SQLite database entirely from reviewed repository files
Exclusions: operation-history import, automatic conflict merging, MCP surface changes, game rules
Allowed files/areas: `src/system/catalog-tools`, catalog-focused tests, `catalog/`, `.gitignore`,
catalog portability documentation and completion evidence
Stop point: commands pass focused tests, the full catalog imports into a fresh database, the current
database content is represented in `catalog/`, and the runtime database is absent from Git history

## Confirmed decisions

- Public developer commands are named `setup` and `upgrade` as explicitly requested by the user.
- `setup` refuses to overwrite an existing database.
- `upgrade` requires an existing database, creates a pre-upgrade backup, migrates the schema, and
  treats reviewed filesystem catalog records as authoritative.
- The current live database is exported in full to dated evidence. Records whose component owners
  are already part of the generic catalog are also merged into `catalog/`; legacy application-owned
  rows stay in the evidence export instead of creating duplicate generic owners. The original live
  database is not used as a merge scratchpad.

## D&D 5e 2024 alignment

Not applicable. This slice is generic SQLite/catalog orchestration and introduces no D&D meaning.

## External implementation reference

No Foundry reference is relevant because this slice does not implement a game rule.

## Prerequisite evidence

- `CATALOG_HANDOVER.md` defines catalog files as development authority and import/export as the
  explicit live synchronization boundary.
- `CatalogValidator` already proves migration plus full import against a disposable database.
- `CatalogImporter` already owns atomic record import and explicit `CatalogForce.Files` behavior.
- EF Core migrations already own database schema evolution.

## Runtime artifacts

- New developer CLI tools: `setup` and `upgrade`.
- No new catalog record IDs, component schemas, migrations, MCP kinds, or runtime protocol verbs.

## Authoritative state and closed input

- Input is a catalog directory and optional explicit SQLite path.
- Catalog records are authoritative during setup/upgrade.
- Database schema is authoritative in EF migrations.
- Callers cannot supply record hashes, versions, derived state, or partial record selections.

## Behavior, result, and typed effects

- `setup`: resolve a target path, refuse if it exists, create its parent, migrate a new SQLite file,
  import every catalog record, verify agreement, and remove the newly created database if setup fails.
- `upgrade`: resolve an existing target, create a timestamped sibling backup, migrate, import every
  filesystem change with files authoritative, verify agreement, and report the backup path.
- Import remains one database transaction under `CatalogImporter`; migrations remain EF-owned.

## Failure, replay, and rollback contract

- Missing catalog, contradictory path state, invalid catalog, migration failure, or import failure
  exits nonzero.
- Setup never overwrites an existing file and cleans up only the file it created on failure.
- Upgrade never creates a missing target and preserves the pre-upgrade file for recovery.
- Re-running setup against the same path refuses; re-running upgrade is idempotent once catalog and
  database agree, while still preserving a new pre-run backup.

## Implementation sequence

1. Add shared database/catalog synchronization orchestration and command registration.
2. Add focused command tests for create/refuse/upgrade/backup/fresh-import behavior.
3. Preserve a full live export, merge compatible live world content with current filesystem files
   in a copied database, then export the canonical generic catalog.
4. Validate the resulting catalog and run the full suite.
5. Record a receipt, remove the runtime database from unpushed Git history, and push.

## Acceptance matrix

| Case | Expected evidence |
| --- | --- |
| Missing database + valid catalog | setup creates, migrates, imports, and verifies |
| Existing setup target | refusal with no file change |
| Existing database + new file record | upgrade backs up, applies, and verifies |
| Missing upgrade target | refusal and no database creation |
| Invalid/conflicting catalog | nonzero; setup-created target removed or upgrade backup retained |
| Replay | subsequent upgrade reports agreement and remains valid |
| Fresh import | `roleplay validate catalog` succeeds |
| Surface | MCP remains exactly `orient`, `query`, `commit` |

## Verification commands

```powershell
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --filter CatalogDatabaseLifecycleTests
.\roleplay validate catalog
dotnet test
git diff --check
```

## Completion receipt and exit gate

Completion evidence is recorded in `CATALOG-SETUP-UPGRADE-SLICE-1-RECEIPT.md`. Stop without adding
operation-history portability or deployment automation.

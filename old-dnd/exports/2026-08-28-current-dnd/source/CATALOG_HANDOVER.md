# Catalog portability handover

Status: **Implemented development workflow; durable handover and acceptance summary**
Last reviewed: 2026-08-21

## Purpose

Repository developers author the catalog as readable files; MCP-only runtime agents author the
running SQLite database. Export/import is the explicit bridge. Neither side silently overwrites the
other, and the persistent database is never imported during ordinary feature validation.

This is a developer CLI capability, not a fourth MCP tool.

## Accepted behavior

- Catalog records have stable content fingerprints.
- Export writes deterministic category paths, JSON records, Markdown contracts, JavaScript mechanic
  source, schemas, and `catalog/manifest.json`.
- Import validates the catalog in dependency order and detects file/database drift.
- `roleplay validate catalog` migrates a fresh disposable database, imports the catalog, and runs
  write-side checks without touching the persistent database.
- World records and relationships round-trip into an empty database.
- History can be exported for review without making it authored catalog state.

The catalog's current record count and suite count are intentionally not pinned here. Read the last
validation run; both grow as features land.

## Authority and synchronization

| Situation | Authority | Required action |
| --- | --- | --- |
| Normal repository development | `catalog/` | Edit files, validate in a disposable database. |
| Running game | SQLite | Do not overwrite it from a development checkout. |
| MCP-only authored record that must enter source | SQLite until export | Export first, review the files, then continue file-first. |
| File and database both changed | Neither automatically | Stop, inspect drift, choose the intended side explicitly. |
| Integration play or release | Reviewed repository plus chosen persistent database | Snapshot/backup, import, verify, then start play. |

Do not author the same catalog record in files and the live database concurrently.

## File model

- `catalog/manifest.json` inventories exported records and hashes.
- Procedures and mechanic declarations use Markdown with front matter.
- Executable mechanic source is a neighboring `.js` file, not embedded in planning prose.
- Component definitions and authored entities/relationships use JSON; schemas remain separate when
  they are independently validated or reused.
- `catalog/` is the only authored copy. Embedded bootstrap contracts are built from it.

The mechanic declaration and source are one versioned record. A source-only change therefore
changes the record fingerprint and must append/import as a new version under the normal policy.

## Commands

```powershell
.\roleplay validate catalog
.\roleplay export catalog
.\roleplay import catalog
.\roleplay verify catalog
```

Use `validate` while developing. Use `import` against the persistent database only at an explicit
integration/release boundary, after a backup and drift review.

## Load-bearing decisions

1. **Three-way drift is an error, not a winner-selection algorithm.** File-only, database-only, and
   both-changed states must be visible.
2. **Stable IDs and append-only versions survive transport.** Import does not rename records or
   rewrite history to make a diff disappear.
3. **Hashes use canonical record content.** Formatting noise must not create semantic versions, but
   executable/source changes must.
4. **Validation uses an empty database.** Round-tripping against the source database cannot reveal
   missing dependency ordering or relationship import defects.
5. **Export is deterministic.** Repeating it without state changes should not rewrite files.
6. **No persistent import in feature tests.** Feature acceptance proves the repository catalog in a
   disposable database first.

## Practical traps

- Build/test output redirected into the repository can be indexed by tooling and should not be
  committed.
- A running MCP server may hold build outputs. Stop/restart it when the build cannot replace a DLL;
  do not use stronger deletion commands.
- A stale `.git/index.lock` must be investigated before removal; confirm no Git process owns it.
- JSON/schema APIs may accept different node/element types than expected. Keep import tests at the
  actual serializer boundary.
- Test world relationships in a genuinely empty database. A simulation against the exporting
  database can hide missing edges.
- Never trust raw database row counts as live catalog coverage when tombstones/version history are
  present.

## Verification

For a catalog change:

1. Run focused tests for the changed reader/writer or feature.
2. Run `.\roleplay validate catalog`.
3. At feature acceptance run the full suite.
4. If preparing integration play, back up the persistent database, run import, then verify.

Acceptance should cover deterministic re-export, fresh-database import, dependency ordering,
relationship round-trip, append-only version behavior, drift rejection, malformed input, and no
partial writes.

## Deliberate exclusions

- Automatic conflict merging.
- Persistent database import during normal tests.
- MCP access to import/export.
- Treating operation history as authored catalog content.
- A Node-only mechanic runtime or test harness as part of portability itself.

Readable mechanic refactoring and a reusable JavaScript prelude are separate features. They must not
move rule behavior into C#.

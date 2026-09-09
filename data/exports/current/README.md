# Current complete database export

This is a complete, point-in-time export of the configured MCP server database,
including committed SQLite journal state and external blobs. A consistent SQLite
snapshot was captured in memory while the server was running. Export does not
change the live database or the authored `catalog/`.

Source: `DantesRoleplay.MCPServer/data/dantesroleplay.db`. The separate
`data/dantesroleplay.db` was not used for this capture.
The precise capture timestamp and source snapshot hash are in `manifest.json`.

## Contents

- All 150 tables and 205,184 rows, including deleted records, retained versions,
  SQLite statistics/sequences and full-text-search storage.
- The exact schema, including 338 indexes and 513 triggers.
- Current ECS data: 2,879 entity rows, 5,254 component rows and 5,300 relationship
  rows across all eight state spaces. These counts include retained/deleted rows.
- Application registrations, sources, activation history and state-space bindings.
- All 5,803 operations, 1,210 events, stored conversations and other runtime records.
- Three stored websites, 70 page revisions, all 639 asset rows and 225 deduplicated asset contents.
- All 91 external blob files, with their original paths and exact bytes.
- The earlier 6 September catalog comparison under `catalog/`. It is historical
  comparison material; the current database is captured by `tables/` and `schema.json`.
  The repository's authored `catalog/` remains separate and includes the newer
  object definitions and other authored features pulled from Git.

All persisted tables are captured by schema discovery, including generic ECS
objects, components, relationships and feature-specific storage. This capture
includes the applied database migrations, D&D activation revision 52, the upgraded main state-space binding, the 12 newer component registrations,
the two unchanged item quantities upgraded to schema version 2, and website
revision 52. The export retains the database's actual migration history. New
migrations and authored feature files in the repository are not silently applied to the source database during export.

`tables/*.jsonl` stores ordered rows as arrays. Column names, SELECT order, row
counts and logical row hashes are in `manifest.json`. Large text and BLOB cells
reference `objects/` entries by SHA-256; identical content is stored once. The
manifest maps external blob paths to those objects. No exported file exceeds
8 MiB. `.gitattributes` preserves exact bytes through Git checkout on Windows.

## Restore into a new directory

With Python 3.11+ and SQLite supporting FTS5 and JSON:

```powershell
python data/exports/current/restore_snapshot.py C:/restore/dantesroleplay
```

The destination must not exist. The script verifies file hashes, restores
`dantesroleplay.db` and `blobs/`, then checks the exact schema, every table's row
content/count, source integrity findings, foreign keys and blob hashes. It never
overwrites a live database. The SQLite version used for capture is in the manifest.

Stored application source registrations retain their original paths. Starting a
restored server in a different checkout still requires the normal source/binding
configuration; this export does not silently rewrite activation evidence.

## Discrepancies preserved

The existing catalog comparison reports two database-edited items, 33 file-only
records and three database-only records; see `catalog-drift.txt`. The two item
differences are `stats` in the database versus `fixture.legacy.stats` in the
authored files. Their item content otherwise matches. The three database-only
records are `lock`, `stats` and `f5-natural-probe`. The corrected authored files
and the 33 file-only records were preserved rather than overwritten.

The source also contains the reserved `system` application row while retaining
`CK_system_application_id`, which forbids inserting that ID. Full CHECK validation
on a read-write SQLite connection reports `CHECK constraint failed in
system_application`; read-only integrity inspection reports `ok`. Structural
integrity passes and there are no foreign-key violations. The restore temporarily
disables CHECK enforcement only while replaying captured rows, then reenables it
and verifies the same source finding. It preserves this historical inconsistency
without deleting the row or weakening the stored schema.

The restore rehearsal matches every schema object and all captured rows (including
row IDs and stored types/values), all external blob bytes and the original
integrity/foreign-key results. This is a faithful capture of the repaired runtime
after explicit synchronization. The earlier authored-catalog validation reported
565 records and seven existing legacy capability warnings.

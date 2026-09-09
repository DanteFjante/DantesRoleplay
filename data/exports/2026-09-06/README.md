# Database capture — 6 September 2026

This is a complete, point-in-time export of the configured MCP server database,
including its SQLite journal and external blobs. The server was stopped during
capture. The source database and authored `catalog/` were not changed.

Source: `DantesRoleplay.MCPServer/data/dantesroleplay.db`. The separate tracked
`data/dantesroleplay.db` is an older database and was not used for this capture.
The precise capture timestamp and source snapshot hash are in `manifest.json`.

## Contents

- All 143 tables and 189,573 rows, including deleted records, retained versions,
  SQLite statistics/sequences and full-text-search storage.
- The exact schema, including 324 indexes and 102 triggers.
- Current ECS data: 2,879 entity rows, 5,254 component rows and 5,300 relationship
  rows across all eight state spaces. These counts include retained/deleted rows.
- Application registrations, sources, activation history and state-space bindings.
- All 5,772 operations, 1,210 events, stored conversations and other runtime records.
- Three stored websites, 69 page revisions and all 591 asset rows.
- All 91 external blob files, with their original paths and exact bytes.
- The existing catalog export, including operation history, under `catalog/`.
  This is a database-side export for comparison, separate from the repository's
  authored catalog. It is not an instruction to import it over current files.

`tables/*.jsonl` stores ordered rows as arrays. Column names, SELECT order, row
counts and logical row hashes are in `manifest.json`. Large text and BLOB cells
reference `objects/` entries by SHA-256; identical content is stored once. The
manifest maps external blob paths to those objects. No exported file exceeds
8 MiB. `.gitattributes` preserves exact bytes through Git checkout on Windows.

## Restore into a new directory

With Python 3.11+ and SQLite supporting FTS5 and JSON:

```powershell
python data/exports/2026-09-06/restore_snapshot.py C:/restore/dantesroleplay-20260906
```

The destination must not exist. The script verifies file hashes, restores
`dantesroleplay.db` and `blobs/`, then checks the exact schema, every table's row
content/count, source integrity findings, foreign keys and blob hashes. It never
overwrites a live database. Restoration was verified with SQLite 3.53.1.

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

The restore rehearsal matched every schema object, all 189,573 rows (including
row IDs and stored types/values), all external blob bytes and the original
integrity/foreign-key results. This is a faithful capture, not a database repair
or a new catalog activation. The authored catalog separately validates with
565 records and seven existing legacy capability warnings.

# Catalog and live database

The repository catalog and a running SQLite database have different authority. Treat synchronization as an explicit, reviewed operation.

## Authority

- `catalog/` owns authored development procedures, schemas, fixtures, applications, and JavaScript mechanics.
- SQLite owns a running game's campaigns, world state, events, notifications, operation history, and records authored through MCP.
- Stable IDs connect the two. File location alone does not change a record's authority.

Never reconstruct live changes from memory. Export them before editing the same records in the authored catalog.

## Safe commands

```powershell
.\roleplay.cmd validate catalog
.\roleplay.cmd verify catalog
.\roleplay.cmd help export
.\roleplay.cmd help import
```

- `validate catalog` loads the authored catalog into a disposable database and validates it without touching the live database.
- `verify catalog` compares file and database records and exits unsuccessfully when drift exists.
- Use `roleplay help <command>` for help. Appending `--help` after an export target is not the documented help form.

## Export workflow

Export the live database to a disposable, ignored review directory, not directly over `catalog/`:

```powershell
.\roleplay.cmd export <review-directory> --database <database-path>
```

Then compare records by stable ID and content hash. Merge database-only or database-newer records deliberately while preserving intentional file-only changes. Validate the reviewed catalog before removing the temporary export.

Export overwrites matching files in its destination but does not delete extra destination files or modify the source database. That makes a clean review directory important.

## Import workflow

Preview first:

```powershell
.\roleplay.cmd import catalog --database <database-path> --dry-run
```

Import only at an explicit synchronization boundary. Resolve conflicts rather than selecting `--force-files` or `--force-db` casually. Back up or otherwise protect a material live database before a persistent import.

## Catalog content boundary

Procedures describe callable capabilities, component schemas define stored state, fixtures define authored records, and JavaScript mechanics implement game-specific rules. C# hosts and validates those records; it does not duplicate their game semantics.

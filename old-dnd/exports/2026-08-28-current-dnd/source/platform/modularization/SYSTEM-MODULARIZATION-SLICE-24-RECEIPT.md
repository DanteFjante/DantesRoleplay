# System modularization Slice 24 receipt — stale game-adapter unlink

Status: **accepted**  
Completed: 2026-08-23

## Delivered

- Removed the direct Campaign, Character, Quest, Story, Knowledge, Journey, and Itinerary CLR
  imports, registrations, workers, and dispatcher routes from the generic data-access/MCP host.
- Replaced the runtime protocol catalog and commit dispatcher with generic component, effect,
  mechanic, and action capabilities. Retained legacy MCP helper files are explicitly excluded from
  the generic host project rather than deleted.
- Removed Story-specific action transaction participation. `ActionRunner` now owns only the generic
  action transaction.
- Replaced typed Story-plan EF access with an untyped retention mapping of the existing tables. The
  migration history remains valid; the generic host has no service that reads or writes those
  records.
- Excluded retained game-adapter tests from the generic test assembly and restored component
  manifests for the new application-kernel directories. The application host no longer declares a
  dependency on a game-adapter component.

## Evidence

- Solution build: passed with 0 warnings and 0 errors.
- Application-kernel focused suite: 6 passed, 0 failed.
- Migration drift and catalog-coverage suite: 7 passed, 0 failed.
- Architecture guard suite: 13 passed, 0 failed.
- Shared generic suite: 461 passed, 0 failed.
- `git diff --check`: passed (line-ending notices only).

## Deliberate exclusions

- No game rule was reimplemented, migrated, deleted, or changed.
- No database migration was created, applied, or removed.
- Retained legacy source and tests remain on disk for a future application-adapter/catalog
  replacement slice; they are not compiled into the generic host.
- The retained EF entity names are historical migration metadata only, not CLR or service
  dependencies.

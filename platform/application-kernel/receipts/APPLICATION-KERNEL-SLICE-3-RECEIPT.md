# Application kernel Slice 3 receipt — application and source registry persistence

Status: **accepted**  
Completed: 2026-08-23

## Delivered

- Added additive SQLite persistence for immutable generic application registrations/revisions/base
  relationships, source registrations, and append-only source-scan receipts.
- Added component-owned SQLite adapters and dependency-injection registrations for the existing
  application/source registry ports; no MCP kind or host auto-registration was introduced.
- Added the `ApplicationSourceRegistry` EF migration and model snapshot. Existing database tables
  are untouched by the forward migration. The migration refuses an automatic downgrade so immutable
  registry evidence cannot be silently deleted; recovery uses a database backup.
- Declared all five registry tables as live runtime configuration/evidence that catalog import and
  export must not recreate or overwrite.

## Evidence

- Focused application-registry, migration, and catalog-coverage tests: 10 passed, 0 failed.
- Solution build: passed with 0 warnings and 0 errors.
- Full shared suite: 465 passed, 0 failed.
- The focused migration test upgrades a pre-slice database while preserving an existing row, then
  proves a non-empty registry cannot be automatically downgraded.

## Deliberate exclusions

- No filesystem path resolution, scanning, source winner/overlay materialization, catalog import,
  component-type/schema persistence, state-space binding, ECS write, application activation,
  protocol endpoint, authorization behavior, alias, or legacy application backfill was added.
- The next slice is Slice 4: effective overlay materialization and candidate application manifests.

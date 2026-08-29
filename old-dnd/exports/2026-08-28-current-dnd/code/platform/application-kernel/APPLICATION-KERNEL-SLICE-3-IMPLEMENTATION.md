# Application kernel Slice 3 implementation — application and source registry persistence

Status: **accepted**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Generic application kernel dependency plan](APPLICATION-KERNEL-DEPENDENCY-PLAN.md), C  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Persist immutable application registrations, revisions, base relationships, registered
source specifications, and source-scan receipts in SQLite behind the Slice 2 registry ports.  
Exclusions: Effective overlay/winner materialization, filesystem access or scanning, catalog import,
component-type/schema persistence, state-space binding, ECS writes, application activation, protocol
kinds, authorization implementation, legacy-record backfill, aliases, and any application-specific
branch.  
Allowed files/areas after confirmation: `src/system/application-registry/{domain,persistence,hosting,tests}/`,
`src/system/source-registry/{domain,persistence,hosting,tests}/`,
`DantesRoleplay.DataAccess/DantesRoleplayDbContext.cs`, a new forward EF migration and model
snapshot under `DantesRoleplay.DataAccess/Migrations/`, focused tests, this document, its receipt,
and status/link-only plan updates.  
Stop point: Forward migration, SQLite repositories, no-change/idempotency/migration tests, and
receipt are complete; stop before overlay resolution, physical path configuration, scanning, or
application activation.

## Confirmation required

Slice 0 established the semantics but deliberately deferred the exact persistence shape. Confirm
the following proposal before this document becomes `active` or any runtime/migration artifact is
created:

1. Create these generic SQLite tables, with no foreign key or column referencing a particular
   application:
   - `system_application` — opaque application ID, display name, description, and creation time.
   - `system_application_revision` — `(application_id, revision)` immutable revision, canonical
     SHA-256 fingerprint, and creation time.
   - `system_application_revision_base` — ordered base application IDs for one immutable
     application revision. It preserves the declared relationship rather than assuming the base's
     current revision.
   - `system_application_source` — `(application_id, source_id)` immutable registered allowed-root
     reference, relative path/glob, trust, precedence, and logical identity.
   - `system_application_source_scan` — append-only scan receipt keyed by application/source and
     positive scan generation; it records only scan status, canonical content fingerprint, and
     timestamp, never an absolute host path or file contents.
2. Application registration is append-only: an identical request returns the original revision;
   a changed registration for an existing application ID is rejected. No update, delete, or
   activation capability is introduced.
3. Source registration is append-only: an identical request returns the prior registration;
   changing an existing `(application_id, source_id)` is rejected. A source scan receipt may append
   only at the next generation for that source; duplicate or skipped/stale generations are rejected.
4. The migration is additive only. It backfills no applications, sources, state spaces, catalog
   records, or legacy components. Existing database rows are untouched.
5. The migration is intentionally forward-only: its `Down` path rejects rather than deleting
   immutable registry evidence. Production rollback is restore-from-backup, never an automatic
   destructive downgrade.
6. Existing `DantesRoleplayDbContext` is the SQLite migration owner. The new repositories live in
   their respective system components and are registered by generic component hosting only; no MCP
   command or application host auto-registers data in this slice.

## Confirmed decisions

- [Slice 0](APPLICATION-KERNEL-SLICE-0-IMPLEMENTATION.md) reserves `system`, makes application and
  source identifiers opaque, freezes revisions, and requires source specifications to be relative
  to an allowed-root reference.
- [Slice 2](APPLICATION-KERNEL-SLICE-2-IMPLEMENTATION.md) supplies the ruleset-neutral ports and
  in-memory contracts that the SQLite adapters must preserve.
- The [legacy ownership ratification](LEGACY-OWNERSHIP-RATIFICATION.md) establishes a later
  migration target but authorizes no backfill in this slice.

## Prerequisite evidence

- `DantesRoleplay.DataAccess/DantesRoleplayDbContext.cs` is the existing EF Core/SQLite migration
  owner; the application/source registry is not currently represented there.
- `src/system/application-registry/domain/ApplicationContracts.cs` and
  `src/system/source-registry/domain/SourceContracts.cs` define the current ruleset-neutral
  validation behavior.
- [Slice 2 receipt](receipts/APPLICATION-KERNEL-SLICE-2-RECEIPT.md) proves the initial contract
  tests and explicitly stops before persistence.

## Runtime artifacts after confirmation

- Persistent application and source registry entity records internal to their owning components.
- SQLite implementations of `IApplicationRegistry` and `ISourceRegistry`; the source adapter adds
  an internal scan-receipt port rather than exposing a public protocol operation.
- One additive EF migration and its generated model snapshot update.

## Authoritative state and closed input

The database is authoritative for registered applications, their immutable revision history,
registered source specifications, and scan receipts. A caller supplies only the Slice 2
`ApplicationRegistration` or `SourceRegistration` values and, for an internal scan receipt, the
registered source identity, next generation, status, and SHA-256 fingerprint. The database resolves
existing revisions, source ownership, uniqueness, and idempotency; callers never supply database
keys, absolute paths, a winning overlay, an active manifest, or a state-space binding.

## Behavior, result, and typed effects

Repositories run each mutation in one transaction. They validate using the pure contracts before
writing, then translate database uniqueness/concurrency failures into deterministic no-change
results. Reads return defensive immutable values sorted ordinally by application/source identity.
The scan receipt is evidence only: it neither reads a file nor changes an effective manifest.
No typed effect is produced and no existing world/action transaction is joined.

## Failure, replay, and rollback contract

- Reserved/malformed application IDs, unknown/self/cyclic bases, and changed duplicate application
  registrations fail without rows or revision changes.
- Unsafe source specifications, unknown applications, equal-precedence conflicts, lower-trust
  attempted overrides, and changed duplicate source IDs fail without registry changes.
- A scan receipt for an unregistered source, nonpositive, duplicate, skipped, or stale generation,
  malformed fingerprint, or an existing conflicting receipt fails without rows.
- Repeating an equal registration/receipt returns the original evidence and creates no duplicate.
- Applying the migration to an existing database creates empty isolated tables; failure at any
  migration/repository step rolls back its transaction and leaves existing world/catalog tables
  unchanged.

## Implementation sequence after confirmation

1. Add focused persistence tests for additive migration, cross-application isolation,
   idempotency, failure/no-change, scan-generation ordering, and absolute-path redaction.
2. Add component-owned internal persistence records/adapters and generic hosting registrations.
3. Map the confirmed tables and indexes in the existing database context; generate and inspect one
   forward migration and model snapshot.
4. Run focused tests against fresh SQLite and migration-upgrade databases, then build/full suite.
5. Write the receipt, update the Slice 3/leaf C status once, and stop.

## Acceptance matrix

| Area | Required evidence |
| --- | --- |
| Migration | Fresh and pre-slice SQLite databases upgrade; existing rows/tables remain unchanged; no pending model changes remain. |
| Application registry | Valid registration persists/reloads; equal replay is idempotent; changed duplicate, malformed/reserved ID, unknown/cyclic base, and concurrent duplicate leave no extra revision. |
| Source registry | Valid source persists/reloads in deterministic order; invalid root/specification/trust/precedence/duplicate cases leave no row. |
| Scan evidence | Only contiguous generations append; equal replay is stable; conflicting/stale/skipped generation leaves no row. |
| Isolation | An application/source cannot be read or mutated through another application's identity. |
| Redaction | Values returned outside persistence contain allowed-root IDs and relative specifications only—never resolved absolute paths. |
| Repository | Focused tests, migration-drift test, solution build, full suite, and `git diff --check` pass. |

## Verification commands

```powershell
dotnet test DantesRoleplay.Tests\DantesRoleplay.Tests.csproj --no-restore --filter ApplicationKernel
dotnet test DantesRoleplay.Tests\DantesRoleplay.Tests.csproj --no-restore --filter Migration
dotnet build DantesRoleplay.slnx --no-restore
dotnet test DantesRoleplay.Tests\DantesRoleplay.Tests.csproj --no-restore --no-build
git diff --check
```

## Completion receipt and exit gate

Acceptance evidence is recorded in [the Slice 3 receipt](receipts/APPLICATION-KERNEL-SLICE-3-RECEIPT.md).
Do not start Slice 4 or create a scanner/materializer, manifest, component schema, state-space
binding, protocol kind, or legacy application registration in this slice.

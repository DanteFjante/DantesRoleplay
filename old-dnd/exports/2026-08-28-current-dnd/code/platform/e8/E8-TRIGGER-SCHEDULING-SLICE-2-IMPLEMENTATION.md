# E8 trigger scheduling Slice 2 implementation — durable registrations and evidence

Status: **accepted 2026-08-25**
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)
Dependency tree/leaf: [E8 trigger scheduling dependency tree, D. Persistence](E8-TRIGGER-SCHEDULING-DEPENDENCY-PLAN.md)
Ruleset alignment: **ruleset-neutral**
Source ID and locator: **not applicable**
Outcome: Persist immutable source, structure, and one-time-trigger revisions in the generic SQLite database, and append immutable observation and one-time-fire evidence with deterministic replay/conflict results.
Exclusions: No HTTP route, authentication, source/device identity, schema validation call, hosted worker, lease, retry loop, notification writer/status projection, action/effect/event work, catalog fixture, MCP kind, or live database import.
Allowed files/areas: `src/system/trigger-scheduling/{domain,persistence,tests}`, `DantesRoleplay.DataAccess/DantesRoleplayDbContext.cs`, its EF migration and model snapshot, and the E8 slice/roadmap status documents and receipt.
Stop point: The store can be used directly by later host code, but is not registered or called by a host. A row never schedules, sends, or mutates anything.

## Confirmed decisions

- The user confirmed this migration-bounded Slice 2 by asking to continue on 2026-08-25 after the Slice 1 receipt explicitly named this gate.
- `trigger-scheduling` remains the owner. The main generic SQLite database is the authority for these live records.
- Source, structure, and trigger IDs use the Slice 0 ratified dotted identifier shape; observation and fire IDs retain their ratified prefixes.
- Registrations are immutable revision rows keyed by `(ApplicationId, Id, Version)`. Repeating equal content is a replay; different content under an existing key is a conflict. Revision allocation and administrative authorization are later public-surface work.
- A source revision can only name existing exact structure revisions in the same application. An observation stores and foreign-keys its accepted exact source and structure revision; it never means “latest.” This is the Slice 2 stale-invalidation guard: later revisions cannot silently reinterpret historical evidence.
- One-time triggers have no source/structure dependency. Their immutable versions are stored exactly as evaluated by Slice 1. Condition and external-observation trigger dependencies, leases, and retry state wait for their own later slices.
- Observation evidence is uniquely keyed by both `(ApplicationId, RequestId)` and `(ApplicationId, SourceId, SourceVersion, SourceInstanceId, OccurrenceId)`. Repeating identical evidence is replay; reusing either idempotency identity for changed evidence is conflict, with no new row.
- Fire evidence is keyed by the deterministic Slice 1 fire ID. It records only an eligible evaluator disposition (`due` or `missed`) and is replay-safe. A `pending` evaluation has no fire receipt. It does not claim a delivery or notification outcome.
- Records are append-only. Retention/redaction remains a later explicitly authorized operation; this slice does not delete or update accepted evidence.

## Prerequisite evidence

- [Slice 0 ratification](E8-TRIGGER-SCHEDULING-SLICE-0-RECEIPT.md) confirms IDs, endpoint boundary, retention intent, main-database ownership, and no direct event/effect mutation.
- [Slice 1 receipt](E8-TRIGGER-SCHEDULING-SLICE-1-RECEIPT.md) proves the typed source/structure/observation/one-time-trigger contracts, canonical data and deterministic fingerprints.
- [E8 dependency tree](E8-TRIGGER-SCHEDULING-DEPENDENCY-PLAN.md#next-leaf-and-gate) identifies this leaf and requires it to stop before route, worker, or notification writer.
- Existing `DantesRoleplayDbContext` and `SqliteApplicationRegistry` own generic SQLite mappings and application-scope foreign-key conventions.

## Runtime artifacts

| Artifact | Shape / ownership | Purpose |
| --- | --- | --- |
| `TriggerObservationStructureRecord` | immutable `(ApplicationId, Id, Version)` row | Exact schema profile, normalized schema, hash, description, status, timestamp. |
| `TriggerObservationSourceRecord` and permission rows | immutable `(ApplicationId, Id, Version)` row | Exact status, replay window, rate bound, and allowed structure revisions. |
| `OneTimeTriggerRecord` | immutable `(ApplicationId, Id, Version)` row | UTC due time, misfire policy, notification-only target, timestamp. |
| `TriggerObservationRecord` | append-only evidence | Canonical data/hash, accepted source/structure versions, request/occurrence identity, observed/received UTC times, and admission fingerprint. |
| `TriggerFireReceiptRecord` | append-only evidence | Deterministic fire ID, trigger version, occurrence time, evaluator disposition, recorded UTC time. |
| `ITriggerSchedulingStore` / SQLite implementation | generic persistence seam | Registration and append operations with replay/conflict projections. |
| EF migration | versioned main-database schema | Database constraints/indexes enforce the same basic immutable bounds independently of the store. |

## Authoritative state and closed input

The store accepts only the Slice 1 typed definitions, an admitted observation, or a `OneTimeTriggerEvaluation`. It derives storage timestamps from the supplied UTC clock and derives observation/fire identifiers from the existing deterministic fingerprints. Callers cannot choose a record ID, overwrite an accepted row, replace a source/structure revision, choose a future “latest” revision, or claim that a fire delivered a notification.

## Behavior, result, and transaction ownership

1. Registering a structure validates its typed contract, requires a registered application, and appends one immutable row. The same primary key is replayed only when every persisted field is identical.
2. Registering a source requires the registered application and every listed exact structure revision, then appends its immutable header and permissions atomically.
3. Registering a one-time trigger requires the registered application and appends its immutable definition.
4. Appending an admitted observation creates its deterministic `observation.<32-hex>` ID. The SQLite transaction first resolves both idempotency identities. Equal fingerprints replay the prior record; any changed value conflicts before writing. A successful write includes the exact source and structure version and canonical data.
5. Appending a fire receipt verifies the evaluation belongs to the supplied one-time trigger, then inserts/replays one immutable row under the deterministic `trigger-fire.<32-hex>` key.
6. Each multi-row change is owned by one SQLite transaction. An injected failure rolls back the header/permission or evidence row completely.

## Failure, replay, and rollback contract

| Case | Result | Database effect |
| --- | --- | --- |
| Missing application or exact structure dependency | typed `TRIGGER_SCHEDULING_*_NOT_FOUND` failure | no rows |
| Same revision key, identical definition | `Replay` | no rows added |
| Same revision key, changed definition | `Conflict` | no rows added |
| Observation request or occurrence identity repeated exactly | `Replay` with original observation | no rows added |
| Observation request or occurrence identity reused with changed fingerprint | `Conflict` | no rows added |
| Forged/bounds-violating record bypassing the store | SQLite constraint failure | transaction rolls back |
| Insert failure after a source header / evidence begin | database failure bubbles | transaction rolls back fully |
| Fire ID/evaluation mismatch or unknown trigger version | typed failure | no rows |

## Implementation sequence

1. Add the active implementation document and persistence contracts/entity models.
2. Add the SQLite store and focused persistence tests, without composition registration.
3. Map the records in the sole DbContext with bounds, FK, uniqueness, and immutable-data constraints.
4. Generate and inspect one EF migration and snapshot.
5. Run focused tests, build, full suite, migration freshness check, and record the result.

## Acceptance matrix

| Concern | Evidence |
| --- | --- |
| Immutable structure/source/trigger versions | append, exact replay, changed-content conflict tests |
| Exact source-to-structure authorization | source registration rejects missing/cross-scope revision; persisted permission rows have FKs |
| Observation idempotency | request and occurrence replay/conflict tests |
| Historical interpretation | observation retains exact source/structure revisions despite newer revision rows |
| Fire evidence | deterministic append/replay and mismatch rejection tests |
| Atomicity | injected SQLite failure leaves no partial source/evidence rows |
| Database defense | direct forged record violates SQLite constraints |
| Migration completeness | fresh SQLite context applies migrations and exposes every table |
| Scope discipline | no route, hosted service, or notification/action/event write added |

## Verification commands

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter FullyQualifiedName~TriggerScheduling`
- `dotnet build DantesRoleplay.slnx --no-restore`
- `dotnet ef migrations has-pending-model-changes --project DantesRoleplay.DataAccess --no-build`
- `dotnet test DantesRoleplay.slnx --no-restore`
- `git diff --check`

## Completion receipt and exit gate

Record the delivered tables, migration, focused/full verification, and deliberate exclusions in `E8-TRIGGER-SCHEDULING-SLICE-2-RECEIPT.md`. Mark Slice 2 accepted only after the migration is inspected and every stated check passes. The next slice must separately authorize a worker/lease or private observation endpoint; neither follows automatically from this store.

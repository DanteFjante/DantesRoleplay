# E8 trigger scheduling Slice 8 implementation — exact external-observation matching

Status: **accepted**
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)
Dependency tree/leaf: [E8 trigger scheduling, F. external observation match and G. reviewed coded adapter interface](E8-TRIGGER-SCHEDULING-DEPENDENCY-PLAN.md)
Ruleset alignment: **ruleset-neutral**
Source ID and locator: **not applicable**
Outcome: Add durable application-scoped observation triggers that bind exact current source and
structure revisions, evaluate bounded exact top-level scalar matches through a reviewed coded
adapter, and deliver one immutable notification for each matching accepted observation.
Exclusions: Outbound polling or network access, secret retrieval/storage, webhook/device identity,
phone registration/geofencing, JSONPath/JavaScript/SQL, object/array matching, typed action slots,
actions/effects/events, public trigger administration/status, push delivery, and live import.
Allowed files/areas: `src/system/trigger-scheduling/{domain,persistence,hosting,tests}`;
`DantesRoleplayDbContext`, additive EF migration/snapshot, catalog coverage, the component manifest,
this implementation/receipt, and owning roadmap rows.
Stop point: First-time accepted observations atomically stage bounded exact-revision candidate work;
the durable worker evaluates reviewed scalar matching and can deliver notification-only provenance.
Stop before Slice 9 device identity or Slice 10 public management surfaces.

## Confirmed decisions

- The accepted immutable observation ledger remains authoritative external evidence. Observations
  are not events and matching creates no event, action, effect, or application-state mutation.
- A trigger revision declares one exact application, source ID/version, structure ID/version/hash,
  lifecycle, reviewed matcher ID/version/configuration, and notification target. Registration
  requires those source/structure revisions to be current, enabled/active, and mutually allowed.
- The stable reviewed matcher `system.trigger.observation.closed-scalars` version 1 requires 1–16
  distinct top-level properties with exact JSON scalar values. All comparisons must match; missing,
  object, or array values do not match. Paths, operators, coercion, scripts, and mappings are absent.
- The existing private ingestion owner remains the admission/root transaction. Only a newly
  appended observation selects current active definitions with the same exact source/structure
  revisions and atomically stages at most 64 deterministic work items before commit. Exact replay
  returns prior evidence and stages nothing again.
- Matching is asynchronous and durable so a reviewed adapter failure cannot discard an accepted
  external observation. The worker uses the existing eight-item batch, 60-second lease, three
  attempts, and 5/30-second backoff. False matches complete with immutable `not-matched` evidence;
  true matches commit `matched` evidence, notification, and exact link atomically.
- Source or structure current-revision change makes dependent definitions stale. Stale work cannot
  deliver and status derives the stale reason; a newer reviewed trigger revision is required.
- `IObservationMatchAdapter` is a host-startup reviewed-code seam. It receives only the immutable
  trigger definition and canonical observation projection. This slice registers no outbound
  client, destination, credential/secret port, hosted poller, uploaded code, or adapter-selected
  source. Polling remains gated until explicit network and secret owners exist.

No D&D source or Foundry reference applies because this slice is generic external-input
infrastructure.

## Prerequisite evidence

| Concern | Existing evidence | Slice 8 use |
| --- | --- | --- |
| Immutable authenticated observations | Trigger scheduling Slices 2A and 3 | Reuse exact source/structure/current validation, canonical data, replay identities, principal evidence, and admission transaction. |
| Durable bounded notification delivery | [Slice 7 receipt](E8-TRIGGER-SCHEDULING-SLICE-7-RECEIPT.md) | Reuse lease/retry bounds, notification transaction participant, immutable target/provenance, status, and concurrency patterns. |
| Reviewed adapter boundary | Slice 7 condition adapter seam | Add a separate observation-specific reviewed matcher; do not reinterpret ECS conditions or allow uploaded code. |
| Network and secret ownership | Trigger dependency plan | Both remain missing, so no outbound listener/poller activation is authorized. |

## Runtime artifacts

| Artifact | Purpose |
| --- | --- |
| `ObservationTriggerDefinition` | Closed exact source/structure revision, lifecycle, matcher, configuration, and notification target. |
| `IObservationMatchAdapter` | Reviewed startup matcher returning one deterministic boolean from canonical observation evidence. |
| `ClosedScalarsObservationMatchAdapter` | Stable all-fields exact top-level scalar matcher. |
| Observation trigger definition/current/entity rows | Immutable versions and one current pointer with exact notification links. |
| Observation match work/receipt/notification-link rows | Durable evaluation/delivery state and immutable provenance per trigger revision/observation. |
| `ObservationTriggerAppendParticipant` | Exact indexed candidate staging inside first-time observation admission. |
| `SqliteObservationTriggerWorker` / status reader | Bounded match, retry, delivery, staleness, and internal projection. |
| Additive migration | New constraints, FKs, indexes, sequential-current/transition/provenance and immutability guards. |

## Authoritative state and closed input

The immutable definition owns application/id/version, lifecycle, exact source ID/version, exact
structure ID/version/hash, matcher ID/version, canonical matcher configuration/hash,
notification-only target, entity links, and recorded time. The immutable observation owns its
exact source and structure evidence, occurrence identity, observed/received times, canonical data,
principal, and fingerprints. Work owns only host-derived trigger/observation identity, lease,
attempt, state, and failure fields.

Callers cannot supply match/work/receipt/notification IDs, current source/structure state,
structure hash, trigger selection, matcher selection, adapter output, target data, lease/attempt,
failure/disposition, or handler/action/event claims. Matcher configuration is a canonical object
containing only a `matches` array of closed `{property,value}` objects; values are scalars.

## Behavior, result, and transaction ownership

1. Registration validates the current exact application/source/structure relationship, matcher,
   canonical configuration, notification scope, strict version append, replay/conflict, and stores
   one immutable definition/current pointer.
2. On a first append, the observation root transaction saves the immutable row, selects only
   current active definitions indexed by exact application/source version/structure version/hash,
   rejects candidate fan-out above 64, and inserts one deterministic ready work row per candidate.
   Observation and work commit together. Replay exits before participant staging.
3. A worker leases at most eight eligible rows. It revalidates current trigger, current enabled
   source, current active structure/hash, source permission, and exact observation provenance.
4. The reviewed adapter evaluates canonical observation data. `false` atomically writes one
   immutable `not-matched` receipt and completes work without notification. `true` stages the
   existing immutable notification target and exact observation provenance link, writes one
   `matched` receipt, and completes work in one transaction.
5. Transient database/declared transient adapter failure retries the same deterministic work
   identity after 5/30 seconds. Missing adapter, invalid configuration, stale revisions, permanent
   adapter failure, or exhausted attempts closes work permanently without notification.
6. Status exposes current/superseded/stale lifecycle, latest matched/not-matched observation,
   latest notification, current retry/attempt, and terminal failure without changing evidence.

## Failure, replay, and rollback contract

Malformed match configuration, duplicate/non-scalar fields, unknown adapter/version, missing or
wrong-application source/structure, noncurrent/disabled/retired revisions, mismatched structure
hash, forbidden source-to-structure permission, wrong-scope/deleted notification entities,
candidate fan-out above 64, adapter exception, observation replay/conflict, stale definition, lease
loss, cancellation, concurrent workers, or injected database failure cannot create a partial match,
receipt, link, notification, event, action, effect, or state mutation. Adapter failure does not
delete or rewrite the already accepted immutable observation. Direct database rewrites and illegal
current/work/provenance transitions are rejected.

## Implementation sequence

1. Add pure observation-trigger and reviewed scalar-matcher contracts/tests.
2. Add immutable definition/current/work/receipt/link persistence and additive migration.
3. Add the observation-append participant, bounded worker, notification provenance, status, and hosting.
4. Add exact/replay/stale, true/false, adapter failure/retry, injection, concurrency, rollback,
   tampering, no-event/state, and compatibility tests.
5. Run focused, migration, catalog, full-suite, protocol, build, and diff verification; write the
   receipt and advance roadmap state only after every acceptance row passes.

## Acceptance matrix

| Area | Required proof |
| --- | --- |
| Exact match | One exact current source/structure observation matching all declared scalars produces one immutable notification and provenance. |
| Negative match | Missing, unequal, type-different, object, or array fields complete not-matched with no notification/event/action/effect/state change. |
| Revision staleness | Source or structure current revision/hash/status/permission change prevents later delivery until a newer trigger revision is reviewed. |
| Replay/determinism | Observation replay stages no duplicate; trigger/observation identity deterministically retains one work/receipt/link through retries. |
| Adapter boundary | Unknown/duplicate adapter and malformed config reject registration; transient/permanent failure is bounded; no network/secret/process service or uploaded code is exposed. |
| Scope/bounds | Wrong application/entity scope and more than 64 exact candidates fail without cross-scope match or partial append. |
| Concurrency | Two workers deliver at most one notification/receipt for one trigger/observation. |
| Rollback/security | Injected failure and EF/direct SQLite tampering cannot rewrite evidence or partially complete work/provenance. |
| Compatibility | Existing ingestion response, one-time/recurring/state triggers, ECS/actions/events, web/MCP, and three verbs remain unchanged. |

## Verification commands

```text
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ObservationTrigger|FullyQualifiedName~ObservationIngestion"
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~TriggerScheduling
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~MigrationDriftTests|FullyQualifiedName~CatalogCoverageTests"
dotnet ef migrations has-pending-model-changes --project DantesRoleplay.DataAccess --configuration Release --no-build
dotnet build DantesRoleplay.slnx -c Release --no-restore
dotnet test DantesRoleplay.slnx -c Release --no-restore
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore -p:IncludeProtocolWalkTests=true --filter FullyQualifiedName~ProtocolWalkTests
dotnet run --project DantesRoleplay.Tools/DantesRoleplay.Tools.csproj -c Release --no-build -- validate catalog
git diff --check
```

## Completion receipt and exit gate

Accepted evidence will be recorded in
`platform/e8/E8-TRIGGER-SCHEDULING-SLICE-8-RECEIPT.md`. Stop after internal exact observation
matching delivers notification-only work; phone/device identity remains Slice 9 and public
management remains Slice 10.

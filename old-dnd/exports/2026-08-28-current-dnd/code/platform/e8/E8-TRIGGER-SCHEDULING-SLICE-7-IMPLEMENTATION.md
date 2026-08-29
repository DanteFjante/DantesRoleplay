# E8 trigger scheduling Slice 7 implementation — declared application-state conditions

Status: **accepted**
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)
Dependency tree/leaf: [E8 trigger scheduling, F. world-clock threshold and declared state transition](E8-TRIGGER-SCHEDULING-DEPENDENCY-PLAN.md)
Ruleset alignment: **ruleset-neutral**
Source ID and locator: **not applicable**
Outcome: Add durable, application-scoped world-clock threshold and declared state-condition
triggers that evaluate only after their exact ECS component dependencies commit and create one
notification-only fire when their closed condition activates.
Exclusions: Advancing or interpreting a ruleset clock, arbitrary JSONPath/JavaScript/SQL,
whole-state scans, structural edge conditions, external observations/adapters, phone sources,
actions/effects/events, public web/MCP administration, push delivery, and live import.
Allowed files/areas: `src/system/{trigger-scheduling,ecs-effects}/{domain,persistence,hosting,tests}`;
`DantesRoleplayDbContext`, additive EF migration/snapshot, component manifests, this
implementation/receipt, and owning roadmap rows.
Stop point: The application ECS effect transaction can atomically evaluate declared exact
dependencies, enqueue bounded conditional notification work, deliver it once, and expose internal
status. Stop before Slice 8 observation matching or Slice 10 public surfaces.

## Confirmed decisions

- Application ECS/state-space records are authoritative. The legacy global ECS and event stream are
  not a condition source and are never dual-read or dual-written.
- The existing catalog world-clock mechanic remains the sole owner of clock advancement and its
  calendar/current-minute/revision meaning. A threshold trigger observes an exact application ECS
  component after a mechanic effect; it never advances, corrects, or derives game time.
- A definition declares one state space and 1–16 exact entity/component references. Registration
  validates application ownership, current exact type contracts, live entities, and current values.
- The stable internal adapter ID `system.trigger.closed-scalar` version 1 is a generic reviewed
  adapter, not caller code. It compares one named top-level scalar property with a closed scalar
  using `eq`, `ne`, `gt`, `gte`, `lt`, or `lte`, plus one optional exact top-level guard.
- World-clock thresholds require rising-edge activation, manual re-arm, one dependency, and a
  numeric `gte` comparison. The property and optional calendar guard remain declared configuration;
  C# contains no game-specific component ID, field name, calendar ID, or formula.
- State conditions use rising-edge or level activation. A fire disarms the condition. `on-false`
  re-arms only after a later committed false result; `manual` re-arms only through a newer active
  definition revision. Registration and resume establish baseline truth but never fire.
- Only component add/set/merge/remove and entity deletion can select candidates. One effect batch
  evaluates each candidate once against final staged state. No undeclared component is read.
- The application ECS effect applier remains the root transaction. State changes, truth/arm state,
  and new fire work commit together or all roll back. Delivery uses the existing bounded worker
  policy and notification-only transaction participant.

No D&D source or Foundry reference applies because this slice is generic scheduling infrastructure.

## Prerequisite evidence

| Concern | Existing evidence | Slice 7 use |
| --- | --- | --- |
| Atomic application ECS changes | [Application kernel Slice 8A receipt](../application-kernel/receipts/APPLICATION-KERNEL-SLICE-8A-RECEIPT.md) | One optional transaction participant stages condition state/work before the existing audit and commit. |
| Durable bounded delivery | [Trigger scheduling Slice 6 receipt](E8-TRIGGER-SCHEDULING-SLICE-6-RECEIPT.md) | Reuse eight-item batches, 60-second leases, three attempts, 5/30-second retries, immutable notifications, and exact provenance. |
| World-clock meaning | [`game.core.world.clock.advance`](../../catalog/mechanics/game/core/world/time/mechanic.game.core.world.clock.advance.md) | Observe its declared output component only; never duplicate its rule or emit its event. |
| Exact application ECS contracts | Application ECS component/state-space owners | Persist exact type version/hash and reject cross-application or stale references. |

## Runtime artifacts

| Artifact | Purpose |
| --- | --- |
| `ConditionalTriggerDefinition` and exact dependencies | Closed lifecycle, condition kind, activation/re-arm policy, adapter reference/config, and notification target. |
| `IConditionalTriggerAdapter` | Reviewed host adapter seam returning one boolean from exact dependency snapshots. |
| `ClosedScalarConditionalTriggerAdapter` | Stable generic top-level scalar comparator with bounded canonical configuration. |
| Conditional definition/current/state rows | Immutable revisions plus current truth, armed state, evaluation revision, and operation provenance. |
| Conditional work/receipt/notification-link rows | Durable lease/retry and immutable delivery provenance keyed by state-change operation. |
| `ConditionalTriggerEcsTransactionParticipant` | Changed-dependency selection, bounded final-state evaluation, and atomic work staging. |
| `SqliteConditionalTriggerWorker` / status reader | Bounded delivery and current internal projection. |
| Additive migration | New tables, indexes, constraints, transition guards, and immutable evidence guards. |

## Authoritative state and closed input

The immutable definition owns application/id/version, lifecycle, condition kind, activation,
re-arm policy, state-space ID, ordered exact dependencies, adapter ID/version, canonical adapter
configuration/hash, notification target, and recorded time. The mutable state owns baseline/current
truth, armed state, evaluation revision, last evaluated ECS operation, and last fired operation.

Callers cannot supply current truth, armed state, evaluation revision, work/fire/notification IDs,
lease/attempt fields, receipt state, application ownership, component values, or operation
provenance. The store resolves all current dependency values from the exact state space. Adapter
configuration is a bounded canonical JSON object; values are scalars and property names are one
top-level key, not paths.

## Behavior, result, and transaction ownership

1. Registration validates the exact application/state-space/type/entity/value boundary, resolves
   the named adapter, validates canonical configuration, evaluates baseline truth, and appends one
   immutable strictly newer revision plus current/state projection. It never opens fire work.
2. The ECS effect participant derives a distinct set of changed entity/type keys from the closed
   effect vocabulary, selects only current active definitions indexed by those keys, and rejects a
   batch that would exceed 64 candidate definitions.
3. Each candidate reads only its 1–16 declared exact components after the full effect batch has
   staged. Missing/deleted or contract-mismatched values evaluate false. Adapter failure rejects
   the root ECS transaction rather than committing state without trigger evidence.
4. Rising-edge fires on `false -> true`; level fires on any evaluated true state while armed. A
   fire atomically disarms and creates one deterministic ready work item from definition revision
   and ECS operation ID. False plus `on-false` re-arms.
5. Repeated evaluation of the same ECS operation is idempotent. Unrelated component or structural
   edge changes select no definition. Multiple matching effects in one batch produce one final
   evaluation and at most one work item.
6. The conditional worker leases at most eight due work rows, revalidates the current active
   definition and exact operation provenance, stages the immutable notification/link, receipt,
   and terminal work state in one transaction, and applies the established retry policy.

## Failure, replay, and rollback contract

Malformed IDs/config/scalars, unknown adapters, stale adapter versions, noncurrent component
contracts, missing/deleted dependencies, cross-application state spaces or notification entities,
duplicate dependencies, invalid lifecycle/policy combinations, candidate fan-out beyond 64,
adapter failure, operation replay/conflict, stale definitions, lease loss, cancellation, or injected
database failure cannot partially mutate application state or condition evidence. Direct database
rewrites of immutable definitions/dependencies/receipts/links and illegal current/state/work
transitions are rejected. A delivery failure remains bounded and does not repeat a disarmed level.

## Implementation sequence

1. Add pure closed condition/adapter contracts and comparator tests.
2. Add immutable definition/current/state/work/receipt/link persistence and an additive migration.
3. Add the ECS transaction participant, worker, notification provenance, status, and hosting.
4. Add exact-dependency, edge/level/re-arm, clock threshold, rollback, concurrency, tamper, and
   compatibility tests.
5. Run focused, migration, catalog, full-suite, protocol, build, and diff verification; write the
   receipt and advance roadmap state only after every acceptance row passes.

## Acceptance matrix

| Area | Required proof |
| --- | --- |
| World-clock threshold | A declared numeric threshold fires once after the exact clock component crosses it; already-past registration, correction, and unrelated changes do not fire. |
| State edge/level | Rising edge and armed level semantics, false re-arm, and newer-version manual re-arm are exact and deterministic. |
| Dependency selection | Only declared changed keys evaluate; multiple changes evaluate once; no global JSON/state scan or cross-space/application read. |
| Exact contracts | Stale type version/hash, missing entity/component, and wrong-scope notification links are rejected or evaluate false as specified. |
| Positive delivery | One activation commits one work identity, receipt, immutable notification, and exact provenance link. |
| Replay/concurrency | Same ECS operation, retry/restart, repeated polls, and two contexts create at most one fire/delivery. |
| Rollback/security | Adapter/injected failure and EF/direct SQLite tampering leave no partial application state or trigger evidence. |
| Compatibility | One-time/recurring triggers, observation API, application action execution, structural edges, event authority, web/MCP, and three verbs remain unchanged. |

## Verification commands

```text
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ConditionalTrigger|FullyQualifiedName~ApplicationEcsEffectApplier"
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~TriggerScheduling
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~MigrationDriftTests|FullyQualifiedName~CatalogCoverageTests"
dotnet ef migrations has-pending-model-changes --project DantesRoleplay.DataAccess --configuration Release --no-build
dotnet build DantesRoleplay.slnx -c Release --no-restore
dotnet test DantesRoleplay.slnx -c Release --no-restore
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore -p:IncludeProtocolWalkTests=true --filter FullyQualifiedName~ProtocolWalkTests
git diff --check
```

## Completion receipt and exit gate

Accepted evidence is recorded in the
[Slice 7 completion receipt](E8-TRIGGER-SCHEDULING-SLICE-7-RECEIPT.md). Stop after internal application-state
conditions deliver notification-only work; external observation matching remains Slice 8.

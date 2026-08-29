# E8 trigger scheduling Slice 5 implementation — notification-only reminder

Status: **accepted**
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)
Dependency tree/leaf: [E8 trigger scheduling, F. Trigger consumers](E8-TRIGGER-SCHEDULING-DEPENDENCY-PLAN.md)
Ruleset alignment: **ruleset-neutral**
Source ID and locator: **not applicable**
Outcome: Complete the smallest useful release by atomically creating one immutable, application-linked
notification for one due trigger occurrence and exposing its derived current status.
Exclusions: Recurrence, world-clock/state/observation matching, external adapters, phone identity,
actions/effects/events, public schedule administration, web/MCP kinds, push delivery, and live import.
Allowed files/areas: `src/system/trigger-scheduling/{domain,persistence,hosting,tests}`; the narrow
`events-and-notifications` internal writer contract; `DantesRoleplayDbContext`, one reviewed EF
migration/snapshot, catalog coverage, this plan/receipt, and the owning roadmap rows.
Stop point: A registered one-time notification trigger can complete through the existing worker and
be queried through an internal status reader. Stop before any public creation/query route or any
state-changing target.

## Confirmed decisions

- Slice 0 confirmed immutable notification target content, derived status, trigger-owned root
  transaction, notification-only delivery, and no event or world-state mutation.
- A trigger revision has closed `active` or `cancelled` lifecycle. Rescheduling appends a newer active
  revision; cancellation appends a newer cancelled revision. Historical revisions remain immutable.
- The immutable target contains bounded topic, subject, body, and optional application state-space
  entity links. A cancelled revision retains its target as historical intent but is never eligible.
- Existing Slice 1 factory calls remain source compatible through a safe generic reminder target;
  new callers can supply explicit content. No external observation can select or rewrite it.
- Status is derived, never stored: non-current is `superseded`; current cancelled is `cancelled`;
  current due/missed receipts are `completed`/`missed`; otherwise the clock and operational work
  yield `scheduled` or `due`. Failure detail is projected separately without inventing a seventh
  lifecycle state.
- The notification ID and correlation ID are deterministic from the fire identity. An immutable
  link row binds notification, application, trigger revision, occurrence, and fire receipt.

No D&D source or Foundry reference applies.

## Prerequisite evidence

| Concern | Existing owner/evidence | Slice 5 use |
| --- | --- | --- |
| Durable worker/root transaction | [Slice 4 receipt](E8-TRIGGER-SCHEDULING-SLICE-4-RECEIPT.md) | The participant stages rows in the worker-owned transaction before receipt and terminal work commit. |
| Trigger versions/current pointer | Slices 2/2A persistence receipts | Target/lifecycle extend the immutable version; current pointer still selects one revision. |
| Notification immutability/query | `events-and-notifications` `Notification` and `NotificationStore` | Reuse the existing row/read lifecycle; expose only a narrow internal append participant. |
| Application-scoped ECS | `system_state_space` and `system_ecs_entity` | Declared entity links must resolve live in the trigger application's exact state space at fire time. |

## Runtime artifacts

| Artifact | Shape / purpose |
| --- | --- |
| `TriggerNotificationTarget` and `TriggerLifecycle` | Closed, bounded immutable target and active/cancelled revision contract. |
| Trigger target entity rows | Ordered immutable state-space entity references under one trigger revision. |
| `TriggerNotificationLinkRecord` | One-to-one immutable fire/notification provenance row. |
| `TriggerNotificationTransactionParticipant` | Validates current links and stages one deterministic notification/link without saving or committing independently. |
| `ITriggerScheduleStatusReader` | Bounded application/trigger/version status projection from current definition, receipt, work, and clock. |
| EF schema and security migrations | Target/lifecycle columns, target-entity rows, provenance link, constraints/FKs, then custom SQLite guard restoration after the required table rebuild. |

## Authoritative state and closed input

The registered immutable trigger revision owns due time, lifecycle, misfire policy, target kind,
notification content, optional state-space ID, and ordered entity IDs. The worker supplies only its
trusted lease. The participant reloads the exact current revision and validates every entity against
the exact application-owned state space. Callers cannot supply a notification ID, fire link,
created time, correlation ID, delivery state, event/execution ID, or operation/effect data.

## Behavior, result, and transaction ownership

1. Registration appends a strictly newer immutable trigger revision and ordered target links. A
   cancelled current revision is valid history but never appears in the worker candidate set.
2. For a due active lease, the participant revalidates target kind, lifecycle, application scope,
   current revision, and every live entity link.
3. It stages one deterministic unread notification and one fire link in the existing DbContext. It
   does not call `SaveChanges` or own a transaction.
4. The worker appends the due receipt, marks work completed, and commits notification, link, receipt,
   and work transition together. Any failure rolls all staged rows back.
5. Exact replay or a second worker cannot create a second notification. A pre-existing conflicting
   notification/link fails permanently and creates no receipt.
6. Status reads are `AsNoTracking`, application-scoped, bounded, deterministic under the injected
   UTC clock, and never mutate notification delivery state.

## Failure, replay, and rollback contract

Unknown/stale/cancelled trigger, non-notification target, wrong application/state-space, missing or
deleted entity, malformed stored content, deterministic identity conflict, expired lease,
cancellation, and injected database failure produce no partial notification, link, receipt, event,
effect, action, or state change. Database-lock failures retain the Slice 4 bounded transient retry;
contract and identity failures are permanent and do not spin. Notification content and provenance
rows are append-only in EF and direct SQLite writes.

## Implementation sequence

1. Extend the pure trigger target/lifecycle and persistence contracts with backward-compatible
   defaults and focused validation tests.
2. Map target/link records and create the reviewed schema migration plus the post-rebuild SQLite
   security-guard migration.
3. Implement/register the narrow participant and derived status reader.
4. Add end-to-end worker, reschedule/cancel/status, replay/concurrency, rollback, link validation,
   compatibility, and migrated-database tampering tests.
5. Run focused/full verification, write the receipt, and advance the dependency/roadmap status once.

## Acceptance matrix

| Area | Required proof |
| --- | --- |
| Positive | A 23:00 active reminder commits one immutable notification, link, due receipt, and completed work. |
| Content/link | Exact topic/subject/body and ordered live application entity links survive readback. |
| Status | Scheduled, due, completed, cancelled, missed, and superseded derive correctly. |
| Replay/concurrency | Repeated batch and two workers leave one notification/link/receipt. |
| Reschedule/cancel | New active revision supersedes old; new cancelled revision is never claimed or delivered. |
| Negative/no-change | Wrong scope, missing/deleted entity, stale revision, conflict, and injected failure leave no partial rows. |
| Immutability | EF and direct migrated SQLite reject notification provenance and trigger-target rewrites/deletes. |
| Event/state authority | Notification completion adds no event, operation, effect, or ECS mutation. |
| Compatibility | Existing notification query/state transitions and existing trigger factory calls still pass. |

## Verification commands

```text
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~TriggerScheduling
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~NotificationTests
dotnet ef migrations has-pending-model-changes --project DantesRoleplay.DataAccess --configuration Release --no-build
dotnet build DantesRoleplay.slnx -c Release --no-restore
dotnet test DantesRoleplay.slnx -c Release --no-restore
git diff --check
```

## Completion receipt and exit gate

Record accepted evidence in `E8-TRIGGER-SCHEDULING-SLICE-5-RECEIPT.md`. Acceptance requires every
matrix row and migration freshness to pass in the same worktree. Then mark this document accepted
and hand the dependency tree to Slice 6 without implementing recurrence.

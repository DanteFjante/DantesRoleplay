# E8 trigger scheduling Slice 6 implementation — closed calendar recurrence

Status: **accepted**
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)
Dependency tree/leaf: [E8 trigger scheduling, F. recurring real-time schedules](E8-TRIGGER-SCHEDULING-DEPENDENCY-PLAN.md)
Ruleset alignment: **ruleset-neutral**
Source ID and locator: **not applicable**
Outcome: Add durable daily, weekly, and monthly notification recurrence with deterministic IANA
timezone/DST resolution, versioned pause/resume/cancel, and bounded collapsed restart catch-up.
Exclusions: Free cron/expressions, seconds/minutes recurrence, world clocks, state/observation
matching, adapters, phone identity, actions/effects/events, public web/MCP administration, push
delivery, timezone-rule downloads, and live import.
Allowed files/areas: `src/system/trigger-scheduling/{domain,persistence,hosting,tests}`;
`DantesRoleplayDbContext`, additive EF migrations/snapshot, catalog coverage, the component manifest,
this implementation/receipt, and owning roadmap rows.
Stop point: The existing hosted scheduler can durably fire closed recurring notification targets and
project their internal status. Stop before Slice 7 consumers or Slice 10 public surfaces.

## Confirmed decisions

- Recurrence is closed `daily`, `weekly`, or `monthly`; interval is 1–365; local time and an IANA
  timezone are required; optional start/end dates are inclusive.
- A missing start has no lower bound and uses the fixed Gregorian epoch month/week/day as the
  interval anchor, avoiding registration-time or caller-clock ambiguity.
- Weekly recurrence requires one or more distinct weekdays and no month day. Monthly recurrence
  requires day 1–31 and no weekdays. Daily accepts neither.
- A nonexistent monthly day is skipped. DST gaps use `skip` or `next-valid`; overlap uses `earlier`
  or `later`. Defaults remain gap `skip`, overlap `earlier`.
- Lifecycle is versioned `active`, `paused`, or `cancelled`. Pausing/cancelling creates no work.
  Resuming appends a newer active version and schedules the first occurrence at or after trusted
  resume time; paused occurrences are not replayed.
- Recurring `skip` and `fire-once` reuse Slice 4. After downtime, all elapsed occurrences collapse
  to the most recent eligible local occurrence: `skip` records one miss; `fire-once` delivers it if
  no more than 24 hours late, otherwise records one miss. No unbounded backlog is created.
- Each occurrence has a recurrence-specific deterministic fire ID. One current occurrence may be
  ready/leased/retrying; terminal evidence advances state atomically to the next occurrence.
- Notification content/entity links and the root transaction reuse Slice 5 unchanged in meaning.

No D&D source or Foundry reference applies.

## Prerequisite evidence

| Concern | Existing evidence | Slice 6 use |
| --- | --- | --- |
| UTC clock, misfire, lease, retry | [Slice 4 receipt](E8-TRIGGER-SCHEDULING-SLICE-4-RECEIPT.md) | Same 60-second leases, three attempts, 5/30-second retry, 24-hour fire-once window, and batch bound. |
| Atomic immutable reminder | [Slice 5 receipt](E8-TRIGGER-SCHEDULING-SLICE-5-RECEIPT.md) | Same deterministic notification writer, application/entity validation, and all-or-nothing commit. |
| Version/current persistence | Slices 2/2A receipts | Recurrence definitions append immutable revisions and use one current pointer. |
| Confirmed time semantics | Slice 0 and dependency tree | Closed calendar form, IANA zones, DST policies, monthly skipping, and bounded collapse are already ratified. |

## Runtime artifacts

| Artifact | Purpose |
| --- | --- |
| `RecurringTriggerDefinition` / `RecurrencePattern` | Closed validated lifecycle, calendar, timezone, policy, and notification target. |
| `RecurringScheduleEvaluator` | Pure next/latest occurrence resolution with deterministic DST behavior. |
| Recurring definition/current/state rows | Immutable versions plus one mutable current next-occurrence projection. |
| Recurring work/receipt/notification-link rows | Durable lease/retry, immutable outcome, and notification provenance per occurrence. |
| `SqliteRecurringTriggerWorker` | Bounded claim, collapse, retry/restart, target stage, receipt, and atomic next advance. |
| `IRecurringTriggerStatusReader` | Internal current/superseded status with next and last occurrence evidence. |
| Additive migration | New tables, constraints, FKs, indexes, transition and immutability guards; no existing-table rebuild. |

## Authoritative state and closed input

The immutable recurring revision owns application/id/version, lifecycle, calendar kind/interval,
local time, IANA zone, optional inclusive date bounds, kind-specific weekdays/month-day, DST gap and
overlap policies, misfire policy, notification target, and recorded time. The trusted store validates
the timezone and materializes initial next state. The worker derives occurrences from the exact
stored revision and trusted UTC clock. Callers cannot supply next/last occurrence, work/lease,
attempts, receipt, notification/provenance IDs, delivery state, or event/effect/action data.

## Behavior and transaction ownership

1. Pure evaluation resolves local candidate dates from calendar arithmetic, skips impossible
   monthly dates, and converts one local time to one UTC instant according to explicit DST policy.
2. Registration appends a strictly newer immutable revision/current pointer and atomically resets
   current operational state. Active starts at the first occurrence at/after trusted now; paused or
   cancelled has no next occurrence. Resume therefore skips the paused interval.
3. A bounded worker handles at most eight current schedules per batch. With no active work it
   collapses elapsed occurrences to the most recent; with retry/expired lease it continues the same
   occurrence instead of opening a newer one.
4. Miss completion atomically appends one missed receipt, closes work, records last outcome, and
   advances to the first occurrence after the collapsed occurrence.
5. Due completion stages the existing immutable notification and recurring provenance, appends one
   due receipt, closes work, records last outcome, and advances next state in one transaction.
6. Terminal end-date exhaustion leaves `NextOccurrenceAtUtc = null`; status derives `completed`.

## Failure, replay, and rollback contract

Invalid kind fields, interval/date bounds, local time, non-IANA/unknown zone, DST gap/overlap policy,
impossible timezone conversion, stale/paused/cancelled revision, wrong target/entity scope,
conflicting fire identity, lease loss, cancellation, or injected database failure cannot partially
advance state or create notification/receipt evidence. Explicit database/handler unavailability
uses the existing bounded retry; other failures close permanently. Clock rollback cannot repeat a
terminal occurrence. Forward jumps create at most one collapsed work item per current schedule.

## Implementation sequence

1. Add pure closed contracts/evaluator and exhaustive calendar/DST tests.
2. Add immutable definition/current/state/work/receipt/link persistence and additive migration.
3. Extend the notification participant for recurring provenance and register the recurring worker/status reader.
4. Add pause/resume, collapsed catch-up, end bounds, retry/restart, concurrency, replay, rollback,
   tampering, compatibility, and no-event/state tests.
5. Run focused, migration, catalog, full-suite, protocol, build, and diff verification; receipt and
   advance the roadmap only after every acceptance row passes.

## Acceptance matrix

| Area | Required proof |
| --- | --- |
| Calendar | Daily/weekly/monthly intervals, weekday sets, day 1/28/29/30/31, missing month days, start/end inclusivity. |
| DST | Stockholm spring gap skip/next-valid and autumn overlap earlier/later resolve to exact UTC. |
| Determinism | Same definition/reference produces the same next/latest UTC occurrence and fire ID. |
| Positive | Recurring due occurrence commits one exact notification/link/receipt and advances next. |
| Pause/resume/cancel | Paused/cancelled never fire; resume skips paused time and schedules a future/current occurrence. |
| Catch-up | Many elapsed occurrences collapse to one most-recent due/missed result; no backlog loop. |
| Retry/restart | Same occurrence retains identity through 5/30 retry and expired-lease recovery. |
| Replay/concurrency | Repeated polls and two contexts create one notification/receipt per occurrence. |
| Rollback/security | Injected failure and EF/direct SQLite tampering cannot partially advance or rewrite evidence. |
| Compatibility | One-time reminders, notification state, observation API, event/action/state, web/MCP, and three verbs remain unchanged. |

## Verification commands

```text
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~TriggerScheduling
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~MigrationDriftTests|FullyQualifiedName~CatalogCoverageTests"
dotnet ef migrations has-pending-model-changes --project DantesRoleplay.DataAccess --configuration Release --no-build
dotnet build DantesRoleplay.slnx -c Release --no-restore
dotnet test DantesRoleplay.slnx -c Release --no-restore
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore -p:IncludeProtocolWalkTests=true --filter FullyQualifiedName~ProtocolWalkTests
git diff --check
```

## Completion receipt and exit gate

Accepted evidence is recorded in
[the Slice 6 completion receipt](E8-TRIGGER-SCHEDULING-SLICE-6-RECEIPT.md). Slice 7 is the next
leaf; world-clock and state-condition behavior remain outside this accepted boundary.

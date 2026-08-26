# E8 trigger scheduling Slice 4 implementation — durable one-time worker

Status: **accepted**
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)
Dependency tree/leaf: [E8 trigger scheduling, D. Persistence and worker](E8-TRIGGER-SCHEDULING-DEPENDENCY-PLAN.md)
Ruleset alignment: **ruleset-neutral**
Source ID and locator: **not applicable**
Outcome: Add the bounded durable one-time scheduler runtime with persisted occurrence work,
tokenized expiring leases, fixed retry classification/backoff, misfire finalization, concurrent
claim safety, and restart recovery.
Exclusions: Notification creation/status, target content, recurrence, world-clock/state conditions,
external-observation matching, administration routes, MCP kinds, action/effect/event writes,
outbound network access, device identity, or live database import.
Allowed files/areas: `src/system/trigger-scheduling`, its focused tests,
`DantesRoleplay.DataAccess` composition/model plus one additive migration, catalog-coverage
declarations for the operational table, and E8 status/receipt documents.
Stop point: A hosted worker can durably discover, claim, retry, miss, or transactionally complete
one registered current one-time occurrence through a narrow participant port; no production
participant writes a notification or any application state.

## Confirmed decisions

- The user's “Continue” after the accepted Slice 3 handoff confirms the Slice 4 migration and
  internal operational record boundary. It creates no public route, kind, or caller-selected ID.
- Slice 0 fixes a 60-second lease, three total attempts, 5/30-second retry delays, `skip` and
  `fire-once` misfires, and a 24-hour maximum `fire-once` lateness.
- Operational work is mutable and separate from immutable definitions/fire receipts. Its key is the
  already-confirmed deterministic fire ID, so restart and multiple workers converge on one row.
- A target participant must be scoped with the same database context and stages durable work inside
  the worker-owned transaction. Success receipt, participant rows, and terminal work state commit
  atomically. Failure or stale/expired lease rolls all staged work back.
- The default participant is unavailable. Due definitions remain untouched without consuming
  work rows or attempts until Slice 5 supplies the notification participant; missed work needs no
  participant and is finalized.

## Prerequisite evidence

- [Slice 0](E8-TRIGGER-SCHEDULING-SLICE-0-IMPLEMENTATION.md) owns the confirmed time, lease,
  retry, misfire, notification-only, and transaction semantics.
- [Slice 2A receipt](E8-TRIGGER-SCHEDULING-SLICE-2A-RECEIPT.md) proves current-revision checks,
  trusted-clock evaluation, deterministic fire receipts, concurrency hardening, and immutable DB
  evidence.
- [Slice 3 receipt](E8-TRIGGER-SCHEDULING-SLICE-3-RECEIPT.md) proves composition and the full suite
  before the worker is introduced.

## Runtime artifacts

- Add `TriggerFireWorkRecord` in table `trigger_fire_work`, keyed by deterministic `FireId`, with
  exact trigger occurrence identity, closed state, attempt count, next-attempt instant, opaque
  lease owner/token/expiry, closed failure kind, revision, and trusted timestamps.
- Add `TriggerFireLease`, `TriggerFireAttemptResult`, `ITriggerFireTransactionParticipant`, and
  `IOneTimeTriggerWorker` contracts. The participant cannot select trigger identity, occurrence,
  attempt count, lease duration, receipt ID, or retry time.
- Add a SQLite worker coordinator plus a singleton hosted polling service. One batch is bounded to
  eight ordered due candidates; polling is one second and contains no wake/public surface.
- Register a scoped unavailable participant by default. Future Slice 5 may replace it with the
  narrow notification transaction participant.

## Authoritative state and closed input

SQLite current trigger pointers and immutable definitions determine candidates. The trusted clock
determines due/missed status, lease expiry, retry eligibility, and timestamps. The deterministic
fingerprint determines `FireId`; the worker generates a random lease token and bounded process
worker ID. No request, trigger target, participant, or external timestamp supplies operational
identity or scheduling decisions.

Work states are closed to `ready`, `leased`, `retry`, `completed`, `missed`, and `failed`.
Failure kinds are closed safe codes. State-shape checks require lease fields only while leased,
retry time only while retrying, and clear all operational fields for terminal states.

## Behavior, result, and transaction ownership

1. Read at most eight eligible current one-time definitions with `DueAt <= trusted now`, ordered by
   due instant/application/ID/version. Without a participant, only already-missed definitions are
   eligible.
2. Materialize each deterministic work row with insert-if-absent. A terminal row or immutable fire
   receipt prevents repeat work after restart or clock rollback.
3. A missed occurrence atomically transitions to `missed` and appends the immutable missed receipt.
4. Due work is claimable only when a participant is available and the row is ready, retry-due, or
   leased with an expired lease. Atomic compare/update increments the attempt and grants one
   60-second random-token lease.
5. Completion reloads the current pointer and lease inside one transaction, re-evaluates time, and
   invokes the scoped participant. Success stages the due receipt and terminal state in that same
   transaction. Any lease expiry, supersession, cancellation, or exception rolls back participant
   changes.
6. Explicit transient handler-unavailable/database failures schedule attempt 2 after 5 seconds and
   attempt 3 after 30 seconds. A third transient failure becomes terminal `failed`. Permanent,
   malformed, unauthorized, stale, policy, or unknown failures never retry.

## Failure, replay, and rollback contract

- A non-UTC trusted clock fails closed before state change.
- A current pointer change before completion terminates the old work as stale without a success
  receipt or participant row.
- An expired or wrong lease token cannot complete or schedule failure state. Expired attempts may
  be reclaimed; an expired third attempt becomes terminal exhausted.
- Cancellation rolls back the active transaction and leaves the lease recoverable after expiry.
- SQLite lock/provider failures are the only database failures classified transient. Unknown
  exceptions are permanent and their raw messages are never persisted.
- Exact replay, worker restart, clock rollback, or two workers produce one terminal work row and
  at most one immutable fire receipt.

## Implementation sequence

1. Add closed worker contracts and operational persistence model/migration.
2. Add atomic materialize/claim/finalize coordinator and default unavailable participant.
3. Register the scoped coordinator and bounded hosted worker.
4. Add fake-clock, multi-worker, retry, restart, clock-jump, expiry, stale-pointer, and rollback
   tests; run migration drift, full suite, and diff checks.

## Acceptance matrix

| Concern | Required proof |
| --- | --- |
| Due success | Available participant stages once; one due receipt and completed work commit together. |
| Default stop | Unavailable production participant creates no due work/attempt and writes no success receipt. |
| Misfire | `skip` late and `fire-once` over 24 hours become one missed receipt; catch-up inside 24 hours remains due. |
| Retry | Attempts occur immediately, after 5 seconds, and after 30 seconds; only explicit transient failures retry. |
| Lease | Lease is 60 seconds, wrong/unexpired token cannot steal, expired lease is reclaimable, and third expiry exhausts. |
| Concurrency | Two contexts/workers racing the same occurrence yield one lease/participant execution/receipt. |
| Restart/time | A fresh coordinator recovers persisted ready/retry/expired work; forward jumps apply misfire and rollback cannot repeat terminal work. |
| Stale/rollback | Superseded trigger or participant failure leaves no participant/success receipt; terminal failure evidence is safe and bounded. |
| Compatibility | Existing observation/web/MCP/event/action/notification behavior and full suites remain unchanged. |

## Verification commands

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --filter "FullyQualifiedName~TriggerSchedulingWorker|FullyQualifiedName~TriggerSchedulingPersistence"`
- `dotnet build DantesRoleplay.slnx -c Release --no-restore`
- `dotnet ef migrations has-pending-model-changes --project DantesRoleplay.DataAccess --configuration Release --no-build`
- `dotnet test DantesRoleplay.slnx -c Release --no-restore`
- `git diff --check`

## Completion receipt and exit gate

Record the migration, lease/retry/misfire/concurrency/restart evidence, full verification, and
deliberate exclusions in `E8-TRIGGER-SCHEDULING-SLICE-4-RECEIPT.md`. Mark Slice 4 accepted only
after hostile and concurrent tests pass. Stop before Slice 5 notification creation/status work.

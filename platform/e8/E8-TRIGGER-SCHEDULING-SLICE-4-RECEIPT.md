# E8 trigger scheduling Slice 4 completion receipt

Status: **accepted**
Completed: 2026-08-25
Ruleset alignment: **ruleset-neutral**
Implementation document: [durable one-time worker](E8-TRIGGER-SCHEDULING-SLICE-4-IMPLEMENTATION.md)
Dependency tree: [durable scheduling and external triggers](E8-TRIGGER-SCHEDULING-DEPENDENCY-PLAN.md)

## Delivered boundary

Slice 4 adds the first bounded durable one-time scheduler runtime while preserving the stop before
notification or application-state writing:

- deterministic occurrence work is stored in `trigger_fire_work` separately from immutable trigger
  definitions and fire receipts;
- one hosted worker polls one-second intervals and examines at most eight ordered eligible
  occurrences per batch;
- atomic claims grant random-token 60-second leases, increment one of three total attempts, and
  permit only ready, retry-due, or expired-lease work;
- explicit transient failures retry after 5 and 30 seconds, while permanent, stale, unknown, and
  exhausted failures become closed terminal evidence without persisting exception details;
- `skip` lateness and `fire-once` lateness beyond 24 hours append one missed receipt; bounded
  `fire-once` catch-up remains due;
- a scoped transaction participant stages future target rows inside the worker-owned transaction,
  so participant evidence, due receipt, and terminal work state commit together or all roll back;
- the production participant is unavailable by default, so due definitions create no work or
  attempt until Slice 5 supplies the notification participant; missed definitions can still close;
  and
- the `TriggerSchedulingOneTimeWorker` migration adds closed state/lease constraints plus direct-DB
  transition and delete guards.

## Security review closures

- Candidate selection and atomic claim both reject an already-existing immutable fire receipt,
  preventing legacy/pre-work evidence from replaying a target.
- Deterministic work identity is read back after insert-if-absent; a forged composite/ID conflict
  fails closed instead of silently processing another row.
- Terminal work cannot be rewritten or deleted directly, revisions must increase exactly once, and
  attempt counts can increase only during a permitted lease transition.
- Cleanup of stale and exhausted work is bounded to eight rows per pass. Raw failures, participant
  messages, target data, and authorization material are never persisted.
- Lease and current-trigger identity are rechecked before and after participant staging. Expiry,
  supersession, cancellation, or injected failure rolls staged rows back and leaves only safe
  recoverable/terminal operational evidence.

## Evidence

- Focused trigger-scheduling tests: **46 passed, 0 failed**.
- Worker tests prove default-safe behavior, due completion, missed/catch-up policies, fixed retry
  times, permanent/exhausted failure, 60-second restart recovery, cancellation rollback, clock
  rollback/forward replay, eight-item batching, current-revision revocation, and legacy receipt
  blocking.
- Two independent SQLite contexts racing one occurrence produce one lease execution, one terminal
  work row, and one immutable receipt.
- Migrated-database tests prove terminal rewrites and deletes are rejected by SQLite triggers.
- Catalog coverage accepts the operational table and all fifteen non-catalog fields.
- `dotnet build DantesRoleplay.slnx -c Release --no-restore`: **0 warnings, 0 errors**.
- `dotnet ef migrations has-pending-model-changes --project DantesRoleplay.DataAccess
  --configuration Release --no-build`: **no pending model changes**. The local EF CLI retains its
  existing 10.0.2-versus-10.0.11 informational version warning.
- `dotnet test DantesRoleplay.slnx -c Release --no-restore`: **892 shared tests passed and 20
  local-AI tests passed**.
- `git diff --check`: passed; only existing line-ending notices were reported.

## Deliberate exclusions

No notification row/status projection, recurrence, world-clock/state condition, observation
matching, external adapter, phone identity, action/effect/event write, administration route, MCP
kind, public hosting, or live database import was added. A due notification-only trigger remains
intentionally unexecuted until Slice 5 registers its transactional participant.

## Handoff

Slice 5 is the separately gated notification-only reminder and current status projection. It must
replace the unavailable participant with a narrow immutable notification writer that shares the
worker transaction, proves exactly-once linkage, and creates no event or world-state mutation.

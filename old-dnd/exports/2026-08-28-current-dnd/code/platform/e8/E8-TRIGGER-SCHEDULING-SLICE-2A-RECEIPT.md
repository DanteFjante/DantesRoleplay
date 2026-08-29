# E8 trigger scheduling Slice 2A completion receipt

Status: **accepted**
Completed: 2026-08-25
Ruleset alignment: **ruleset-neutral**
Implementation document: [persistence security hardening](E8-TRIGGER-SCHEDULING-SLICE-2A-IMPLEMENTATION.md)
Dependency tree: [durable scheduling and external triggers](E8-TRIGGER-SCHEDULING-DEPENDENCY-PLAN.md)

## Delivered boundary

Slice 2A closes all seven findings from the Slice 2 security review before any route or worker can
use the persistence layer:

- callers can no longer provide admitted observations or fire evaluations; the store derives both
  from registered state and its constructor-injected trusted clock;
- mutable current-revision pointers revoke superseded source, structure, and trigger revisions for
  new work while immutable historical evidence remains readable;
- SQLite write transactions serialize replay checks, and unique-key losers resolve to the committed
  append, replay, or conflict result instead of leaking provider errors;
- EF rejects updates/deletes to immutable trigger rows, while twelve migrated SQLite triggers
  enforce the same rule for direct database writes;
- observations have a five-part foreign key proving the exact source revision permitted the exact
  structure revision;
- replay windows accept only integral seconds from 1 through 604800; and
- the `TriggerSchedulingSecurityHardening` and
  `TriggerSchedulingObservationImmutability` migrations add and backfill the hardened schema using
  transactional SQLite table rebuilding.

## Evidence

- Focused trigger-scheduling tests: **25 passed, 0 failed**.
- Concurrent exact submissions: one append, one replay, one row.
- Concurrent changed-identity submissions: one append, one conflict, one row.
- Migration hardening test: current pointers backfilled, permission FK enforced, and all twelve
  SQLite immutability triggers installed.
- Migration atomicity test: no non-transactional migration warning.
- `dotnet build DantesRoleplay.slnx --no-restore`: **0 warnings, 0 errors**.
- `dotnet ef migrations has-pending-model-changes --project DantesRoleplay.DataAccess --no-build`:
  **no pending model changes**. (The local EF CLI reports its existing 10.0.2-versus-10.0.11
  informational version warning.)
- `dotnet test DantesRoleplay.slnx --no-build --no-restore`: **857 shared tests passed and 20
  local-AI tests passed**.
- `git diff --check`: passed; the worktree retains pre-existing line-ending notices.

## Deliberate exclusions

No route, authentication/device identity, rate limiter, schema-validation invocation, hosted
worker, lease/retry state, notification writer, action/effect/event write, external adapter, MCP
kind, or live database import was added. Trigger scheduling remains unreachable from the host.

## Handoff

Slice 3 is the separately gated private application-scoped observation endpoint. It must bind the
E9 principal boundary, authenticate before expensive parsing/validation, enforce request and rate
limits, invoke exact schema validation, and return only the safe observation receipt. It must not
add a worker, notification writer, or state-changing target.

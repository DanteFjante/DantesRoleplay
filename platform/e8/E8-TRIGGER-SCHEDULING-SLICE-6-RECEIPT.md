# E8 trigger scheduling Slice 6 completion receipt

Status: **accepted**
Completed: 2026-08-25
Ruleset alignment: **ruleset-neutral**
Implementation document: [closed calendar recurrence](E8-TRIGGER-SCHEDULING-SLICE-6-IMPLEMENTATION.md)
Dependency tree: [durable scheduling and external triggers](E8-TRIGGER-SCHEDULING-DEPENDENCY-PLAN.md)

## Delivered boundary

- Closed daily, weekly, and monthly recurrence uses bounded calendar fields, whole-second local
  time, an explicit IANA timezone, optional inclusive start/end dates, and explicit DST gap and
  overlap policies. Missing monthly dates are skipped.
- Immutable application-scoped revisions own `active`, `paused`, or `cancelled` lifecycle.
  A trusted current pointer and recurrence state project the next and last occurrence; resume
  begins at or after trusted resume time and does not replay paused time.
- A bounded recurring worker reuses Slice 4's eight-item batches, 60-second leases, three attempts,
  5/30-second retry delays, and 24-hour `fire-once` limit. Restart catch-up collapses all elapsed
  occurrences to the latest eligible occurrence.
- Due or missed terminal evidence advances the next calendar occurrence atomically. Permanent
  delivery failure is retained in status and advances the calendar so one bad occurrence cannot
  freeze later reminders; stale superseded work cannot advance the replacement revision.
- The Slice 5 notification participant now stages either one-time or recurring provenance in the
  same worker-owned transaction. Notification, recurring link, receipt, terminal work, and next
  state commit together or all roll back.
- Internal status distinguishes scheduled, due, paused, cancelled, completed, and superseded
  recurrence while exposing next/last outcome, last failure, latest notification, and current
  retry evidence.

No public web/MCP administration route, event, effect, action, world-clock mutation, arbitrary
state/JSON match, adapter, phone identity, push delivery, or ruleset-specific behavior was added.

## Security review closures

- Contract validation rejects unknown/invalid IANA zones, mixed kind-specific fields, repeated or
  missing weekdays, invalid month days, fractional-second local times, inverted bounds, unsupported
  targets, and invalid lifecycle/DST policies before persistence.
- The worker revalidates current revision, active lifecycle, exact derived occurrence, application
  scope, state space, and every live entity link inside the leased root transaction.
- Deterministic recurrence fire IDs use a distinct fingerprint domain; retry, restart, repeated
  polling, and two contexts retain one identity and one notification/receipt.
- SQLite checks and triggers reject immutable definition/entity/receipt/link rewrites, illegal
  current/state/work transitions, operational deletes, and provenance that does not exactly match
  a due receipt. EF retains the matching immutable-row guard.
- Clock jumps create at most one collapsed work item per schedule. Lease loss, cancellation,
  supersession, injected database failure, invalid provenance, and terminal failure cannot leave a
  partial notification, receipt, or state advance.
- Calendar search is bounded, including forever-impossible aligned monthly patterns near the end
  of the supported Gregorian range.

## Evidence

- Focused trigger-scheduling suite: **78 passed, 0 failed**.
- Migration drift and catalog coverage: **7 passed, 0 failed**.
- Full solution suite: **928 shared tests and 20 local-AI tests passed, 0 failed**.
- Protocol walk after dependency registration changed: **6 passed, 2 intentionally skipped, 0 failed**.
- EF pending-model check: **no pending model changes**; the existing local EF CLI
  10.0.2-versus-runtime-10.0.11 informational warning remains.
- Release build: **0 warnings, 0 errors**.
- `git diff --check`: passed; only existing line-ending notices were reported.

## Acceptance coverage

Daily/weekly/monthly intervals, weekday sets, days 28–31, leap day, absent month days, inclusive
bounds, Stockholm spring gap `skip`/`next-valid`, autumn overlap `earlier`/`later`, deterministic
next/latest occurrence and fire identity, exact notification delivery, end completion,
pause/resume/cancel, collapsed skip/fire-once downtime, retry identity, concurrent workers,
supersession, rollback, status, EF protection, and direct SQLite tampering are asserted. Successful
recurrence creates no event or application-state mutation.

## Deliberate exclusions and handoff

World-clock threshold and declared state-transition triggers, external observation matching,
adapters, phone identity/geofencing, outbound feeds, delegated state-changing targets, public
schedule administration/status, web/MCP UI, push delivery, and live database import remain
excluded. Slice 7 is the next separately gated leaf and requires Sol review where its world/state
semantics cross existing owners.

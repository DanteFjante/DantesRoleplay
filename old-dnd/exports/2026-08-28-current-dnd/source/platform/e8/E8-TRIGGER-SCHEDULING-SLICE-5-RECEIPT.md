# E8 trigger scheduling Slice 5 completion receipt

Status: **accepted**
Completed: 2026-08-25
Ruleset alignment: **ruleset-neutral**
Implementation document: [notification-only reminder](E8-TRIGGER-SCHEDULING-SLICE-5-IMPLEMENTATION.md)
Dependency tree: [durable scheduling and external triggers](E8-TRIGGER-SCHEDULING-DEPENDENCY-PLAN.md)

## Delivered boundary

Slice 5 completes the smallest useful durable-reminder release:

- immutable one-time trigger revisions now carry closed `active`/`cancelled` lifecycle plus
  bounded notification topic, subject, body, and optional ordered application state-space entity
  links;
- the production worker participant revalidates the exact current trigger/application/entity
  boundary and stages one deterministic unread notification plus one immutable fire-provenance link;
- notification, provenance link, due fire receipt, and completed work transition commit in the
  worker-owned transaction or all roll back;
- rescheduling appends a newer active revision and cancellation appends a newer cancelled revision;
  cancelled current revisions are not eligible for work;
- the bounded internal status reader derives `scheduled`, `due`, `completed`, `cancelled`, `missed`,
  or `superseded` from current definition, work, receipt, and the trusted UTC clock; and
- existing notifications retain their unread/read/archive lifecycle while content, entity links,
  trigger targets, and fire provenance remain immutable.

No new web route, MCP kind, public schedule writer, event, effect, action, operation, ECS mutation,
push-delivery promise, or application-specific rule was added.

## Security review closures

- Notification and correlation IDs derive from the deterministic fire ID; conflicting existing
  notification/link identities fail permanently without creating a receipt.
- Current trigger revision, active lifecycle, target kind, occurrence, exact application-owned
  state space, and every live entity link are rechecked inside the leased root transaction.
- Cancellation, supersession, missing/deleted/wrong-scope links, lease expiry, replay, and injected
  failure leave no partial notification, provenance link, receipt, event, or state change.
- EF guards reject notification-content, notification-entity, trigger-target, and provenance
  mutation while still permitting the existing delivery-state transitions.
- Migrated SQLite databases enforce notification content/link immutability, trigger/link identity,
  target bounds, provenance/fire agreement, and post-rebuild trigger-definition immutability.
- The schema delta avoids a non-transactional SQLite table rebuild. A second empty-model security
  migration restores/checks custom triggers only after all schema operations are complete.
- Legacy trigger rows receive the source-compatible generic reminder target and exact replay rather
  than silently becoming conflicting definitions.

## Evidence

- Focused trigger-scheduling suite: **55 passed, 0 failed**.
- Combined trigger/catalog/notification boundary suite: **67 passed, 0 failed** before the final
  hardening assertion; the final notification-only class contains **9 passing tests**.
- Migration transaction/drift plus trigger suite: **59 passed, 0 failed**.
- Full solution suite: **903 shared tests and 20 local-AI tests passed, 0 failed**.
- Protocol walk after dependency-composition change: **6 passed, 2 intentionally skipped, 0 failed**.
- EF pending-model check: **no pending model changes**; the local EF CLI retains its existing
  10.0.2-versus-10.0.11 informational version warning.
- Release build: **0 warnings, 0 errors**.
- `git diff --check`: passed; reported only existing line-ending notices.

## Acceptance coverage

- A 23:00 reminder preserves exact topic/subject/body and ordered live entity links, appears once,
  and remains readable and markable as read through the existing notification store.
- Scheduled, exact-due, completed, cancelled, missed, and superseded statuses are asserted.
- Reschedule, cancellation, repeated polling, two independent worker contexts, database failure,
  bad application/entity scope, legacy migration, EF tampering, and direct SQLite tampering are
  covered with explicit no-partial-change assertions.
- Successful notification firing leaves event, operation, action/effect, and ECS state unchanged.

## Deliberate exclusions and handoff

Recurrence/timezone/DST behavior, world-clock/state/observation consumers, adapters, phone identity,
delegated state-changing authority, schedule administration, public status queries, web/MCP UI,
push delivery, and live database import remain excluded. Slice 6 is the next separately gated leaf
and owns only closed recurrence with the already-confirmed timezone/DST policies.

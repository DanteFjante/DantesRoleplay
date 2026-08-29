# E8 trigger scheduling Slice 7 completion receipt

Status: **accepted**
Completed: 2026-08-25
Ruleset alignment: **ruleset-neutral**
Implementation document: [declared application-state conditions](E8-TRIGGER-SCHEDULING-SLICE-7-IMPLEMENTATION.md)
Dependency tree: [durable scheduling and external triggers](E8-TRIGGER-SCHEDULING-DEPENDENCY-PLAN.md)

## Delivered boundary

- Added immutable, versioned application-scoped definitions for world-clock thresholds and
  declared state conditions. Each definition binds one state space, 1–16 ordered exact
  entity/component version/hash dependencies, lifecycle, activation/re-arm policy, a reviewed
  adapter revision, canonical configuration, and one notification-only target.
- Added the stable generic `system.trigger.closed-scalar` version 1 adapter. It compares one named
  top-level scalar with `eq`, `ne`, `gt`, `gte`, `lt`, or `lte`, supports one optional exact guard,
  rejects paths/unknown fields/duplicate properties, and contains no game or clock field IDs.
- World-clock thresholds require numeric `gte`, rising-edge activation, and manual re-arm. They
  baseline without firing, remain silent when registered already past the threshold, and cannot
  re-fire after correction. The catalog world-clock mechanic remains the only clock-advance owner.
- Extended the atomic application-ECS effect boundary with an optional generic transaction
  participant. Conditional evaluation runs after the whole batch has staged and before audit/commit;
  adapter/fan-out failure rolls the ECS change and trigger evidence back together.
- Candidate selection is indexed from only component add/set/merge/remove or entity deletion keys,
  evaluates each condition once against final staged state, reads only declared exact components,
  and rejects more than 64 candidates. Structural edges and unrelated components select nothing.
- Added rising-edge and armed-level truth transitions, `on-false` and newer-revision manual re-arm,
  deterministic operation-bound fire identities, bounded durable work, retries/leases, immutable
  receipts/notification links, one atomic notification delivery, and internal status evidence.

No event is emitted, no action/effect is delegated, and no world state or clock is mutated by the
trigger service.

## Security review closures

- Registration rejects stale component versions/hashes, missing dependency entities, unavailable
  adapter revisions, malformed canonical configuration, cross-application state spaces, and
  wrong-scope notification entities.
- The closed adapter admits no JSONPath, JavaScript, SQL, nested traversal, uploaded code, or
  object/array comparison. Component removal/deletion becomes false through exact reads rather
  than a state scan.
- SQLite constraints and triggers enforce application/state-space scope, sequential immutable
  definitions/current pointers, legal truth/work transitions, exact work/receipt/link provenance,
  operational non-deletion, and immutable definition/dependency/receipt/link evidence. EF retains
  matching immutable-row protection.
- One ECS operation can create at most one fire per definition revision. Retry, restart, repeated
  polling, two contexts, and already-true level state cannot duplicate a notification or receipt.
- Notification delivery revalidates the current active revision, last fired operation, exact
  target application/state space, and every live linked entity inside the leased root transaction.
- Adapter failure, lease loss, supersession, direct database forgery, and injected transaction
  failure leave no partial application state, truth transition, work, receipt, link, or notification.

## Evidence

- Focused trigger-scheduling suite: **88 passed, 0 failed**.
- Conditional/ECS compatibility subset: **103 passed, 0 failed** before the final focused additions;
  the final full suite includes all additions.
- Migration drift and catalog coverage: **7 passed, 0 failed**.
- Full solution: **944 shared tests and 20 local-AI tests passed, 0 failed**.
- Protocol walk after dependency registration changed: **6 passed, 2 intentionally skipped, 0 failed**.
- EF pending-model check: **no pending model changes**; the existing local EF CLI
  10.0.2-versus-runtime-10.0.11 informational warning remains.
- Release build: **0 warnings, 0 errors**.
- Fresh catalog validation: **144 records valid**, 21 existing advisory near-duplicate warnings;
  no live data touched.
- `git diff --check`: passed; only line-ending notices were reported.

## Acceptance coverage

Exact threshold crossing, already-past baseline, calendar guard, unrelated component suppression,
multi-effect final-state evaluation, rising-edge/level truth, false/manual re-arm, stale contracts,
cross-application scope, adapter failure rollback, deterministic retry identity, migrated-database
provenance, direct tamper rejection, immutable evidence, current status, notification delivery,
repeated polling, and two-context concurrency are asserted. Existing one-time and recurring
schedules, observation ingestion, application execution, ECS structural edges, event authority,
web/MCP behavior, and three verbs remain compatible.

## Deliberate exclusions and handoff

External observation matching, outbound coded listeners, phone/device identity, geofencing,
state-changing targets, public schedule administration/status, web/MCP condition authoring, push
delivery, and live database import remain excluded. Slice 8 external observation matching and its
reviewed adapter/network/secret boundary is the next separately gated leaf.

# E8 trigger scheduling Slice 8 completion receipt

Status: **accepted**
Completed: 2026-08-25
Ruleset alignment: **ruleset-neutral**
Implementation document: [exact external-observation matching](E8-TRIGGER-SCHEDULING-SLICE-8-IMPLEMENTATION.md)
Dependency tree: [durable scheduling and external triggers](E8-TRIGGER-SCHEDULING-DEPENDENCY-PLAN.md)

## Delivered boundary

- Added immutable versioned observation-trigger definitions binding one application, exact current
  source revision, exact current structure revision/hash, lifecycle, reviewed matcher revision,
  canonical closed configuration, and notification-only target.
- Added `system.trigger.observation.closed-scalars` version 1. It requires 1–16 distinct simple
  top-level properties and exact JSON scalar values; all declared values must match. Paths,
  operators, coercion, nested traversal, objects, arrays, scripts, mappings, and uploaded code are
  rejected or cannot match.
- First-time observation admission now stages at most 64 deterministic exact-revision candidates in
  the immutable observation transaction. Replay returns the original evidence before staging, so
  it cannot create duplicate work.
- Added bounded durable matching with eight-item discovery, 60-second leases, three attempts, and
  5/30-second retry delays. False evaluation writes immutable `not-matched` evidence. True
  evaluation atomically commits `matched` evidence, one notification, exact observation provenance,
  and completed work.
- Added source/structure staleness checks, transient/permanent matcher classification, restart-safe
  work, status projections, background hosting, catalog classification, and an additive guarded
  migration.

Observations remain external evidence rather than events. This slice creates no event, action,
effect, world-state change, clock change, device registration, poller, or public administration
surface.

## Security review closures

- Registration requires one uniquely registered startup-reviewed adapter and the exact current
  enabled source/current active structure/hash/permission tuple. Cross-application notification
  state spaces and missing/deleted linked entities are rejected.
- The adapter receives only immutable definition and canonical observation projections. No network
  client, destination, credential/secret port, process service, source selector, or poller is
  registered. Outbound polling remains blocked on explicit network and secret owners.
- Observation-controlled data cannot select a trigger, matcher, target, handler, action, event,
  path, operator, work ID, receipt disposition, or notification content.
- Accepted evidence survives adapter and delivery failure. Transient failure retries the same
  deterministic identity; permanent failure and stale revisions close work without partial
  notification or downstream mutation.
- SQLite constraints/triggers enforce current exact scope, sequential current pointers, legal work
  transitions, work/receipt/link provenance, operational non-deletion, and immutable definitions,
  entity links, receipts, and notification links. EF has matching immutable-row protection.
- The 64-candidate bound rolls back observation and work together. Injected receipt failure rolls
  notification/link/receipt/completion back before retry, and two workers deliver at most once.

## Evidence

- Final trigger-scheduling focused suite: **101 passed, 0 failed**.
- Observation-trigger/ingestion plus migration/catalog subset: **23 passed, 0 failed**.
- Migration drift and catalog coverage: **7 passed, 0 failed**.
- Shared suite excluding the independently failing untracked D&D ability-check class:
  **960 passed, 0 failed**; local-AI suite: **20 passed, 0 failed**.
- The complete shared suite reports **962 passed and 1 failed** across 963 tests. The remaining
  failure is `Dnd2024AbilityCheckTests.Raw_check_rejects_undeclared_input_before_an_output`: its
  assertion expects the phrase `exactly ability and dc`, while the current catalog mechanic emits
  different validation wording. It reproduces alone, is outside this generic trigger slice, and
  was not hidden by changing game-rule code or its test.
- Protocol walk: **6 passed, 2 intentionally skipped, 0 failed**.
- EF pending-model check: **no pending model changes**; the local EF CLI
  10.0.2-versus-runtime-10.0.11 informational warning remains.
- Release build: **0 warnings, 0 errors**.
- Fresh catalog validation: **144 records valid**, with 21 existing advisory near-duplicate
  warnings; no live data touched.
- `git diff --check`: passed; only line-ending notices were reported.

## Acceptance coverage

Exact true/false/type-different matching, closed configuration injection, replay, deterministic
identity, current source and structure staleness, unknown and duplicate adapters, transient and
permanent failures, cross-application notification scope, candidate fan-out rollback, injected
commit rollback/retry, migrated-database immutability/provenance, internal status, no-event
authority, repeated polling, and two-context concurrency are asserted. Existing observation
ingestion and one-time/recurring/conditional triggers remain green in their focused suite.

## Deliberate exclusions and handoff

Phone/device identity, raw GPS, geofencing, outbound polling, network destinations, secret storage,
state-changing targets, public web/MCP management, push delivery, and live database import remain
excluded. Slice 9 phone companion registration/privacy-minimized observations is the next leaf;
Slice 10 remains the public management/final-acceptance boundary.

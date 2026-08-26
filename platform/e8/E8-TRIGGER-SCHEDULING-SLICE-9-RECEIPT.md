# E8 trigger scheduling Slice 9 completion receipt

Status: **accepted**
Completed: 2026-08-25
Ruleset alignment: **ruleset-neutral**
Implementation document: [phone companion identity and privacy-minimized observations](E8-TRIGGER-SCHEDULING-SLICE-9-IMPLEMENTATION.md)
Dependency tree: [durable scheduling and external triggers](E8-TRIGGER-SCHEDULING-DEPENDENCY-PLAN.md)

## Delivered boundary

- Added opaque phone installation IDs, deterministic application-scoped principals, a single
  `privacy-minimized-signals` permission profile, and immutable observation-structure privacy
  classifications. General, raw-location, and third-party-notification-content structures default
  deny for phone registration.
- Added an internal durable registry for exact application/device/source/structure permissions.
  Registration returns a 256-bit credential once and stores only a domain-separated verifier;
  ordinary get/list/status projections expose neither credential nor verifier.
- Added append-only active/revoked lifecycle revisions with a guarded current pointer. Revocation
  is immediate, replay-safe, and cannot reactivate or recover the original credential.
- Added credential authentication to the existing observation route before body parsing. After
  parsing, a device policy rechecks exact source revision, installation ID, structure
  revision/hash/classification, and current active status. Operator submissions retain their
  existing behavior.
- Preserved the existing replay window, canonical request/occurrence identity, schema validation,
  rate limiting, immutable observation evidence, Slice 8 matching, and notification-only delivery.
- Added an atomic migration with device tables, exact FKs/indexes, privacy bounds, immutable rows,
  legal lifecycle transitions, and source/structure scope guards.

This slice adds no registration web/MCP route, phone application, raw GPS history, forwarded phone
notifications, outbound polling, event/action/effect authority, or state-changing target.

## Security review closures

- A credential is 256 random bits, bounded to one dedicated header, domain-separated before
  persistence, compared through an indexed verifier lookup, and never copied to evidence or error
  output. Unknown, revoked, and wrong-application credentials share one denial.
- Device identity does not trust Tailscale alone. The existing private transport check still runs,
  while the device credential supplies the submitting principal.
- The observation route applies the existing bounded web upload policy before device
  authentication; accepted principals additionally retain the exact per-source rate limiter.
- Registration cannot widen source policy: the source must already permit the deterministic device
  principal and every exact structure. Source/structure supersession makes the device stale rather
  than silently rebinding it.
- Only privacy-minimized structures can be paired. The phone cannot select source revisions,
  structure hashes, classifications, trigger handlers, notification content, events, actions,
  effects, or state mutations through its submitted data.
- Registration/status/permission rows are immutable in EF and guarded in migrated SQLite. Direct
  row rewrite, illegal status transition, and current-pointer deletion fail closed. Credential
  collisions retry within a fixed bound and leave no partial second registration.
- Database authentication failure returns a bounded availability error; cancellation remains
  cancellation. Observation durability failures retain the existing safe 503 response.

## Evidence

- Phone/ingestion/matching focused release suite: **24 passed, 0 failed**.
- Complete trigger-scheduling release suite: **109 passed, 0 failed**.
- Web, migration-drift, and catalog-coverage subset: **86 passed, 0 failed**.
- Release build: **0 warnings, 0 errors**.
- EF pending-model check: **no pending model changes**; the local EF CLI
  10.0.2-versus-runtime-10.0.11 informational warning remains.
- Protocol walk: **6 passed, 2 intentionally skipped, 0 failed**.
- Fresh catalog validation: **144 records valid**, with 21 advisory near-duplicate warnings; no
  live data was touched.
- `git diff --check`: passed; only line-ending notices were reported.
- The complete local-AI suite passed **20/20**. The shared full-suite run passed **979/980**; its one
  catalog-tree immutability assertion observed concurrently-added untracked D&D catalog files. The
  same assertion passed in isolation, all Slice 9 and catalog-coverage tests pass, and the D&D files
  were preserved rather than changed or removed by this slice.

## Acceptance coverage

One-time credential handling, current authentication, revocation, deterministic principal binding,
safe readback, exact source/device/structure permissions, in-window delayed evidence, replay,
expiry, stale-source denial, all non-minimized privacy classes, credential collision rollback,
migrated-database tamper guards, authenticate-before-parse behavior, generic credential denial,
operator compatibility, migration drift, component/catalog ownership, and protocol compatibility
are asserted.

## Deliberate exclusions and handoff

Slice 10 remains the authenticated web/MCP device-management and final-acceptance boundary. It may
expose pairing, revocation, and safe status using this internal registry, but must not add raw GPS,
phone-notification forwarding, outbound destinations, source/structure authoring by devices,
credential recovery, ambient event/action/effect authority, or state-changing scheduled targets.

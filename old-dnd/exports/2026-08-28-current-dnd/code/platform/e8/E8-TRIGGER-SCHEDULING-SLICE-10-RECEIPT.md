# E8 trigger scheduling Slice 10 completion receipt

Status: **accepted**
Completed: 2026-08-25
Ruleset alignment: **ruleset-neutral**
Implementation document: [web/MCP management and final acceptance](E8-TRIGGER-SCHEDULING-SLICE-10-IMPLEMENTATION.md)
Dependency tree: [durable scheduling and external triggers](E8-TRIGGER-SCHEDULING-DEPENDENCY-PLAN.md)

## Delivered boundary

- Added one shared trigger-administration contract and SQLite transaction owner for safe bounded
  queries, exact preview, commit, replay, lifecycle revisions, reviewed structure/source runtime
  synchronization, schedule registration, phone pairing, and revocation.
- Added the server-selected `trigger.admin.read` and `trigger.admin.write` capabilities. Web and MCP
  authenticate before parsing and use the same closed
  `{requestToken, operation, applicationId, value}` command.
- Added private no-store web routes below `/api/control/triggers` for application summaries,
  resource/status queries, deterministic phone principal derivation, preview, and apply. Existing
  loopback/Tailscale identity, same-origin mutation checks, and bounded body/rate limits remain in
  force.
- Added `system.trigger-scheduling` to the existing MCP `query` and `commit` catalogs without adding
  a fourth tool. Exact MCP mutation requires the successful dry run of the same canonical command.
- Added a persistent control-center Triggers workspace for current schedules, past fires, a simple
  one-time reminder, privacy-minimized phone pairing, and all eight exact advanced operations.
  Preview enables apply only for the unchanged request body.
- Activated the reviewed control-center bundle as immutable live page revision 9 after exporting
  revision 8. Browser verification confirmed the route, status data, default exact command,
  disabled-before-preview buttons, and zero console errors.

This slice adds no new trigger semantics, direct observation mutation, event/effect/action write,
state-changing target, arbitrary predicate/code, outbound polling, push delivery, raw GPS flow,
forwarded phone-notification flow, secret recovery, retention operation, or phone application.

## Security review closures

- Query resources and results are closed and bounded. Observation projections omit raw data JSON
  and request fingerprints; device projections omit credential verifiers; status/fire projections
  omit lease owners/tokens and unnecessary work internals.
- The command token is exactly 32 lowercase hexadecimal characters. Operation and value properties
  are exact; application scope and authorization capability cannot be supplied inside `value`.
- Preview executes all owner validation inside a rolled-back transaction, returns no phone secret,
  and records fingerprinted evidence. Commit requires that exact evidence.
- Definition/device/current-pointer mutation and the successful audit row share one root SQLite
  transaction. Existing stores join the root instead of independently committing. Injected audit
  failure rolls everything back.
- Exact replay verifies both the successful operation subject and the immutable stored resource.
  Conflicting token reuse fails; concurrent exact commits leave one definition and one audit row.
- A pairing credential is returned only by the first successful `phone.register` response. Preview,
  replay, query, device status, and errors cannot recover it.
- Source credentials cannot call administration. Web routes select their read/write capability,
  reject cross-origin writes, and MCP authorization happens before parsing an invalid body.
- Administration never writes events, effects, actions, observation data, notifications, catalog
  files, paths, destinations, headers, or worker lease state.

## Evidence

- Administration transaction/replay/privacy/concurrency suite: **7 passed, 0 failed**.
- Focused management, phone, web, and MCP suite: **96 passed, 0 failed**.
- Complete trigger-scheduling suite: **119 passed, 0 failed**.
- Authorization, catalog-coverage, and migration-drift subset: **20 passed, 0 failed**.
- Updated canonical system-kind assertion: **1 passed, 0 failed**.
- Current production MCP/web host build: **0 warnings, 0 errors**.
- EF pending-model check: **no pending model changes**; the local EF CLI
  10.0.2-versus-runtime-10.0.11 informational warning remains.
- Protocol walk: **6 passed, 2 intentionally skipped, 0 failed**.
- Fresh catalog validation: **144 records valid**, with 21 advisory near-duplicate warnings; no
  live game data was touched by validation.
- Main test assembly excluding the unrelated Trail Survival simulation class: **1,001 passed,
  2 intentionally skipped, 0 failed**. The separate local-AI suite passed **20/20**.
- `git diff --check`, component JSON parsing, and inline control-center JavaScript syntax passed;
  only line-ending notices were reported.
- Browser smoke test loaded `#/triggers`, two application choices, all eight operations, current
  counts, reminder/pairing forms, and no console error. No trigger-management POST was made against
  live game data during the smoke test.

## Unrelated acceptance observations

- The full shared suite reproducibly has five failures in the untracked
  `TrailSurvivalSimulationTests` class: activated Trail Survival create/run actions currently return
  `Unsupported` where those tests expect `Succeeded`. The focused filter reproduced **1 passed,
  5 failed**. This class and its application-execution/catalog changes are outside Slice 10; no
  Trail files were changed to mask the failure. Close it when the Trail Survival action evaluator
  again resolves its activated create/run mechanics.
- After the clean solution build and Slice 10 test runs, a separate in-progress
  `system-capabilities` refactor changed `ControlStructureExplorer` to capability-backed async
  methods without updating seven existing calls in `WebInterfaceTests`. The current production
  host still builds cleanly, but a new shared-test-project build reports those seven unrelated
  compile errors. Slice 10 does not restore the removed direct explorer API or overwrite that
  concurrent refactor.

## Acceptance coverage

Matching web/MCP projections, all eight registrations, immutable lifecycle revision, dynamic
notification status, phone principal/pairing/secret/replay/revocation, auth-before-parse, wrong
capability/origin denial, exact dry run, request-token conflict, concurrent submission, audit
rollback, raw-observation omission, three-tool compatibility, migration drift, catalog ownership,
protocol walking, and desktop browser navigation are asserted.

## Deliberate exclusions and handoff

The downstream notification-only trigger plan is complete through Slice 10. A future slice may add
state-changing scheduled or external actions only after durable scoped delegated authorization has
its own confirmed owner and expiry/revocation/replay contract. Outbound polling, secret storage,
push delivery, raw-location profiles, forwarded phone notifications, retention execution, and a
phone companion application remain separate future decisions.

# Trail Game TG2 Slice 3 receipt — canonical run-domain acceptance

Status: **accepted through scoped equivalent automated invariant evidence**
Completed: **2026-08-25**
Implementation: [TG2 Slice 3](TG2-SLICE-3-IMPLEMENTATION.md)
Parent: [TG2 run domain](TG2-RUN-DOMAIN-DEPENDENCY-PLAN.md)
Ruleset alignment: **ruleset-neutral**

## Delivered boundary

- Added metadata/schema pairs for `trail-survival.policy`,
  `trail-survival.pending-choice`, and `trail-survival.outcome`.
- Completed the confirmed eleven-type run domain: scenario pin, run, clock, route progress, party,
  member, conveyance, resources, policy, pending choice, and outcome.
- Added one governing `procedure.trail-survival.run-state` procedure that preserves the boundary
  between canonical state and later mechanics/projections.
- Proved all eleven metadata/schema pairs parse, compile under the bounded schema profile, accept
  representative closed values, reject malformed/boundary/derived-field witnesses, register as
  exact Trail-owned version-1 types, and replay without appending versions.
- Proved every type round-trips through the generic application-scoped ECS using exact version/hash
  references, an invalid value leaves no component, and both directions of cross-application use
  reject.
- Preserved TG1 application/source/activation/catalog/state-space behavior as the source expanded;
  the private real-host onboarding walk remains on the existing three-verb protocol.

## Acceptance evidence

- Focused TG1/TG2 plus real-host onboarding suite: **6 passed, 0 failed**.
- Current-source TG2 run-domain suite from isolated output: **2 passed, 0 failed**.
- Full shared suite after recompiling the test assembly: **905 passed, 0 failed** against the
  repository's normal verified dependency outputs.
- Standalone local-AI suite: **20 passed, 0 failed**.
- Current-source isolated solution build: **0 warnings, 0 errors**.
- Disposable catalog validation: **144 records valid**, 21 existing advisory warnings, no errors,
  and no live data touched.
- Authored audit: **11 metadata/schema pairs**, 22 JSON files parsed, all Trail Game local links
  resolved, and no owned-file trailing whitespace or scoped diff error.

The focused tests directly assert the complete TG2 state-contract invariant and provide equivalent
confirmation for this application-owned schema boundary.

## Verification context

The working tree contains unrelated concurrent trigger-scheduling migration work. A fresh-output
attempt of the entire current-source suite reports that work's pending model changes, while moving
test output outside the repository also breaks older tests that locate catalog files by walking up
from their binaries. TG2 does not modify the database model or migrations. Acceptance therefore
combines a warning-free current-source build and current-source TG2 tests with the 905-test normal
output compatibility run; no unrelated migration file was changed to manufacture a green result.

## Deliberate exclusions and next boundary

No authored scenario, route graph, market, resource/event definition, state fixture, mechanic,
seed/cursor, action, calculation, transition, query/projection, UI, migration, startup registration,
normal-database mutation, or external code/asset was added. TG3 must separately plan and confirm the
first root transaction, deterministic seed/replay contract, mechanic/procedure IDs, derived inputs,
typed effects, failure behavior, and rollback evidence.


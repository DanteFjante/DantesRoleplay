# Trail Game TG1 Slice 3 implementation — package isolation and coexistence acceptance

Status: **accepted 2026-08-25**; [receipt](TG1-SLICE-3-RECEIPT.md)
Owner/roadmap: [Customizable trail-survival application roadmap](TRAIL-GAME-ROADMAP.md)
Dependency tree/leaf: [TG1 application package / TG1.3](TG1-APPLICATION-PACKAGE-DEPENDENCY-PLAN.md)
Ruleset alignment: **ruleset-neutral**
Source ID and locator: **not applicable; package/isolation acceptance only**
Outcome: Close TG1 by proving a zero-application database remains empty and independently activated
`trail-survival` and `dnd2024` sources/catalogs/state spaces coexist without cross-application
catalog or state leakage.
Exclusions: Full `dnd2024` adoption, new Trail state/schema/mechanics, public catalog publication,
startup/live installation, migration, UI, or public-surface changes.
Allowed files/areas: `DantesRoleplay.Tests/TrailSurvivalApplicationSeamTests.cs` for one focused
zero-app/coexistence acceptance case; TG1 plan/receipt/status documents.
Stop point: Stop after TG1 acceptance evidence and receipt; do not begin TG2.

## Confirmed decisions

- Coexistence uses the already authored Trail Survival procedure and one existing `dnd2024`-owned
  world-time procedure as minimal independent catalog witnesses. It does not repeat full legacy
  adoption.
- Each application has no base application, its own source registration, preview, activation,
  qualified catalog record, and empty state space.
- The test uses a fresh disposable database and repository source resolver; no normal database or
  startup configuration changes.
- Final TG1 acceptance may use automated evidence because the test asserts the complete bounded TG1
  package invariant. TG2 remains separately gated.

## External implementation reference

No external implementation applies. The `dnd2024` procedure is existing repository-owned evidence
and no D&D rule meaning is interpreted or changed.

## Prerequisite evidence

- [TG1 Slice 1 receipt](TG1-SLICE-1-RECEIPT.md) proves the authored source/internal seam.
- [TG1 Slice 2 receipt](TG1-SLICE-2-RECEIPT.md) proves the exact real-host operator sequence.
- [Legacy ownership ratification](../platform/application-kernel/LEGACY-OWNERSHIP-RATIFICATION.md)
  proves the selected `game.core.*` procedure belongs to `dnd2024` and remains reference/other-app
  authority.

## Runtime artifacts

Add no runtime or catalog artifact. Add one disposable integration test only.

## Authoritative state and behavior

1. A fresh database lists zero applications.
2. Register `trail-survival` and `dnd2024` independently, each with no base application.
3. Register the exact Trail source glob and exact existing `dnd2024` time-procedure path.
4. Preview and activate both through the same generic services.
5. Materialize exactly one qualified procedure per application.
6. Bind one empty state space to each exact active fingerprint.
7. Prove application-scoped discovery returns only its own binding.
8. Prove a catalog navigator rejects another application's request/qualified record.
9. Prove no ECS entity/component exists and no source path crosses the confirmed boundaries.

## Failure, replay, and rollback contract

Existing registration/source/activation/state-space failures retain their no-change behavior from
Slices 1–2. This acceptance case specifically fails if either preview includes the other source,
either materialized record has the wrong application qualification, either state-space page leaks,
or a cross-application catalog request succeeds.

## Implementation sequence

1. Add the focused zero-app/two-app acceptance case using current generic services.
2. Run TG1 focused tests, catalog validation, full shared/local-AI suites, build, link checks, and
   TG1 diff checks.
3. Write the TG1 Slice 3/final TG1 receipt, collapse completed TG1 detail, update the roadmap once,
   and stop before TG2.

## Acceptance matrix

| Concern | Required evidence |
| --- | --- |
| Zero app | Fresh database has no registered application. |
| Two app | Both independent sources preview/activate/materialize. |
| Catalog isolation | One correctly qualified record per app; cross-app inspect/request rejects. |
| State isolation | One empty binding per app; discovery never returns the other's binding. |
| Data boundary | No ECS entity/component and no cross-owned winner path. |
| Compatibility | Existing full suite, local-AI suite, catalog validation, and build pass. |
| Surface/live | No public/startup/normal-database change. |

## Verification commands

- Focused `TrailSurvivalApplicationSeamTests`.
- Focused real-host onboarding walk.
- Disposable `roleplay validate catalog`.
- Full shared and standalone local-AI suites.
- Warning-free solution build.
- Markdown-link and TG1 `git diff --check` checks.

## Completion receipt and exit gate

Record `TG1-SLICE-3-RECEIPT.md` and update the TG1/root roadmaps to accepted. Stop before any TG2
schema/ID/state or TG3 mechanic work.

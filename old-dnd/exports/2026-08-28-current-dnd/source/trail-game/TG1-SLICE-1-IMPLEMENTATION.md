# Trail Game TG1 Slice 1 implementation — minimal independent application seam

Status: **accepted 2026-08-25**; [receipt](TG1-SLICE-1-RECEIPT.md)
Owner/roadmap: [Customizable trail-survival application roadmap](TRAIL-GAME-ROADMAP.md)
Dependency tree/leaf: [TG1 application package / TG1.1](TG1-APPLICATION-PACKAGE-DEPENDENCY-PLAN.md)
Ruleset alignment: **ruleset-neutral**
Source ID and locator: **not applicable; original application metadata only**
Outcome: Prove one real authored `trail-survival` source can be registered, previewed, activated,
materialized, replayed, and bound to an empty isolated state space using existing generic owners.
Exclusions: Simulation schemas/mechanics, entities, scenario content, startup/live registration,
migrations, public kinds/routes, UI, external code, and `dnd2024` changes.
Allowed files/areas: `catalog/applications/trail-survival/` for one procedure record;
`DantesRoleplay.Tests/TrailSurvivalApplicationSeamTests.cs` for disposable seam evidence; TG0/TG1
planning and receipt documents.
Stop point: Stop after the one-record catalog and disposable seam tests pass; do not begin TG1.2 or
TG2.

## Confirmed decisions

- [TG0](TG0-PRODUCT-CONTRACT-CONFIRMATION.md) confirms application ID `trail-survival`, display name
  `Trail Survival`, no base application, source ID `trail-survival-core`, authored placement
  `catalog/applications/trail-survival/`, and procedure ID `procedure.trail-survival.about`.
- The procedure is descriptive metadata only. It does not promise or implement game behavior.
- The source registration uses the existing opaque `workspace` allowed root in a disposable test.
- No direct external implementation code or asset is reused.

## External implementation reference

No external implementation review applies. This slice contains only original descriptive metadata
and generic-kernel integration evidence.

## Prerequisite evidence

- [Application-kernel completion receipt](../platform/application-kernel/receipts/APPLICATION-KERNEL-COMPLETION-RECEIPT.md)
  proves application registration, sources/overlays, preview/activation, application-scoped ECS,
  catalog navigation, and generic execution foundations.
- [Legacy ownership ratification](../platform/application-kernel/LEGACY-OWNERSHIP-RATIFICATION.md)
  proves `game.core.*` remains `dnd2024`-owned and cannot be reused as Trail Survival runtime state.
- Current source, preview, activation, catalog-navigation, and ECS tests prove the named generic
  owner seams.

## Runtime artifacts

Add exactly one permanent application-owned catalog procedure:

- ID: `procedure.trail-survival.about`
- category: `trail-survival.application`
- status: `active`
- purpose: describe application identity, isolation, and current no-gameplay boundary

Add no component definition, schema, mechanic, JavaScript, event, subscription, entity,
relationship, migration, database bootstrap, route, or public protocol kind.

## Authoritative state and closed input

The authored procedure file is source authority. Disposable SQLite owns only test registrations,
activation evidence, and the empty state-space binding. Test inputs are exact constants confirmed
by TG0; the scanner derives file hashes/lengths, preview derives manifest fingerprints, activation
derives its fingerprint, and the state-space binding consumes the exact activated application
revision/fingerprint.

No caller supplies catalog parse success, winner hashes, activation result, or cross-application
authority.

## Behavior, result, and typed effects

1. Register `trail-survival` with no base applications.
2. Register trusted source `trail-survival-core` at
   `catalog/applications/trail-survival/**/*` under a test-only `workspace` root resolver.
3. Scan and preview twice; both results are valid, deterministic, contain exactly the authored
   procedure, and expose no `dnd2024`, system, world, component, mechanic, or fixture path.
4. Dry-run then activate the exact preview and replay the same request token; activation is stable.
5. Materialize the active catalog and inspect exactly the qualified procedure record.
6. Create and read one empty state space bound to the exact application revision and active manifest
   fingerprint; `dnd2024` application discovery returns no Trail Survival state space.

This slice executes no application action and produces no ECS effect beyond the generic empty
state-space binding in disposable test state.

## Failure, replay, and rollback contract

- Unknown allowed root produces a closed invalid preview and no activation/state space.
- A stale or different registration cannot reuse the immutable application/source IDs.
- Activation requires exact dry-run evidence and exact preview fingerprint.
- Replay returns the prior activation and never appends a second activation revision.
- Cross-application state-space discovery exposes no Trail Survival binding.
- No failure changes authored files or any normal/live database.

## Implementation sequence

1. Add and parse the descriptive procedure record.
2. Add focused disposable positive/determinism/replay/catalog/state-space/isolation tests.
3. Add focused invalid-root/no-activation/no-state test.
4. Run focused tests, repository catalog validation, full shared suite if the focused seam passes,
   solution build, local link/diff checks, and record a receipt.
5. Stop before TG1.2 or TG2.

## Acceptance matrix

| Concern | Required evidence |
| --- | --- |
| Positive | Exact real source previews, activates, materializes, and binds an empty state space. |
| Determinism | Repeated preview fingerprints/winners match. |
| Catalog | Exactly one qualified `trail-survival` procedure is inspectable. |
| Replay | Duplicate exact activation returns prior receipt/revision. |
| Failure/no-change | Unknown root cannot activate or create state. |
| Isolation | No base application, no forbidden winner path, and no state-space leak to another application. |
| Compatibility | Existing central catalog remains valid and `dnd2024` files/state are unchanged. |
| Surface | No new public kind, route, startup registration, or production C# branch. |

## Verification commands

- Focused `TrailSurvivalApplicationSeamTests`.
- `roleplay validate catalog` against its fresh disposable database.
- Full `DantesRoleplay.Tests` suite after focused success.
- Warning-free solution build where unrelated worktree changes permit it.
- Local Markdown-link check and `git diff --check` for TG1-owned files.

## Completion receipt and exit gate

Record results in `TG1-SLICE-1-RECEIPT.md`, mark this slice accepted only after explicit acceptance,
update TG1/root roadmap status once, and stop before operator onboarding, simulation state, mechanics,
content, UI, migration, or live installation.

# Trail Game TG2 Slice 1 implementation — run spine schemas

Status: **accepted 2026-08-25**; [receipt](TG2-SLICE-1-RECEIPT.md)
Owner/roadmap: [Customizable trail-survival application roadmap](TRAIL-GAME-ROADMAP.md)
Dependency tree/leaf: [TG2 run domain / TG2.1](TG2-RUN-DOMAIN-DEPENDENCY-PLAN.md)
Ruleset alignment: **ruleset-neutral**
Source ID and locator: **not applicable; original Trail Survival contracts**
Outcome: Add the governing run-state procedure and the scenario-pin, run, clock, and route-progress
component contracts, with bounded schema/registration evidence.
Exclusions: Party/member/conveyance/resources/policy/choice/outcome schemas, fixtures, mechanics,
actions, calculations, transitions, UI, migration, public surface, startup, or live state.
Allowed files/areas: `catalog/applications/trail-survival/procedures/`,
`catalog/applications/trail-survival/components/`, one focused TG2 test file, TG1 forward-compatible
test assertions, and TG2 plan/receipt statuses.
Stop point: Stop after four component types compile/register/replay and their representative valid
and invalid values pass; do not begin TG2.2 in this slice.

## Confirmed decisions

- IDs and meanings are confirmed in [TG2 run-domain confirmation](TG2-RUN-DOMAIN-CONFIRMATION.md).
- Metadata follows `ComponentDefinitionFile`; each schema is an exact sibling sidecar.
- Schemas use the bounded Draft 2020-12 profile and contain no rules or derived projections.
- The Trail source glob remains unchanged and automatically includes these files.

## External implementation reference

No external implementation applies. These are original ruleset-neutral state contracts.

## Prerequisite evidence

- [TG1 receipt](TG1-SLICE-3-RECEIPT.md) proves the application/source/activation/state-space seam.
- The existing `ecs`, `schema-validation`, `component-type-administration`, and `catalog` owners
  provide the generic storage, versioning, validation, and file contracts.

## Runtime artifacts

- `procedure.trail-survival.run-state`
- `trail-survival.scenario-pin` metadata/schema
- `trail-survival.run` metadata/schema
- `trail-survival.clock` metadata/schema
- `trail-survival.route-progress` metadata/schema

## Authoritative state and closed input

The schema values are the closed shapes in the confirmation. Callers cannot supply component type
versions, schema hashes, application ownership, ECS revision, application activation fingerprint,
or derived display/progress values.

## Behavior, result, and typed effects

This slice defines no behavior or effect. Generic registration derives immutable schema versions and
hashes. Generic ECS validation will later gate values against those exact references.

## Failure, replay, and rollback contract

Malformed schemas or values reject without a component-type/state mutation. Re-registering identical
schema bytes replays version 1. A changed schema would append a new version through existing generic
semantics and is outside this slice.

## Implementation sequence

1. Add the governing procedure and four metadata/schema pairs.
2. Add focused parse, compile, valid/invalid, ownership, registration, and replay tests.
3. Update the TG1 source winner assertion so additive non-public catalog files do not invalidate TG1.
4. Validate the catalog and focused suite, write the receipt, and stop.

## Acceptance matrix

| Concern | Required evidence |
| --- | --- |
| Authored pair | Each metadata ID matches filename/schema registration ID. |
| Schema | All four compile under the bounded profile. |
| Values | Representative valid values pass; missing/extra/wrong-type/out-of-bound values fail. |
| Registry | Exact `trail-survival` ownership, version 1, stable hash, identical replay. |
| Source | Application preview includes all new files without changing source registration. |
| TG1 compatibility | About procedure remains materialized/navigable after source expansion. |
| Isolation/live | Disposable database only; no other-app type or normal database mutation. |

## Verification commands

- Focused TG1/TG2 tests using an isolated build output if the running private host locks its normal
  output directory.
- Disposable `roleplay validate catalog`.
- Full suite/build only at TG2 final acceptance.
- Markdown link and owned-file whitespace checks.

## Completion receipt and exit gate

Record `TG2-SLICE-1-RECEIPT.md`, mark TG2.1 accepted, activate TG2.2 in a separate implementation
document, and do not add party/inventory artifacts before that boundary.

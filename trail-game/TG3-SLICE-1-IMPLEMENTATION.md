# Trail Game TG3 Slice 1 implementation — scenario contract and create-run transaction

Status: **accepted 2026-08-25**; [receipt](TG3-SLICE-1-RECEIPT.md)
Owner/roadmap: [Customizable trail-survival roadmap](TRAIL-GAME-ROADMAP.md)
Dependency tree/leaf: [TG3 simulation / TG3.1](TG3-SIMULATION-DEPENDENCY-PLAN.md)
Ruleset alignment: **ruleset-neutral**
Source ID and locator: **not applicable; original Trail Survival rule contracts**
Outcome: Register immutable scenario rule data and create a completely pinned nested run through
one exact, replay-safe application action.
Exclusions: Trade/daily/travel/event/terminal mechanics, authored scenario fixture, UI/public/MCP
surface, migration, startup change, or live state.
Allowed files/areas: Trail application catalog, TG3 focused tests, and Trail planning/receipts.
Stop point: Accept TG3.1 and activate TG3.2; do not implement journey/event behavior in this slice.

## Confirmed decisions

All identities, state meanings, inputs, seed behavior, and transaction ownership are confirmed in
[TG3 simulation confirmation](TG3-SIMULATION-CONFIRMATION.md).

## External implementation reference

No external implementation applies; no outside source or asset is copied.

## Prerequisite evidence

[TG2 acceptance](TG2-SLICE-3-RECEIPT.md) proves the eleven canonical state types and isolated
application ECS. Generic application execution already provides exact active mechanic lookup,
closed projections, Jint seeds, typed-effect translation, atomic rollback, audit, and replay.

## Runtime artifacts

- `trail-survival.scenario` metadata/schema.
- Revised `trail-survival.run` schema with `randomSeed` and `seedCursor`.
- `procedure.trail-survival.simulation`.
- `mechanic.trail-survival.run.create` Markdown/JavaScript pair.
- Focused catalog/schema/sandbox/application-runner tests.

## Authoritative state and closed input

Role `scenario` exposes only `trail-survival.scenario`. Input has exactly `runId`, `partyId`,
`partyName`, `conveyanceId`, and `members`; every member has exactly `entityId`, `name`, and
`roleId`. Scenario content derives pin, clock, route, policy, resources, health, and conveyance.

## Behavior, result, and typed effects

Validate scenario relational invariants and the host seed, then create run/party/member/conveyance
entities, add all initial components, and create the run→party→member/conveyance containment graph.
The generic runner translates and atomically applies the one ordered effect list.

## Failure, replay, and rollback contract

Malformed/extra/duplicate IDs, invalid scenario semantics, invalid seed, existing entity, stale
catalog/activation, wrong application/state space, or injected effect failure leaves no partial
run. Exact operation replay returns the prior receipt without duplicate effects.

## Implementation sequence

1. Add scenario and revised run contracts plus governing procedure.
2. Add standalone create mechanic with closed validation and generic effects.
3. Prove schema/materialization, deterministic output, exact runner commit/replay, and rollback.
4. Validate catalog, record receipt, and activate TG3.2 only after the slice passes.

## Acceptance matrix

| Concern | Required evidence |
| --- | --- |
| Valid setup | Complete nested state equals scenario-derived values. |
| Closed boundary | Extra/malformed/duplicate identities and corrupt scenario reject. |
| Determinism | Same projection/seed produces byte-identical output. |
| Replay/stale | Same identity replays; changed fingerprint or existing IDs reject unchanged. |
| Rollback | An injected late collision leaves zero created run artifacts. |
| Isolation | Wrong application/state space/fingerprint cannot execute. |
| Compatibility | Existing TG1/TG2 tests, catalog validator, and full suite pass. |

## Verification commands

Run focused Trail TG3 tests, disposable catalog validation, full shared/local-AI suites, a
warning-free solution build, and owned link/whitespace/diff checks. No protocol walk.

## Completion receipt and exit gate

Record `TG3-SLICE-1-RECEIPT.md`, mark this accepted, collapse TG3.1 in the dependency plan, and
activate one TG3.2 implementation document.

# Trail Game TG3 Slice 3 implementation — travel, events, arrivals, and terminal transitions

Status: **accepted 2026-08-25**; [receipt](TG3-SLICE-3-RECEIPT.md)
Owner/roadmap: [Customizable trail-survival roadmap](TRAIL-GAME-ROADMAP.md)
Dependency tree/leaf: [TG3 simulation / TG3.3](TG3-SIMULATION-DEPENDENCY-PLAN.md)
Ruleset alignment: **ruleset-neutral**
Source ID and locator: **not applicable; original Trail Survival rules**
Outcome: Add the complete deterministic journey transition and event-choice resolution.
Exclusions: Authored starter scenario, browser/public/MCP seam, migrations, startup, live state, or
final full-suite acceptance.
Allowed files/areas: Trail mechanics/tests and TG3 documents.
Stop point: Accept journey behavior, then activate TG3.4 acceptance only.

## Confirmed decisions

The permanent IDs and full seed/input/effect boundary are confirmed in
[TG3 simulation confirmation](TG3-SIMULATION-CONFIRMATION.md).

## External implementation reference

No external implementation applies.

## Prerequisite evidence

[TG3.1](TG3-SLICE-1-RECEIPT.md) and [TG3.2](TG3-SLICE-2-RECEIPT.md) prove exact activated setup,
nested projections, scenario parity, seeds, atomic effects, daily commands, replay, and rollback.

## Runtime artifacts

Mechanics `.travel` and `.event.choose` plus focused headless tests.

## Authoritative state and closed input

Travel input is exactly `{ "legId": <string|null> }`: a string starts an authored outgoing leg,
while null continues the canonical active leg. Event choice is exactly one offered `choiceId`.
No event, roll, distance, cost, delta, arrival, or result enters input.

## Behavior, result, and typed effects

Travel consumes ration food, applies health and conveyance wear, advances time/distance, arrives at
landmarks, draws a weighted event from the action seed, opens one pending choice, and stores derived
victory/defeat. Choice applies the authored resource/member/conveyance/time deltas, removes pending
state, and may terminate. Each root updates turn/cursor once in one generic effect transaction.

## Failure, replay, and rollback contract

Malformed/extra input, invalid leg/choice, wrong phase/seed/pin, insufficient resources, pending
command conflict, terminal command, corrupt bounds, stale revision, and injected failure reject
without partial change. Choice is the only pending-compatible command.

## Implementation sequence

1. Add travel with route/policy/party derivation and seeded event draw.
2. Add offered event choice resolution and terminal derivation.
3. Extend activated tests through pending blocking, arrival, victory, defeat, replay, and no-change.
4. Record receipt and activate TG3.4 final acceptance.

## Acceptance matrix

No-event/event paths, deterministic draw, wrong next leg, pending block, invalid choice, resource and
health/wear boundaries, arrival, victory, defeat, terminal block, replay/stale, and rollback.

## Verification commands

Focused Trail tests and warning-free isolated build; TG3.4 owns final catalog/full-suite acceptance.

## Completion receipt and exit gate

Record `TG3-SLICE-3-RECEIPT.md`, mark accepted, and activate TG3.4 acceptance only.

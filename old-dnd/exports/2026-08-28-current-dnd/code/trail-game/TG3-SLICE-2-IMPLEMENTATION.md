# Trail Game TG3 Slice 2 implementation — trade, policy, rest, and forage

Status: **accepted 2026-08-25**; [receipt](TG3-SLICE-2-RECEIPT.md)
Owner/roadmap: [Customizable trail-survival roadmap](TRAIL-GAME-ROADMAP.md)
Dependency tree/leaf: [TG3 simulation / TG3.2](TG3-SIMULATION-DEPENDENCY-PLAN.md)
Ruleset alignment: **ruleset-neutral**
Source ID and locator: **not applicable; original Trail Survival rules**
Outcome: Execute four exact non-journey commands from one pinned run-root projection.
Exclusions: Travel, event draw/choice, arrival, terminal results, authored fixture, UI/public/MCP,
migration, startup, or live state.
Allowed files/areas: Trail mechanics/tests and TG3 documents.
Stop point: Accept the four commands and activate TG3.3; do not add journey behavior in this slice.

## Confirmed decisions

IDs, seed derivation, caller inputs, scenario authority, pending/terminal blocking, and generic
transaction ownership are confirmed in [TG3 simulation confirmation](TG3-SIMULATION-CONFIRMATION.md).

## External implementation reference

No external implementation applies.

## Prerequisite evidence

[TG3.1 acceptance](TG3-SLICE-1-RECEIPT.md) proves scenario pinning, nested containment projection,
seed/cursor state, exact activated execution, replay, and rollback.

## Runtime artifacts

Mechanics `.trade`, `.policy.set`, `.rest`, and `.forage`, with focused headless tests only.

## Authoritative state and closed input

All commands bind only role `run`, follow its scenario pin, and materialize the bounded run→party
graph. Trade input is exactly mode/resource/quantity, policy input exactly pace/ration, and
rest/forage input `{}`. Prices, weight, food, healing, time, yield, and seed are derived.

## Behavior, result, and typed effects

Trade enforces landmark market, affordability, stock, overflow, and cargo capacity. Policy admits
only scenario IDs. Rest consumes food and heals living members. Forage draws one seeded bounded
yield and enforces storage bounds. Each updates run turn/cursor once; timed actions update clock.

## Failure, replay, and rollback contract

Malformed/extra input, wrong phase, pending/terminal state, pin mismatch, wrong seed, missing graph,
unknown policy/offer, insufficient resource/capacity, overflow, stale revisions, and injected
effect failure reject with no partial change. Exact operation replay is idempotent.

## Implementation sequence

1. Add the shared declared run-root projection to four standalone mechanics.
2. Implement closed validations and one set effect per changed component.
3. Extend the activated headless harness through positive, negative, seed, replay, and no-change cases.
4. Record receipt and activate TG3.3.

## Acceptance matrix

Positive derivation, malformed/extra input, wrong seed/phase, pending/terminal blocking, zero/max and
overflow/capacity boundaries, deterministic forage, replay/stale, rollback, and TG3.1 compatibility.

## Verification commands

Focused Trail tests and warning-free isolated build; final TG3 owns catalog/full-suite acceptance.

## Completion receipt and exit gate

Record `TG3-SLICE-2-RECEIPT.md`, mark accepted, and activate one TG3.3 implementation document.

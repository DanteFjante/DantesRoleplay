# Trail Game TG3 Slice 5 implementation — boundary and acceptance hardening

Status: **accepted through equivalent automated invariant evidence**
Owner/roadmap: [Customizable trail-survival roadmap](TRAIL-GAME-ROADMAP.md)
Dependency tree/leaf: [TG3 simulation / TG3.5](TG3-SIMULATION-DEPENDENCY-PLAN.md)
Ruleset alignment: **ruleset-neutral**
Source ID and locator: **not applicable; original Trail Survival rules**
Outcome: Strengthen TG3 acceptance evidence without expanding its behavior or entering TG4.
Exclusions: New IDs, schema meanings, formulas, mechanics, scenario fixtures, balance changes,
public/browser/MCP surfaces, migrations, startup, live state, or generic runtime changes.
Allowed files/areas: TG3 tests and TG3 plans/receipts only.
Stop point: Record the hardening receipt, restore TG3 to accepted, and leave TG4 inactive.

## Confirmed decisions

TG3's existing IDs, state meanings, command semantics, seed contract, and transaction ownership
remain unchanged. This slice adds equivalent automated evidence only.

## External implementation reference

No external implementation applies.

## Prerequisite evidence

[TG3 final acceptance](TG3-SLICE-4-RECEIPT.md) proves the complete headless loop, deterministic
state, exact audit, replay, rollback, and compatibility before this hardening pass.

## Runtime artifacts

No production artifact. Extend `TrailSurvivalSimulationTests` and correct stale prospective wording
in the accepted TG3 dependency plan.

## Authoritative state and closed input

Tests continue to use disposable scenario ECS state. They may vary schema-valid data to reach
boundaries but may not add an authored TG4 fixture or make caller input authoritative for outcomes.

## Behavior, result, and typed effects

Prove the existing implementation at the 32-member setup/effect limit, unaffordable and
capacity-exceeding trades, a multi-turn active-leg continuation requiring null `legId`, and an
event choice whose unavailable resource cost preserves pending state. No behavior changes are
planned; any discovered defect must remain inside the confirmed TG3 semantics.

## Failure, replay, and rollback contract

Every negative witness compares canonical state before/after and requires no partial mutation.
Existing replay, exact audit, and collision rollback evidence must remain green.

## Implementation sequence

1. Correct stale TG3 plan status/evidence text and activate this slice.
2. Add focused boundary and no-change cases using the existing activated harness.
3. Run focused tests, full shared/local-AI suites, build, and authored-file audit.
4. Record `TG3-SLICE-5-RECEIPT.md` and restore TG3 acceptance.

## Acceptance matrix

| Concern | Required evidence |
| --- | --- |
| Setup maximum | 32 members produce a valid batch below the 128-effect ceiling. |
| Trade failures | Affordability and capacity failures preserve canonical bytes. |
| Partial route | String starts the leg, null continues it, wrong repeated string rejects unchanged. |
| Event cost | Insufficient resource rejects and retains the exact pending choice. |
| Compatibility | Existing TG3, full shared/local-AI suites, and warning-free build remain green. |

## Verification commands

Focused Trail tests, full shared/local-AI suites, warning-free isolated solution build, JSON/link/
whitespace/diff audit. Retry catalog validation only if the unrelated migration condition clears.

## Completion receipt and exit gate

Record the receipt, mark this accepted, collapse TG3.5 to verified, restore roadmap/parent status to
TG3 accepted with TG4 next, and stop.

Completion evidence: [TG3 Slice 5 receipt](TG3-SLICE-5-RECEIPT.md).

# Trail Game TG2 confirmation — canonical run-domain meanings and IDs

Status: **confirmed**
Confirmed: **2026-08-25**
Authority: the user's instruction to review TG1 and finish TG2, applying the roadmap's recommended
original, ruleset-neutral boundary.
Application: **`trail-survival`**

## Confirmed state model

Each component is application-owned, versioned by the generic component registry, stored only in a
Trail Survival state space, and closed with `additionalProperties: false`.

| Permanent ID | Canonical meaning |
| --- | --- |
| `procedure.trail-survival.run-state` | How to inspect and preserve authoritative run-domain state without inferring mechanics. |
| `trail-survival.scenario-pin` | Immutable selected scenario ID, positive version, content hash, and rules-profile ID. |
| `trail-survival.run` | Run lifecycle phase, monotonic turn number, and owning party entity ID. |
| `trail-survival.clock` | Total in-world elapsed minutes; calendar/day presentation is derived from scenario content. |
| `trail-survival.route-progress` | Selected route, current landmark, optional active leg, distance into that leg, and visited landmarks. |
| `trail-survival.party` | Party name, ordered unique member entity IDs, and optional conveyance entity ID. |
| `trail-survival.member` | Member name, role ID, presence status, current health points, and condition IDs. |
| `trail-survival.conveyance` | Conveyance kind, operational status, current/max condition, and cargo capacity. |
| `trail-survival.resources` | Bounded resource-ID/quantity entries; total weight/value/consumption are derived. |
| `trail-survival.policy` | Selected authored pace and ration policy IDs. |
| `trail-survival.pending-choice` | One unresolved event ID, bounded offered choice IDs, and opening turn. Absence means none. |
| `trail-survival.outcome` | Terminal victory/defeat kind, authored cause ID, and reached turn. Absence means non-terminal. |

## Semantic boundaries

- Component schemas own structural validity, not game-rule validity across components.
- TG3 JavaScript mechanics will own phase transitions, monotonicity, resource/health/capacity
  calculations, route eligibility, policy effects, choice resolution, victory, and defeat.
- `scenarioVersion` and `scenarioContentHash` pin authored content. Application activation and state
  space binding separately pin application/source/schema context.
- `elapsedMinutes` is the only canonical clock scalar. Day number, date labels, and time-of-day are
  projections.
- `distanceIntoLeg` is canonical progress on the active leg. Remaining distance and percentages are
  projections from route content.
- Resource entries contain no weight, price, or consumption rate copies.
- Member and conveyance status coexist with bounded health/condition state; TG3 owns legal
  combinations and transitions.

## Explicit exclusions

No component fixture, scenario/route/resource/event definition, mechanic, seed/cursor contract,
action, typed effect, query/projection, UI, migration, startup registration, live database mutation,
or external code/asset is confirmed by this decision.


# World Feature 14 dependency plan — multi-leg on-foot journey planning

Status: **Feature 14 verified**  
Last updated: 2026-08-20

## Target capability

A trusted GM can ask for a marked traveller to reach a distant stored location. The system returns
a read-only ordered on-foot itinerary made from existing active, open, directed Feature 8 routes,
starting at the traveller's actual containment location. It contains one route per leg, total fixed
minutes, and the root-clock revision it was calculated against.

The itinerary is advice, not a movement command or stored journey state. To travel it, each leg is
resolved through the existing Feature 8 journey action. Before every next leg, the caller obtains a
fresh itinerary from the traveller's actual location. A newly closed route or later travel blocker
therefore stops progress at that location; the system never skips locations, batch-moves, or
advances time for an untraversed leg.

### Included

- One trusted-GM `journey-plan` query for one traveller, one world, and one destination.
- Deterministic shortest-total-duration search over active, open, world-scoped Feature 8 `on-foot`
  routes only.
- Stable leg ordering, total minutes, already-there, unreachable, and blocked/no-open-path results.
- A continuation rule: re-plan after every accepted leg using current containment and root-clock
  revision, never a prior itinerary as authorization.
- Public-query, route graph, deterministic tie-break, stale-plan, blocking, and no-write tests.

### Excluded

- Executing more than one leg in an action; creating a journey entity; automatic/background travel;
  reservations; parties; carts; dragons; mixed-mode paths; path geometry; distance; speed; terrain;
  weather; player authorization; or map UI.
- Rations, fatigue, money, cargo, camping, random rolls, bandit encounters, combat, or automatic
  GM narration. Each needs its own owner and pre-leg interruption contract.
- Changes to Feature 8 route meaning, Feature 10 scheduling, containment, root-clock policy, item
  ownership, character creation, events, subscriptions, migrations, or generic graph-query meaning.

## Source and contract basis

| Authority | Evidence | Decision supplied |
| --- | --- | --- |
| Repository workflow | `AGENTS.md`; `procedure.system.verify` | Confirmation, validation, and public-surface acceptance gates. |
| Bounded reads | [Feature 7 plan](../feature-07/WORLD-FEATURE-07-DEPENDENCY-PLAN.md) | Trusted-GM query discipline; W14 adds a purposeful itinerary query instead of asking callers to infer paths from partial reads. |
| One-leg travel | [Feature 8 plan](../feature-08/WORLD-FEATURE-08-DEPENDENCY-PLAN.md) and `procedure.game.core.world.travel` | Directed on-foot route truth, derived current location, atomic leg movement/time, and stale-origin rejection. |
| Blocked routes | [Feature 10 plan](../feature-10/WORLD-FEATURE-10-DEPENDENCY-PLAN.md) | Route availability is authoritative for whether a leg can begin. |
| Clock evidence | `procedure.game.core.world.time` and Feature 5 receipt | The plan labels root-clock revision but never reserves or changes time. |
| Other travel modes | Features 12 and 13 plans | Conveyance and flight own separate contracts and are not coerced into this on-foot planner. |

## Ownership and confirmation boundary

`procedure.game.core.world.travel` remains the owner of route/journey semantics and is revised to
define itinerary eligibility, deterministic selection, staleness, and the one-leg continuation
rule. Feature 14 does not change the Feature 8 action or create a second movement mechanism.

Confirm this public vocabulary, result bounds, and tie-break before implementation:

| Artifact | Proposed meaning |
| --- | --- |
| `query(kind: "journey-plan")` | Trusted-GM read-only query that produces one bounded on-foot itinerary from a traveller's actual location to a specified destination. |
| `JourneyPlanQuery` / `JourneyPlanProjection` | Implementation-local request/response types, not catalog records or durable world state. |
| `procedure.game.core.world.travel` revision | Declares eligible route records, query input/output, no-write/staleness semantics, and deterministic selection. |

~~~text
query(kind: "journey-plan")
{
  worldId: existing active world-root ID,
  travellerId: existing active traveller ID,
  destinationId: existing active location ID
}
~~~

No caller supplies an origin, route IDs, legs, duration, adjacency, availability, effects, or clock
value. Origin is derived from traveller containment. The first delivery permits at most 20 legs and
14,400 total declared minutes. Exceeding either returns `too-long`, never a partial itinerary.

## Closed planning result

~~~text
JourneyPlanProjection
{
  status: "ready" | "already-there" | "unreachable" | "blocked" | "too-long",
  worldId: existing ID,
  travellerId: existing ID,
  originId: existing location ID,
  destinationId: existing location ID,
  clockRevision: non-negative integer,
  legs: [{ routeId, fromId, toId, durationMinutes }],
  totalDurationMinutes: non-negative integer
}
~~~

The result is closed. `ready` contains 1–20 ordered legs; `already-there` has no legs and zero
minutes. `unreachable`, `blocked`, and `too-long` have no legs and zero minutes. `unreachable`
means no directed route-graph path exists; `blocked` means a graph path exists but no currently
open path exists. The response never reveals a hidden route as a workaround.

An eligible edge has valid active Feature 8 route data, one valid world/from/to link, active sibling
endpoints with canonical adjacency, `on-foot` mode, positive duration, and active/open availability.
The sequence minimises total duration. A tie is broken by lexicographic route-ID sequence, then
destination IDs, so equivalent calls return the same itinerary.

`clockRevision` is advisory. It locks nothing. The consumer re-queries after every accepted Feature
8 leg; that action remains final authority and rechecks location, route, availability, and clock.

## Dependency order and slices

~~~text
World Feature 14: one read-only multi-leg on-foot itinerary
├─ W5 root clock                                                       [verified]
├─ W7 trusted-GM bounded reads/public-query discipline                 [must be verified]
├─ W8 directed on-foot route and atomic one-leg journey                [must be verified]
├─ W10 route availability / blocked-leg proof                          [must be verified]
├─ confirmed itinerary public contract, caps, and tie-break            [implemented]
│  └─ Slice 1: query surface and deterministic route planner           [verified]
└─ verified itinerary query                                             [parent: Slice 1]
   └─ Slice 2: continuation handoff and stale/blocking evidence        [implementation verified]

Supplies, encounters, parties, carts, dragon flight, mixed-mode travel, and automatic execution
[excluded]. Feature 16 owns the later mode-aware itinerary selection; W14 remains the reusable
on-foot-only foundation.
~~~

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Itinerary query | W7/W8/W10 are verified; query ID, result grammar, caps, and tie-break are confirmed. | **Verified:** the public query returns one stable on-foot route sequence or an exact empty-status result without writing state. See the [Slice 1 receipt](WORLD-FEATURE-14-SLICE-1-RECEIPT.md). |
| 2 | One-leg continuation handoff | Slice 1 is verified. | **Implementation verified:** a tested caller pattern plans, executes exactly one Feature 8 leg, re-plans, and stops cleanly when the next route is closed or unusable. See the [implementation receipt](WORLD-FEATURE-14-IMPLEMENTATION-RECEIPT.md). |

## Slice 1 — deterministic itinerary query

| Artifact | Change |
| --- | --- |
| Public query surface | Add `journey-plan` with the closed request/result grammar and capability entry. |
| Travel procedure | Revise `procedure.game.core.world.travel` for eligibility, duration minimisation, tie-break, caps, and no-write semantics. |
| Read implementation | Add a bounded path-planning read service over existing route, relationship, availability, containment, and clock reads. It creates no world-specific persistent table. |
| Focused test | Add `WorldJourneyPlanTests` or nearest query owner for paths, ties, cycles, corrupt links, closed routes, caps, and no-write behavior. |

### Slice 1 acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Distant destination | A reachable three-leg fixture returns route/endpoints in order, summed minutes, and current root-clock revision. |
| Tie/cycle | Equal-duration alternatives choose the specified stable route-ID sequence; cycles terminate and do not duplicate legs. |
| Already there | Traveller at destination returns `already-there`, no legs, and no clock/action/event change. |
| Route truth | Reversed, inactive, malformed, cross-world, non-adjacent, non-on-foot, or invalid-availability routes are never selected. |
| Blocking | A closure removing the only otherwise valid path yields `blocked`; an absent directed path yields `unreachable`. |
| Bounds | A path beyond either cap yields `too-long`, never a partial plan. |
| No-write/public surface | Calls leave components, containment, relationships, events, notifications, operations, and clock unchanged; capability and protocol-walk tests prove grammar. |

## Slice 2 — explicit per-leg continuation

The user-facing continuation is intentionally small:

1. Request an itinerary to the destination.
2. Execute only its first Feature 8 route leg.
3. Read resulting containment and request a new itinerary.
4. Stop and surface the result if it is `blocked`, `unreachable`, or `too-long`.

This is the later insertion point for ration and encounter rules: before a subsequent leg, their
own feature may make the leg unavailable or create an explicit interruption. They may not make a
prior itinerary silently succeed.

### Slice 2 acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Three-leg journey | Three separately audited Feature 8 actions move through intermediate locations and advance time once per accepted leg. |
| Mid-journey closure | After leg one, a Feature 10 closure makes the fresh plan `blocked`; no second-leg movement/time occurs. |
| Stale prior plan | A cached second leg is not authorization; Feature 8 rejects invalid current location or availability. |
| Future interruption boundary | No ration, encounter, combat, inventory, party, cart, dragon, or mixed-mode behavior is fabricated here. |
| Repository acceptance | Focused tests, `roleplay validate catalog`, public-query protocol walk, full suite, and `git diff --check` pass. |

## Completion boundary

Feature 14 is complete when a trusted GM can request a bounded distant on-foot itinerary and conduct
the journey one existing route action at a time, with a fresh plan required between legs. Stop before
supplies or encounters. Those must be separately planned before “rations ran out” or “bandits
approach” becomes a real travel blocker rather than narrative text.

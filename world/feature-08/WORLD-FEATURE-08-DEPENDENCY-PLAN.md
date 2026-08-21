# World Feature 8 dependency plan — named routes and on-foot journeys

Status: **Feature 8 verified**  
Last updated: 2026-08-20

## Target capability

A marked traveller may take one named, active, one-way route between two stored adjacent locations.
The route declares the sole initial travel mode, `on-foot`, and a fixed positive duration in
in-world minutes. The journey action validates the traveller's derived current location, the route
endpoints, canonical adjacency, route scope, and root clock; then it atomically moves the traveller
and advances the same root clock by the route's declared duration.

Current location remains containment. The route records no traveller state, clock value, position,
map geometry, path, distance, party, or campaign copy. A return trip needs a separate authored
route in the opposite direction.

### Included

- One closed `game.core.world.route` component on a route entity.
- Three directed empty-data route relationships: route-to-world, route origin, and route
  destination.
- One Feature 8 fixture route from the existing gate to the existing market.
- One `on-foot` journey mechanic with exact empty input and five declared roles.
- A governed two-effect action: traveller `containment.move` and root-clock `component.set` in one
  transaction, with existing structural events and action audit as evidence.
- Fresh-import, direct-route, duration, route-scope, rollback, replay, and no-change coverage.

### Excluded

- Pathfinding, multi-leg travel, return-route inference, route selection, transport inventory,
  groups/parties, speed, encumbrance, distance units, terrain, weather, random encounters,
  conditions, schedules, waiting, and background advancement.
- Coordinates, drawn maps, map anchors, line of sight, fog-of-war, player authorization, website
  UI, and player-safe read filtering.
- A new generic travel table, migration, event type, subscription, notification, or MCP query
  kind. Existing action routing, effects, audit, and structural events remain sufficient.
- Changing Feature 2 adjacent movement. It stays a no-time, no-route local relocation capability.

## Source and contract basis

| Authority | Evidence | Decision supplied |
| --- | --- | --- |
| Feature workflow | `AGENTS.md`; `procedure.system.create-feature`; `procedure.system.verify` | Repository mode, semantic confirmation, catalog validation, focused/full acceptance evidence, and no persistent import during ordinary development. |
| Verified adjacent movement | `procedure.game.core.world.travel`, `mechanic.game.core.world.location.move`, [Feature 2 receipt](../feature-02/WORLD-FEATURE-02-SLICE-2-RECEIPT.md) | Traveller eligibility/current containment, active sibling locations, canonical adjacency, one action transaction, and the existing no-route boundary. |
| World clock | [Feature 5 plan](../feature-05/WORLD-FEATURE-05-DEPENDENCY-PLAN.md) | Root-owned closed clock, monotonic minutes/revision, bounds, and structural-event/audit evidence. Feature 8 starts only after its implementation receipt confirms the final clock contract. |
| World topology | `procedure.game.core.world.location` and Feature 1 receipt | Locations remain containment/topology owners; `connected-to` remains an empty-data canonical adjacency record. |
| Mechanics/action | `procedure.mechanic.write`; `procedure.action.run`; `procedure.mechanic.projection` | Explicit roles, frozen containment/relationship projection, deterministic effects, replay, and all-or-nothing application. |
| World changes | `procedure.world.change` | A single action's effects are ordered and validated as one transaction; later failure leaves neither travel nor time. |
| Future spatial boundary | [World/lore post-foundation roadmap](../../WORLD_AND_LORE_PLAN.md) | W9 consumes stable route/topology records; geometry never replaces them. |

## Ownership and confirmation boundary

The existing `procedure.game.core.world.travel` remains the travel owner and is revised to govern
route authoring and the journey action alongside Feature 2's adjacent movement. Feature 8 does not
create a parallel travel procedure.

The Feature 5 time procedure is revised only to permit this declared route mechanic to perform the
same validated monotonic root-clock replacement from route-derived minutes. It remains the sole
owner of clock data meaning, numeric bounds, calendar identity, and administrative correction.
Feature 8 must not introduce a second clock shape or an independent time policy.

The user confirmed the following permanent vocabulary and exact semantics on 2026-08-20:

| Artifact | Proposed meaning |
| --- | --- |
| `game.core.world.route` | Closed metadata for one directed, world-scoped travel route: lifecycle, summary, descriptive visibility, the initial `on-foot` mode, and fixed duration minutes. |
| `game.core.world.route.in-world` | Directed empty-data relationship from a route entity to exactly one active world root. |
| `game.core.world.route.from` | Directed empty-data relationship from a route entity to exactly one active origin location. |
| `game.core.world.route.to` | Directed empty-data relationship from a route entity to exactly one active destination location. |
| `mechanic.game.core.world.route.travel-on-foot` | Active deterministic action that validates one matching route and moves one active traveller while advancing its route-scoped root clock. |
| `route.feature-08.gate-to-market-on-foot` | The first reviewed fixture route, directing gate → market in the existing world in 30 in-world minutes. |

The relationship data for all three route links is exactly `{}`. A route's direction comes only from
its distinct `from` and `to` kinds; `connected-to` stays undirected and does not imply a return
route or duration.

## Closed route and action contracts

### Route component

~~~text
game.core.world.route
{
  status: "active" | "archived",
  summary: trimmed text, 1–1,000 Unicode scalar values,
  visibility: "public" | "party" | "gm",
  mode: "on-foot",
  durationMinutes: integer, 1–1,440 inclusive
}
~~~

All fields are required and the record is closed. Missing, `null`, arrays, scalars, unknown keys,
untrimmed/empty text, invalid status/visibility/mode, fractional values, zero, negatives, values
above 1,440, or an inactive route reject. The first feature has exactly one mode, so the component
does not carry an unbounded mode list, speed, access rule, condition, cost currency, distance, or
route geometry.

Each active route has exactly one `in-world` link, one `from` link, and one `to` link. The three
target entities must exist; the world carries `game.core.world.root` with active state, while both
endpoints carry active `game.core.world.location` state. Origin and destination must be distinct
sibling locations in slot `location`, have the same direct container, and have exactly one existing
canonical `connected-to` relationship. A route cannot link itself, reverse a convention, use
nonempty data, cross worlds, or share the same endpoint in both roles.

### Journey action

The mechanism declares exactly these roles:

| Role | Required projection | Purpose |
| --- | --- | --- |
| `traveller` | `game.core.world.traveller` | Must be active and currently contained at the claimed origin in `presence`. |
| `origin` | `game.core.world.location` with relationships | Claimed current location and canonical adjacency evidence. |
| `destination` | `game.core.world.location` | Claimed route destination. |
| `route` | `game.core.world.route` with relationships | Active route data plus its in-world/from/to evidence. |
| `world` | `game.core.world.root` and verified `game.core.world.clock` | Route scope and the only clock that may advance. |

Input is exactly `{}`. The caller supplies no route direction, mode, minutes, destination result,
clock value, slot, adjacency, or effects; the five explicit roles identify the reviewed world
records to validate.

The rule validates, in order:

1. All five roles are distinct where their types require it, their closed component data is valid,
   and traveller/origin/destination/world status is active.
2. The traveller's derived containment is origin/`presence`; origin and destination are active
   sibling `location` entities.
3. Origin has exactly one canonical `connected-to` edge to destination.
4. Route has exactly one valid `in-world` edge to world, one `from` edge to origin, and one `to`
   edge to destination. It accepts neither a reversed route nor a merely adjacent unselected route.
5. The world clock satisfies the verified Feature 5 closed contract. Compute
   `nextMinute = currentMinute + route.durationMinutes` and `nextRevision = revision + 1`;
   reject on either Feature 5 overflow boundary.

On success, return exactly these ordered effects:

1. `containment.move` of traveller to destination in slot `presence`.
2. Complete `component.set` replacement of the same world clock, preserving `calendarId` and using
   computed `currentMinute` and `revision`.

The route, traveller marker, location data, adjacency, root data, relationships, and all
knowledge/faction/campaign/quest state remain unchanged. The one action produces the existing
containment-moved and component-replaced structural events under one root operation; no semantic
`journey-completed` event is added until a later proven consumer requires one.

## Dependency order and slices

~~~text
World Feature 8: named route with deterministic on-foot journey
├─ W1 active topology and canonical adjacency                         [verified]
├─ W2 traveller marker, containment movement, relationship projection [verified]
├─ W5 root clock and clock bounds/audit                               [verified]
├─ existing action/effect transaction and structural events           [implemented]
├─ confirmed route vocabulary, fixture duration, and time handoff     [semantic boundary]
│  └─ Slice 1: route state, links, fixture, and convention tests     [verified]
└─ verified route foundation                                           [parent: Slice 1]
   └─ Slice 2: on-foot journey action and atomic time/location tests  [implemented]

W7 reads may consume route records later; W9 map geometry, conditions, groups, and routes beyond
one leg are excluded.
~~~

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Route foundation | W5 is verified; route IDs, endpoint conventions, fixture duration, and time-procedure handoff are confirmed. | **Verified** — [Slice 1 receipt](WORLD-FEATURE-08-SLICE-1-RECEIPT.md): fresh import contains the valid directed 30-minute gate→market route; invalid fixture conventions are rejected without changing established world state. |
| 2 | On-foot journey | Slice 1 is verified. | **Verified** — [Feature 8 receipt](WORLD-FEATURE-08-IMPLEMENTATION-RECEIPT.md): one action changes only traveller containment and root clock atomically; invalid, reversed, stale, inactive-route, and overflow paths leave both unchanged. |

## Slice 1 — route foundation

| Artifact | Change |
| --- | --- |
| Component definition/schema | Add `game.core.world.route` with the closed route contract above. |
| Governing procedure | Revise `procedure.game.core.world.travel` for route scope, directional endpoint conventions, and the distinct Feature 2 versus Feature 8 action boundaries. Revise the Feature 5 time procedure only for the approved journey-clock handoff. |
| Fixture | Add `route.feature-08.gate-to-market-on-foot`, its active route component, and three canonical relationships. Do not change existing location components, containment, adjacency, traveller, knowledge, faction, or clock fixture bytes. |
| Focused test | Add `CatalogWorldFeature8Tests` or the nearest world catalog test owner for fresh import/readback and invalid disposable fixture conventions. |

### Slice 1 acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Fresh import | One active route has valid closed metadata, scope, origin, and destination; it directs gate → market and names only `on-foot` with the confirmed duration. |
| Closed data | Missing/extra/non-object/invalid text/status/visibility/mode/minute data rejects. |
| Endpoint convention | Missing/duplicate/reversed/self/non-location/inactive/cross-world/nonempty-data route links reject. |
| Topology coupling | Same endpoint, non-sibling endpoints, or endpoints without exactly one canonical adjacency reject. |
| Isolation | Existing root/location/traveller/faction/knowledge components, containment, and adjacency are unchanged; a route is neither a location nor a replacement travel edge. |
| Repository | Focused test and `roleplay validate catalog` pass; no persistent import occurs. |

## Slice 2 — deterministic on-foot journey

Add the mechanic `.md`/`.js` pair. Its match phrases cover natural language such as
`travel the gate-to-market road` and `walk from gate to market` without outranking the existing
generic adjacent-movement phrases for an action that lacks the required `route` and `world` roles.

The source must preserve the existing Feature 2 local-move rule. It may repeat only the clock
replacement calculation authorized by the revised time procedure; it must not invent a second
calendar, minute bound, or mutable audit field. Every required component is parsed/closed-validated
from the frozen projection before an effect is proposed.

### Slice 2 acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Happy path | Active traveller at gate, active direct route gate→market, and root clock at minute 0/revision 0 yields exactly two effects: move to market/`presence` then clock minute `durationMinutes`/revision 1. |
| Direction | Gate→market route cannot be used market→gate; a separate reviewed reverse route is required. |
| Route binding | Unknown, inactive, malformed, wrong-world, wrong-origin, wrong-destination, duplicated-link, or non-adjacent route rejects with zero effects. |
| Traveller/topology | Missing/inactive traveller, stale origin, invalid location, missing/corrupt/reversed adjacency, or non-sibling endpoints rejects with no location/time change. |
| Clock | Missing/malformed/inactive-root/overflow clock rejects with no movement; minute/revision always advance together on success. |
| Closed call | Missing/extra/non-object input; missing/duplicate/wrong role assignments; caller-supplied mode/minutes/effects all reject. |
| Replay/determinism | On the same fresh fixture, identical route state/input/seed yields the same proposed two effects. Repeating after success with the old origin or route direction cannot move or advance time again. |
| Atomic evidence | Success has one action operation and the two normal structural events under its root correlation. Any injected invalid second effect rolls back traveller containment, clock state, events, and success audit together. |
| Feature isolation | The Feature 2 adjacent-move action still moves without advancing time or requiring a route; F8 changes no campaign/quest/faction/knowledge/map state. |
| Repository acceptance | Focused action tests, `roleplay validate catalog`, full suite, and `git diff --check` pass. Run a protocol walk only if the existing MCP/dependency registration surface changes. |

## Completion boundary

Feature 8 is complete when the confirmed route fixture imports cleanly, the on-foot journey action
proves a legal one-route atomic location/time transition, and all invalid/directional/replay/overflow
paths prove no partial progress. Stop before more modes, reverse inference, distances, map
coordinates, multi-stop itinerary planning, conditions, parties, or background travel.

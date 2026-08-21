# World Feature 13 dependency plan — generic aerial-conveyance journey

Status: **Feature 13 verified**  
Last updated: 2026-08-20

## Target capability

One world-owned aerial conveyance and one marked rider can take one named, directed aerial route between
two explicitly authored landing locations. The route is not required to follow ground adjacency,
roads, ground routes, or map connector lines. A successful action co-moves rider and conveyance to
the destination and advances the root clock by duration derived from route distance and conveyance speed.

This is a generic aerial-conveyance proof. The initial dragon is a fixture, not the only valid
aerial conveyance. A winged mount, airship, flying carpet, or setting-specific craft uses the same
contract through its entity name, summary, and speed. The conveyance is persistent world state only for this
journey: it has no combat statistics, personality/AI, ownership, taming, passengers, cargo,
flight altitude, free-flight movement, terrain rules, or pathfinding.

### Included

- One `game.core.world.aerial-conveyance` component with explicit air mode and speed.
- One closed `game.core.world.aerial-route` component and world/from/to links for a directed
  aerial route with distance.
- One dragon fixture co-located with the existing traveller and two explicit active landing
  locations from the current world fixture.
- One deterministic rider-and-conveyance journey mechanic that moves rider, conveyance, and root clock
  atomically.
- Fresh-import, landing/route scope, non-adjacent endpoints, co-location, clock, replay, rollback,
  and no-change coverage.

### Excluded

- Free flight, arbitrary destination selection, altitude, range, distance, path geometry,
  airspace/obstacles, weather, aerial combat, tactical maps, line of sight, take-off/landing
  checks, conveyance health/AI/attitude, ownership/taming, passengers, cargo, rider skill, party
  movement, mounts/items/vehicles as character possessions, or creatures as player characters.
- Reusing `game.core.world.route`, ground adjacency, road routes, cart routes, or map visual lines
  as flight truth. Aerial route topology is explicit and separate.
- Browser map interaction, player authorization, audience filtering, migrations, new event types,
  subscriptions, notifications, schedulers, or a new MCP kind.

## Source and contract basis

| Authority | Evidence | Decision supplied |
| --- | --- | --- |
| Repository workflow | `AGENTS.md`; `procedure.system.create-feature`; `procedure.system.verify` | Confirmation, validation, test, and acceptance boundaries. |
| Conveyance co-travel | [Feature 12 revision](../feature-12/WORLD-FEATURE-12-GROUND-CONVEYANCE-PLAN.md) | Generic ground-conveyance co-location and distance/speed clock proof; W13 reuses the pattern but not ground route semantics. |
| Traveller/clock | Feature 2 movement; `procedure.game.core.world.time`; Features 5 and 8 plans | Marked traveller/current containment and bounded root-clock replacement/audit behavior. |
| Location topology | `procedure.game.core.world.location` and Feature 1 receipt | Existing active locations are landing endpoints; containment remains the location authority. |
| World structures/mechanics | `procedure.world.model`; `procedure.world.change`; `procedure.mechanic.write`; `procedure.action.run` | Components and explicit relationships, frozen roles, deterministic effects, and full transaction rollback. |
| Map boundary | Features 7 and 9 plans | A visual map may show a flight connector later, but no anchor/line is flight authorization or path geometry. |
| Character/item boundary | Character Creation and Items plans | Ownership, riding equipment, and player-created conveyances are intentionally absent from this world fixture. |

## Ownership and confirmation boundary

Revise `procedure.game.core.world.travel` to govern the first aerial route and generic aerial action.
It remains the shared travel owner, but does not merge aerial data into the ground-route component.

The user confirmed these permanent IDs and exact fixture values on 2026-08-20:

| Artifact | Proposed meaning |
| --- | --- |
| `game.core.world.aerial-conveyance` | Closed active/archived aerial-conveyance state with air mode, descriptive metadata, and positive speed; dragon is fixture data only. |
| `game.core.world.aerial-route` | Closed directed aerial journey metadata with air mode, distance, lifecycle, summary, and descriptive visibility. |
| `game.core.world.aerial-route.in-world` | Directed empty-data link from aerial route to exactly one active world root. |
| `game.core.world.aerial-route.from` | Directed empty-data link from aerial route to exactly one active launch/landing location. |
| `game.core.world.aerial-route.to` | Directed empty-data link from aerial route to exactly one active destination landing location. |
| `mount.feature-13.dragon` | The first world-owned dragon mount entity. |
| `aerial-route.feature-13.gate-to-observatory` | The first reviewed aerial route, gate → observatory, which may be non-adjacent by ground topology. |
| `mechanic.game.core.world.aerial-conveyance.travel` | Active deterministic action that co-moves one rider and one aerial conveyance while deriving root-clock minutes from distance/speed. |

The fixture dragon starts at gate/`presence`; the route is gate → observatory with distance **600
units** and speed **30 units/minute**, deriving **20 minutes** in Slice 2. The route links are all
exactly `{}`. The `from` and `to` endpoints are the only permitted
launch/landing locations for this route; no separate landing-site component is introduced in this
first slice. A reverse flight needs another authored route. A ground-adjacent pair may also have a
flight route, but its adjacency is neither required nor sufficient.

## Closed aerial-conveyance, aerial-route, and action contracts

~~~text
game.core.world.aerial-conveyance
{
  status: "active" | "archived",
  summary: trimmed text, 1–1,000 Unicode scalar values,
  visibility: "public" | "party" | "gm",
  travelMode: "air",
  speedUnitsPerMinute: integer, 1–10,000 inclusive
}

game.core.world.aerial-route
{
  status: "active" | "archived",
  summary: trimmed text, 1–1,000 Unicode scalar values,
  visibility: "public" | "party" | "gm",
  travelMode: "air",
  distanceUnits: integer, 1–1,000,000 inclusive
}
~~~

Both records are closed. Missing, `null`, arrays/scalars, extra keys, unknown status/mode/
visibility, invalid text, non-integer/zero/negative/out-of-range speed or distance, or archived
conveyance/route rejects. Neither record contains owner, rider, passenger, cargo, current location,
location ID, world ID, altitude, terrain, flight path, health, action economy, campaign/quest, or
event/audit state.

The aerial conveyance and rider are each directly contained by the same active origin location in `presence`.
The mechanic declares exactly six roles:

| Role | Required projection | Purpose |
| --- | --- | --- |
| `rider` | `game.core.world.traveller` | Active traveller co-located at the aerial-route origin. |
| `conveyance` | `game.core.world.aerial-conveyance` | Active air conveyance co-located at the origin. |
| `origin` | `game.core.world.location` | Exact route launch location; ground adjacency is not read. |
| `destination` | `game.core.world.location` | Exact route landing location; it may be non-adjacent. |
| `aerialRoute` | `game.core.world.aerial-route` with relationships | Active aerial route and its exact world/from/to links. |
| `world` | `game.core.world.root` and `game.core.world.clock` | Route scope and root-clock advance. |

Input is exactly `{}`. The caller cannot supply a destination, flight path, distance, duration,
altitude, conveyance/rider state, effect, landing permission, or random result.

After closed-state, co-location, scope, and clock validation, success returns exactly:

1. `containment.move` conveyance → destination / `presence`;
2. `containment.move` rider → destination / `presence`;
3. complete `component.set` root clock replacement with preserved calendar identity, computed minute,
   and incremented revision.

It derives minutes by ceiling distance/speed and does not read or require `connected-to`,
ground/on-foot routes, anchors, terrain, or map data.
This makes the non-adjacent flight authorization explicit and reviewable rather than accidental.

## Dependency order and slices

~~~text
World Feature 13: generic aerial-conveyance journey
├─ W1 locations/containment                                             [verified]
├─ W2 traveller marker                                                  [verified]
├─ W5 root clock                                                        [must be verified]
├─ W12 multi-entity conveyance/clock journey proof                      [must be verified]
├─ confirmed aerial-conveyance/aerial-route vocabulary, endpoints, distance/speed [implemented]
│  └─ Slice 1: aerial components, links, fixture, tests                         [verified]
└─ verified aerial foundation                                           [parent: Slice 1]
   └─ Slice 2: atomic rider/conveyance/clock journey action            [implementation verified]

Ground adjacency, free flight, dragon simulation, and player mount ownership [excluded]
~~~

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Aerial foundation | W5/W12 are verified; aerial-conveyance/aerial-route IDs, endpoints, distance/speed are confirmed. | **Verified:** fresh import proves one dragon fixture under the generic contract and one explicit non-adjacent aerial route; invalid endpoint/scope data is rejected. See the [Slice 1 receipt](WORLD-FEATURE-13-SLICE-1-RECEIPT.md). |
| 2 | Aerial-conveyance journey | Slice 1 is verified. | **Implementation verified:** one legal action co-moves conveyance + rider and derives root-clock time; road/adjacency assumptions, stale/co-location/scope/clock failures leave all unchanged. See the [implementation receipt](WORLD-FEATURE-13-IMPLEMENTATION-RECEIPT.md). |

## Slice 1 — aerial foundation

| Artifact | Change |
| --- | --- |
| Component definitions/schemas | Add `game.core.world.aerial-conveyance` and `game.core.world.aerial-route` with the exact closed contracts. |
| Governing procedure | Revise `procedure.game.core.world.travel` for explicit aerial route/landing conventions; preserve Feature 2, 8, and 12 behavior. |
| Fixture | Add one dragon at gate/`presence` and one world-scoped gate→observatory flight route with its three canonical links. Do not change ground route, adjacency, clock, faction, knowledge, condition, map, item, or character state. |
| Focused test | Add `CatalogWorldFeature13Tests` or the nearest world catalog owner for import/readback and invalid aerial-conveyance/aerial-route conventions. |

### Slice 1 acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Fresh import | One active dragon fixture is at gate/`presence`; one active gate→observatory aerial route has air mode, valid world/from/to links, and reviewed distance. |
| Closed state | Invalid conveyance/route field, status, mode, speed, or distance rejects. |
| Aerial topology | Non-adjacent endpoints are valid only through the explicit flight route. A ground adjacency alone creates no flight permission. |
| Landing scope | Missing/duplicate/reversed/self/non-location/inactive/cross-world/nonempty route links reject. |
| Isolation | Existing on-foot/cart route data, containment topology, clock, traveller, conditions, map anchors, factions, knowledge, item, and character state remain unchanged. |

## Slice 2 — deterministic aerial-conveyance journey

### Slice 2 acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Happy path | Co-located active rider/conveyance at gate use the explicit gate→observatory aerial route; exactly three effects move conveyance, move rider, then advance root clock. |
| Non-adjacent proof | The action succeeds for the approved non-adjacent endpoints and never reads `connected-to`. |
| Landing/direction | Reversed roles, an unlisted landing location, or missing/invalid aerial route rejects even if the locations are adjacent or map-connected. |
| Co-location/lifecycle | Conveyance/rider elsewhere, wrong slot, archived/malformed conveyance/route, inactive origin/destination, or identity collision rejects. |
| Clock/replay | Missing/corrupt/overflow clock rejects; repeating the success with old origin cannot move rider/conveyance or advance time twice. |
| Rollback/evidence | An invalid effect rolls back both moves, clock, structural events, and success audit. Success has three normal structural events under one root action. |
| Boundary | No passenger, cargo, ownership, taming, health, combat, condition, weather, path, altitude, map, campaign, quest, notification, or authorization behavior is added. |
| Repository acceptance | Focused action tests, `roleplay validate catalog`, full suite, and `git diff --check` pass. |

## Completion boundary

Feature 13 is complete when one reviewed aerial route moves one rider and generic aerial conveyance
atomically with clock time derived from distance/speed, with all invalid landing/scope/co-location/
replay/rollback paths preserving state. Stop before free flight, conveyance agency, player ownership,
passengers, combat, flight geometry, or an aerial map.

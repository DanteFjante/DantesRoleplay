# World Feature 12 revision — generic ground conveyance journey

Status: **Feature 12 verified**  
Last updated: 2026-08-20

## Product decision

Feature 12 is a generic **ground conveyance** system, not a horse-cart system. A route stores
distance; a conveyance stores speed; the action derives elapsed minutes as
`ceiling(distanceUnits / speedUnitsPerMinute)`. The initial horse cart is merely a fixture. A
sled, wagon, car, rover, or another setting's ground vehicle uses the same contract through its
entity name, description, and speed.

Ground is deliberately the only travel mode here. Air, water, and space require later explicit
mode/route plans, so a descriptive name such as “dragon cart” never silently grants flight.

## Contract basis and ownership

| Authority | Result |
| --- | --- |
| `AGENTS.md` | Confirm IDs/schema meaning, validate a disposable catalog, and run full acceptance only when complete. |
| `procedure.game.core.world.travel`; Features 2/8 | Containment remains current location; explicit directed links/actions own travel; local and on-foot behavior stays unchanged. |
| `procedure.game.core.world.time`; Feature 5 | The root clock is bounded and replaced atomically; callers cannot supply elapsed time. |
| `procedure.world.model`; `procedure.world.change` | Conveyances/routes are entities/components/relationships; co-location is containment, not copied location state. |
| [Feature 13 plan](../feature-13/WORLD-FEATURE-13-DEPENDENCY-PLAN.md) | Ground conveyance does not grant aerial travel; flight remains separately explicit. |

Revise travel/time rather than adding a parallel transport/time owner. No MCP surface, event type,
subscription, migration, inventory ownership, passenger/cargo, terrain, or vehicle simulation is
included.

## Confirmation boundary

The user confirmed these permanent IDs and fixture values on 2026-08-20:

| Artifact | Proposed meaning |
| --- | --- |
| `game.core.world.conveyance` | Closed active/archived generic ground-conveyance state with descriptive metadata and positive speed; not an item, actor, mount, or route. |
| `conveyance.feature-12.horse-cart` | First reviewed ground-conveyance fixture, co-located with the existing traveller at the gate. |
| `game.core.world.conveyance-route` | Closed active/archived directed ground-route state with descriptive metadata and positive distance; it contains no vehicle type, speed, duration, or clock. |
| `game.core.world.conveyance-route.in-world` | Directed empty-data route → active world-root link. |
| `game.core.world.conveyance-route.from` | Directed empty-data route → active origin-location link. |
| `game.core.world.conveyance-route.to` | Directed empty-data route → active destination-location link. |
| `conveyance-route.feature-12.gate-to-market-ground` | First reviewed ground route, gate → market, distinct from Feature 8's on-foot route. |
| `mechanic.game.core.world.conveyance.travel-ground` | Action that co-moves one active driver and active ground conveyance while deriving root-clock minutes from distance/speed. |

Proposed fixture values: route distance **300 units**, horse-cart speed **15 units/minute**. The
derived elapsed time is **20 minutes**. They are fixture values, not global constants.

## Closed contracts

~~~text
game.core.world.conveyance
{
  status: "active" | "archived",
  summary: trimmed text, 1–1,000 Unicode scalar values,
  visibility: "public" | "party" | "gm",
  travelMode: "ground",
  speedUnitsPerMinute: integer, 1–10,000 inclusive
}

game.core.world.conveyance-route
{
  status: "active" | "archived",
  summary: trimmed text, 1–1,000 Unicode scalar values,
  visibility: "public" | "party" | "gm",
  travelMode: "ground",
  distanceUnits: integer, 1–1,000,000 inclusive
}
~~~

Both records are closed. Invalid/extra JSON, invalid text/lifecycle/visibility/mode,
non-integer/out-of-range speed or distance, and archived state reject. A conveyance stores no
owner, driver, horse, passenger, cargo, current location, fuel, damage, item/character ID, or
clock. A route stores no vehicle type, speed, duration, passenger/cargo, terrain, path, or clock.

Driver and conveyance are directly contained at the same active origin in `presence`. The mechanic
uses exact integer arithmetic for `ceil(distance / speed)`, rejecting derived zero, out-of-range
duration, or clock overflow. Its exact roles are `driver`, `conveyance`, `origin`, `destination`,
`conveyanceRoute`, and `world`; input is exactly `{}`.

Success returns exactly: move conveyance to destination/`presence`; move driver to
destination/`presence`; replace root clock with derived minutes and revision +1. Any invalid effect
rolls back both moves, clock, events, and audit. Existing structural events are the only evidence.

## Dependency order and slices

~~~text
World Feature 12: generic ground conveyance journey
├─ W1/W2 containment, adjacency, traveller eligibility                [verified]
├─ W5 root clock                                                       [verified]
├─ W8 directed on-foot route/action pattern                            [verified]
├─ confirmed vocabulary and fixture distance/speed                     [implemented]
│  └─ Slice 1: conveyance/route state, links, fixture, convention tests [verified]
└─ verified ground-conveyance foundation                               [parent: Slice 1]
   └─ Slice 2: atomic driver/conveyance/clock action                  [implementation verified]

Air/water/space, cargo/passengers, multi-mode routes, vehicle simulation [excluded]
~~~

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Ground-conveyance foundation | W5/W8 and the confirmed IDs/distance/speed. | **Verified:** fresh import proves one active conveyance and ground route; invalid state leaves established movement unchanged. See the [Slice 1 receipt](WORLD-FEATURE-12-SLICE-1-RECEIPT.md). |
| 2 | Ground-conveyance journey | Slice 1 verified. | **Implementation verified:** one legal action moves conveyance + driver + clock atomically, deriving time from distance/speed; invalid/replay calls change none. See the [implementation receipt](WORLD-FEATURE-12-IMPLEMENTATION-RECEIPT.md). |

## Slice 1 — ground-conveyance foundation

- Add the two closed components and schemas.
- Revise travel/time for derived ground-conveyance timing while preserving Feature 2/8 behavior.
- Add the horse cart at gate/`presence` and a distinct 300-unit gate → market ground route.
- Add `CatalogWorldFeature12Tests` for fresh import/readback, closed data, link/placement/topology
  conventions, route separation, and isolation.

Acceptance: invalid speed/distance/mode/state or wrong containment/slot/endpoints rejects; the
on-foot route and availability stay byte-identical; traveller, clock, conditions, factions,
knowledge, items, characters, and maps do not change; focused tests and disposable catalog
validation pass.

## Slice 2 — deterministic ground-conveyance journey

- `300 / 15` derives exactly 20 minutes; non-divisible values use ceiling division.
- Co-located driver/conveyance at gate use the matching route through exactly three effects:
  conveyance move, driver move, root-clock replacement.
- Wrong/co-located state, on-foot/non-ground route, wrong scope/endpoints, corrupt/archived state,
  bad/overflow clock, and old-origin replay reject without change.
- Any invalid effect rolls back all effects, structural events, and audit.
- No passenger, cargo, horse, inventory, ownership, mount, combat, condition, map,
  air/water/space, or authorization state changes.

Run focused tests, disposable catalog validation, the full suite, and `git diff --check` at Feature
12 acceptance. Run a protocol walk only if an MCP/dependency registration changes.

## Completion boundary

Stop after one reviewed ground conveyance and driver can make an atomic distance/speed journey.
Do not model engines/horses, passengers/cargo, multi-mode routes, or inventory/character ownership.

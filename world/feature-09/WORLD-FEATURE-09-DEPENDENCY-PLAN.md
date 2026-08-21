# World Feature 9 dependency plan — authored map anchors and trusted-GM layout

Status: **Feature 9 verified**  
Last updated: 2026-08-20

## Target capability

The first fixture region receives a small authored display layout: each of its three direct
locations carries one normalized two-dimensional anchor. A trusted-GM map recipe combines those
anchors with authoritative containment, canonical adjacency, and verified Feature 8 route records
to produce a bounded map-layout input for a future read-only website map.

Anchors are presentation metadata. They do not create a location, parent, adjacency edge, route,
distance, travel time, terrain, path geometry, line of sight, or player discovery. If an anchor is
missing, malformed, or inconsistent with the region scope, the map recipe fails; it never repairs
or reinterprets topology.

### Included

- One `game.core.world.map.anchor` component attached to direct location entities.
- One `procedure.game.core.world.spatial` contract for anchor authoring, scope, correction, and
  display-only semantics.
- Three normalized anchors for the existing first region's direct locations.
- One bounded trusted-GM map-layout recipe built from Feature 7's generic graph query and the
  verified Feature 8 route vocabulary.
- Fresh-import/closed-data/scope/topology/read-recipe coverage and a concise consumer handoff.

### Excluded

- A browser page, SVG/canvas renderer, map tile/image asset, HTTP endpoint, SSE subscription,
  cache, coordinate editor, clickable travel, or any browser write. These are owned by the website
  plan after its read-only host/API slices.
- Player map access, fog-of-war, audience filtering, authentication, per-character discovery,
  hidden route display, or visibility enforcement.
- Geographic coordinates, real-world projections, distances, bearings, path polylines, terrain,
  elevation, grid combat, line of sight, collision, tactical movement, route finding, or navigation
  algorithms.
- New containment/adjacency/route/time data, new event types, subscriptions, mechanics,
  migrations, or a world-specific C# read branch.

## Source and contract basis

| Authority | Evidence | Decision supplied |
| --- | --- | --- |
| Repository workflow | `AGENTS.md`; `procedure.system.create-feature`; `procedure.system.verify` | Permanent-ID confirmation, repository validation, focused/full evidence, and no persistent import during ordinary development. |
| Topology owner | `procedure.game.core.world.location` and Feature 1 receipt | Locations, containment, and canonical `connected-to` remain authoritative; location component data itself remains coordinate-free. |
| Bounded reads | [Feature 7 plan](../feature-07/WORLD-FEATURE-07-DEPENDENCY-PLAN.md) | The generic graph query and `procedure.game.core.world.read` provide bounded, generic data access without world identifiers in C#. |
| Routes | [Feature 8 plan](../feature-08/WORLD-FEATURE-08-DEPENDENCY-PLAN.md) | Directional named routes and their in-world/from/to links are authoritative travel records, not map line geometry. |
| Visibility boundary | `GAME_SYSTEM_MASTER_PLAN.md`, Visibility; `procedure.game.core.world.knowledge` | Visibility is descriptive until an authenticated audience policy exists. The Feature 9 recipe is trusted-GM only. |
| Website ownership | `WEBSITE_AND_API_PLAN.md`, Slices 2, 3, and 7 | A later server-rendered read-only map consumes stable data; presentation does not own world state or browser writes. |

No existing component or procedure owns map-anchor display coordinates. The location contract
explicitly excludes coordinates from `game.core.world.location`, so this feature adds a separate
optional component rather than widening location state or inventing a spatial database table.

## Ownership and confirmation boundary

The user confirmed these permanent IDs, normalized-plane meaning, and first fixture positions on
2026-08-20:

| Artifact | Proposed meaning |
| --- | --- |
| `game.core.world.map.anchor` | A closed, optional, display-only coordinate pair attached directly to a location entity. It positions that location within the coordinate plane of its direct containing region. |
| `procedure.game.core.world.spatial` | Governs anchor placement/correction, region scope, map-recipe construction, and the rule that presentation never overrides topology/travel truth. |

No new fixture entity ID is created. The first anchors attach to the existing `gate`, `market`, and
`observatory` location entities at `(150, 650)`, `(500, 500)`, and `(850, 250)` respectively.
They are presentation choices, not values a mechanic calculates.

## Closed anchor contract

~~~text
game.core.world.map.anchor
{
  x: integer, 0–1000 inclusive,
  y: integer, 0–1000 inclusive
}
~~~

The plane is normalized: `(0, 0)` is the top-left and `(1000, 1000)` is the bottom-right of a
consumer's available map region. A renderer may scale this plane proportionally, but must not
derive distance, time, heading, terrain, or spatial rules from it.

An anchor is legal only when all of the following are true:

1. Its entity carries a valid active `game.core.world.location` component.
2. It is directly contained by one active `region` location through containment slot `location`.
3. The feature's selected region contains exactly one anchor for each direct active location it
   presents; anchor pairs are unique within that region.
4. The anchor record is a complete closed object. Missing, `null`, arrays/scalars, extra keys,
   non-integers, negative/out-of-range coordinates, duplicate pairs, inactive locations, nested
   interiors, and anchors on roots, actors, routes, factions, or knowledge entities are invalid.

The component contains no region ID, map ID, labels, visibility, z-index, scale, image reference,
route ID, distance, or position history. Entity identity/name and containment supply scope and
display identity. A later multi-map or interior-map feature must introduce its own explicit
ownership decision rather than overloading these two numbers.

## Trusted-GM map-layout recipe

Feature 9 revises `procedure.game.core.world.read` only to publish this consumer recipe; the generic
`query(kind: "graph")` implementation remains unchanged and must not recognise map-specific IDs.

For one selected active region, the recipe uses two capped generic graph reads and an in-memory
intersection by permanent IDs:

| Read | Root/traversal | Selected components | Relationship kinds | Cap | Use |
| --- | --- | --- | --- | --- | --- |
| Region topology | Selected region; containment depth 1; relationship depth 1 | `game.core.world.location`, `game.core.world.map.anchor` | `game.core.world.location.connected-to` | 50 nodes / 100 edges | Returns direct anchored locations and their canonical adjacency. |
| World routes | The selected region's world root; containment depth 0; relationship depth 2 | `game.core.world.route` | `game.core.world.route.in-world`, `game.core.world.route.from`, `game.core.world.route.to` | 100 nodes / 150 edges | Returns route records and endpoint references in the same world. |

The recipe returns a normalized consumer model, ordered by permanent IDs:

~~~text
{
  region: { id, name },
  locations: [{ id, name, kind, summary, x, y }],
  adjacency: [{ fromLocationId, toLocationId }],
  routes: [{
    id, name, fromLocationId, toLocationId,
    mode, durationMinutes, visibility
  }]
}
~~~

Only active direct region locations with exactly one valid anchor appear. An adjacency appears only
when both endpoint locations appear. A route appears only when it is active, has valid
in-world/from/to links, and both endpoints appear. The layout includes neither a straight-line
travel claim nor a route shape: a renderer may draw a visual connector, but that connector is not
a path or rule.

Any invalid selected-region/location/anchor/adjacency/route relationship is a stable recipe
failure. The implementation must not discard a bad edge, coerce a coordinate, substitute a
missing anchor, infer a route from adjacency, or include an anchor/component outside the selected
region. The recipe is trusted-GM data; descriptive visibility is included for a later authorized
consumer to enforce, never filtered here.

## Dependency order and slices

~~~text
World Feature 9: display anchors and trusted-GM map layout
├─ W1 root/region/location containment and adjacency                 [verified]
├─ W7 generic bounded graph query and world read procedure           [verified]
├─ W8 route entity/links and on-foot journey                         [verified]
├─ website read-only host/API/map consumer                           [separate future owner]
├─ confirmed anchor vocabulary and first-region layout               [semantic boundary]
│  └─ Slice 1: anchor component, procedure, fixture, convention tests [verified]
└─ verified anchors plus W7/W8 read records                          [parent: Slice 1]
   └─ Slice 2: trusted-GM map-layout recipe and consumer handoff     [implemented]

Player projection, rendered map, map interaction, geometry, and spatial rules [excluded]
~~~

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Anchor foundation | W7/W8 are verified; component/procedure IDs and three reviewed fixture positions are confirmed. | **Verified** — [Slice 1 receipt](WORLD-FEATURE-09-SLICE-1-RECEIPT.md): fresh import has one valid anchor on each first-region direct location; invalid anchor conventions are rejected. |
| 2 | Map-layout recipe | Slice 1 and the generic W7 reader are verified; W8 route fixture/readback is available. | **Verified** — [Feature 9 receipt](WORLD-FEATURE-09-IMPLEMENTATION-RECEIPT.md): the two public graph reads deterministically return only the selected region's anchored locations, adjacency, and in-region route references; no authoritative world write occurs. |

## Slice 1 — anchor foundation

| Artifact | Change |
| --- | --- |
| Component definition/schema | Add `game.core.world.map.anchor` with exactly `x` and `y`. |
| Governing procedure | Add `procedure.game.core.world.spatial`. Revise location/read procedure wording only where necessary to state that coordinates are separate optional presentation components, never location fields. |
| Fixture | Add confirmed anchors to the three existing direct Feature 1 locations. Do not alter their location components, containment, canonical adjacency, routes, clock, knowledge, factions, or traveller state. |
| Focused tests | Add `CatalogWorldFeature9Tests` or the nearest world catalog owner for fresh import/readback and disposable invalid-anchor variants. |

### Slice 1 acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Fresh import | All and only the selected region's direct fixture locations carry one valid anchor; component data contains only the confirmed integer pair. |
| Closed data | Missing/extra/non-object/`null`/fractional/out-of-range coordinate data rejects. |
| Scope and placement | Anchors on roots, actors, factions, routes, knowledge, nested locations, inactive locations, or locations outside the selected active region reject. |
| Unique layout | Duplicate pair or missing direct-location anchor rejects the first-region fixture convention. |
| Topology isolation | Changing/deleting an anchor has no effect on containment, adjacency, route validity, clock state, or traveller location. |
| Repository | Focused tests and `roleplay validate catalog` pass without persistent import. |

## Slice 2 — bounded map-layout recipe

No browser code is included in this slice. Add the recipe definition to the world-read procedure and
tests that execute the two graph reads through their public path, construct the documented layout
model, and assert its stable ordering and no-write behavior.

### Slice 2 acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Happy layout | The selected region returns exactly its anchored direct locations, canonical adjacency, and active routes whose two endpoints are shown. |
| Region isolation | Locations, anchors, adjacency, and route endpoints outside the selected region do not appear. |
| Route integrity | Missing/reversed/cross-world/inactive/malformed route data fails the recipe; adjacency alone never creates a route record. |
| Anchor integrity | Missing/duplicate/malformed/out-of-region anchors fail the recipe rather than producing shifted or partial layout. |
| Ordering and bounds | Locations, adjacency, and routes use canonical permanent-ID ordering; cap exhaustion is explicit and never silently changes visual input. |
| Truth boundary | Altering an anchor changes only returned coordinates. It cannot move a traveller, change topology, create a route, advance time, or emit an event. |
| Audience boundary | The trusted-GM result returns visibility labels but makes no player-safe claim. A player projection is unavailable until authorization is implemented. |
| Consumer handoff | The documented layout schema is sufficient for Website Slice 7 to render a read-only map without database access or custom world rules. |
| Repository acceptance | Focused tests, `roleplay validate catalog`, full suite, and `git diff --check` pass. Run the website/browser suite only in the later website-owned rendering slice. |

## Completion boundary

Feature 9 is complete when reviewed anchors and the trusted-GM map-layout recipe accurately reflect
the selected region's authoritative topology and route records, reject invalid display data
deterministically, and supply a stable read-only input to the website plan. Stop before rendering,
audience filtering, map interaction, geometry, or any change that could make display coordinates
authoritative world state.

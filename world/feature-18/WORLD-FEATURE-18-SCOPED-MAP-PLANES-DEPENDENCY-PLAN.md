# World Feature 18 dependency tree — display placement on more than one scope plane

Status: **Slice 1 verified; live World/City coordinate authoring remains planned**
Ruleset alignment: **ruleset-neutral**
Source: **not applicable**. Display placement is engine infrastructure and defines no D&D rule.

Owner/roadmap: `WORLD_AND_LORE_PLAN.md`
Consumer: `prototype/dnd2024/planning/DND2024-SCOPED-MAP-VIEWS-DEPENDENCY-TREE.md`, Slice 3

## Standing

The user's 2026-08-30 World-tab completion request explicitly asks for canonical coordinates and
hierarchy at World and City scope. That supersedes the earlier prototype-only hold for this one
generic owner. Slice 1 may relax the accepted anchor constraint without adding a second placement
component. Live Thalorien values and web projection remain separate leaves.

## Why this exists

The scoped map workspace's live slices are blocked, and this tree records what actually blocks them.
An earlier reading of that blocker was wrong and is corrected here: **Region is already an
addressable entity.** `game.core.world.location` has carried `kind: "region"` since W1, and
containment already models world root → region → place → interior. Nothing needs to be created for
Region to exist.

What is missing is narrower and more specific: **anchors exist for exactly one plane.** W9's
contract places a location on the plane of the region that directly contains it, and explicitly
forbids anchors on roots, regions, and nested interiors. So a live Region map is nearly buildable
today, while a live World map (regions placed on the world plane) and a live City map (sites and
interiors placed on a settlement plane) have no placement owner at all.

## Outcome and non-goals

**Outcome.** Authoritative, display-only placement for an active location on the plane of whichever
active container directly holds it — root, region, or settlement — so a consumer can render a marker
at every scope without inventing topology.

**Non-goals.** Distance, adjacency, routes, reachability, travel time, terrain, geometry,
pathfinding, line of sight, fog, tactical grids, movement, position history, player discovery state,
map documents, layers, assets, rendering, and generated imagery. Containment and relationships remain
the authoritative world model; coordinates stay display-only, exactly as W9 established.

## Existing owners and evidence

| Concern | Owner | State | Evidence |
| --- | --- | --- | --- |
| Region as an addressable entity | `game.core.world.location`, `kind: "region"` | `verified` | `game.core.world.location.schema.json` kind enum; W1 receipt |
| World → region → place → interior hierarchy | Containment slots `region` and `location` | `verified` | `procedure.game.core.world.location`, instruction 3 |
| Adjacency | `game.core.world.location.connected-to` | `verified` | Same procedure, instruction 4 |
| Display placement on a region plane | `game.core.world.map.anchor` + `procedure.game.core.world.spatial` | `verified` | W9 Slice 1 receipt; `CatalogWorldFeature9Tests` |
| Trusted-GM map-layout projection | Map-layout recipe in `procedure.game.core.world.read` | `verified` | Read procedure, recipe 6 |
| Placement of regions on the world plane | none | `missing` | Spatial constraints: "Roots, regions, nested interiors … never carry an anchor" |
| Placement of sites/interiors on a settlement plane | none | `missing` | Spatial instruction 1 requires direct containment by a region |
| Audience policy over topology | none | `missing`, and roadmap-deferred | Location procedure: "Visibility is descriptive until an authorised audience feature exists"; roadmap Deferred list |
| Perspective-safe answers | `query(kind: "knowledge-answer")` | `verified` for knowledge records only | `procedure.game.core.world.knowledge` |
| Map document / layer / feature / scope-link state | none | `missing` | 66 catalog components; only `game.core.world.map.anchor` is map-related |

## Dependency tree

~~~text
Live scoped maps at every scope                                              [planned]
├─ A. Region-plane placement                                                 [verified]
├─ B. Placement on a declared container plane                                [verified]
│  ├─ B1. Relax the direct-region constraint to active root/region/settlement [verified]
│  ├─ B2. Uniqueness scoped per plane rather than per region                 [verified]
│  └─ B3. Layout recipe generalized from "the region" to "the plane"         [verified]
├─ C. Audience policy over topology and placement                            [missing]
│  ├─ C1. An authorised audience owner for locations                         [missing]
│  └─ C2. Player-safe layout projection                                      [blocked by C1]
└─ D. Map document, layer, feature, and scope-link state                     [missing]
   └─ permanent IDs and schemas confirmed                                    [missing]
~~~

## Conflicts and decisions

- **The plane is already implied by containment.** W9's own rule — "The direct container is the
  anchor's only region scope; never store a region or map ID in the component" — generalizes
  cleanly: the direct container *is* the plane. Under that reading, all three planes are reachable
  with the accepted closed schema unchanged, and B is a **constraint relaxation in the procedure
  plus the layout recipe**, not new state. That is the smallest useful leaf, and it is why this tree
  proposes no new placement component.
- **It is still a change of accepted meaning.** `CatalogWorldFeature9Tests` asserts that an anchor
  on a location contained by a non-region location is invalid. Relaxing B1 therefore rewrites a
  verified assertion and needs confirmation; it must not be slipped in as a bug fix.
- **Do not add a second placement component.** A parallel "scoped anchor" would create two placement
  owners for the same fact, which is exactly the duplicate-state conflict the authoring guide says to
  flag. Extend the one owner or do nothing.
- **Uniqueness must be re-scoped, not dropped.** The current rule is one unique `(x, y)` per region
  layout. On multiple planes it becomes one unique `(x, y)` per plane. Dropping uniqueness would let
  two markers collide silently; scoping it globally would forbid legitimate reuse across planes.
- **Coordinates are integers 0–1,000 per plane.** The dnd2024 prototype fixture uses percentages
  0–100 and derives world markers from locations rather than regions. That fixture is not the live
  contract; the consumer converts at the presentation edge only, and its Slice 3 must adopt the
  integer plane.
- **A live World map may not be a placed map at all.** Until B lands, regions have no coordinates,
  so the honest world-scope view is an ordered list of regions with scope links, not markers on an
  image. The consumer should not fake placement to fill the gap.
- **Audience policy is a separate, already-deferred owner.** C is not this feature's to solve. The
  roadmap defers player-safe views until authenticated audience policy exists, and the read recipes
  are trusted-GM by contract. No map slice may quietly become that owner.

## Ordered leaves

| Order | Leaf | Depends on | Exit gate |
| --- | --- | --- | --- |
| B1 | Placement on any active plane container | Confirmation of the changed contract meaning | An anchor is valid on an active location whose direct container is an active root, region, or settlement; invalid on a root, on an inactive container, and where the container kind is not a plane; the closed `x`/`y` schema is unchanged; W9's topology-isolation coverage still passes. |
| B2 | Per-plane uniqueness | B1 | Two locations on the same plane may not share `(x, y)`; the same `(x, y)` on two different planes is valid; a duplicate is rejected rather than displaced. |
| B3 | Plane-scoped layout recipe | B1, B2 | The map-layout recipe takes any active plane container, not only a region, and still refuses to return partial display data when an anchor is missing, malformed, or non-unique on that plane. Output stays trusted-GM. |
| C1 | Authorised audience owner for locations | Roadmap decision to undefer | Out of scope for this tree; named so no map slice absorbs it by accident. |

## Completed lowest leaf

**B1–B3 are verified as one contract-only slice.** The existing `game.core.world.map.anchor` remains the sole
placement owner and its closed `x`/`y` schema is unchanged. A valid plane is an active
`game.core.world.root` or an active `game.core.world.location` whose kind is `region` or
`settlement`. The anchored child is one direct active location using its topology-required slot.
Coordinates must be unique among anchored direct children of that plane. The trusted-GM layout
recipe accepts any such plane and fails closed on missing, malformed, duplicate, or wrong-scope
anchors. No live record or UI changes occurred. See
`WORLD-FEATURE-18-SLICE-1-RECEIPT.md`.

## Confirmed decisions

1. This is World Feature 18 because it delivers World- and City-plane placement that W9 excluded.
2. `procedure.game.core.world.spatial` may be relaxed from direct Region scope to the direct active
   plane container without adding another placement component.
3. Active World roots, Regions, and settlements are planes. Sites and interiors remain excluded so
   scene-scale geometry is not silently introduced.
4. Uniqueness is per plane. Equal coordinates on different planes are valid.

## Planning receipt

- Runtime artifacts created by planning: none.
- The user's 2026-08-30 request confirms the changed generic contract meaning and activates Slice 1.
- Slice 1 evidence is recorded in `WORLD-FEATURE-18-SLICE-1-RECEIPT.md`.

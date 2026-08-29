# World Feature 18 dependency tree — display placement on more than one scope plane

Status: **on hold — recorded finding, not proposed work.** The owner confirmed on 2026-08-28 that
implementation happens in the prototype and not by changing the canonical model, so nothing in this
tree may be implemented to unblock the dnd2024 scoped map workspace. It stands as the written record
of a real gap in the canonical model, for whenever that model is worked on for its own reasons.
Ruleset alignment: **ruleset-neutral**
Source: **not applicable**. Display placement is engine infrastructure and defines no D&D rule.

Owner/roadmap: `WORLD_AND_LORE_PLAN.md`
Consumer: `prototype/dnd2024/planning/DND2024-SCOPED-MAP-VIEWS-DEPENDENCY-TREE.md`, Slice 3

## Standing

This tree changes canonical contracts, which prototype work may not do. Its leaves are therefore not
available to the scoped map slices: Slice 3 stays blocked, and the prototype accounts for the gap in
its own model instead. Read this for what the canonical model does and does not provide; do not treat
its ordered leaves as a queue.

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
├─ B. Placement on a declared container plane                                [awaiting confirmation]
│  ├─ B1. Relax the direct-region constraint to any active plane container   [awaiting confirmation]
│  ├─ B2. Uniqueness scoped per plane rather than per region                 [awaiting confirmation]
│  └─ B3. Layout recipe generalized from "the region" to "the plane"         [planned]
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

## Lowest ready leaf

**None, and none is being sought.** B1 would be the lowest useful leaf, but it changes the meaning of
an accepted contract and a verified test asserts the narrower rule — and it may not be taken up as
part of prototype work regardless. The gates below record what an owner of the canonical model would
have to decide; they are not this consumer's to answer.

## Questions for a future canonical owner

1. **Feature number and framing.** Is this World Feature 18, or a second slice of W9? The roadmap's
   next-feature rule asks for a player-visible capability not already owned, and placement is owned
   by W9 — but the new planes do deliver capability W9 explicitly excluded.
2. **Changed contract meaning.** May `procedure.game.core.world.spatial` be relaxed from "directly
   contained by an active region" to "directly contained by an active plane container", with W9's
   test updated rather than duplicated?
3. **Plane container kinds.** Which container kinds are planes? This tree proposes root, region, and
   settlement, leaving `site` and `interior` excluded so that scene-scale geometry stays with its own
   future owner.

## Planning receipt

- Runtime artifacts created: none.
- Correction recorded: Region is an existing addressable entity; the real gap is single-plane
  placement. The scoped-map consumer tree and its project-memory note are updated to match.

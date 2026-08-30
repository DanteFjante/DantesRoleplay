# DND2024 scoped map views dependency tree — Slices 2-7

Status: **planning only; Slices 1, 2, 5, 7 and 8-13 accepted; Slices 3 and 6 blocked**
Ruleset alignment: **dnd2024-compatible**
Source: **not applicable**. Scoped maps present authored geography and define no D&D rule. They
compute no distance, travel time, range, visibility, or movement.

Owner/roadmap: `web/WEB-INTERFACE-ROADMAP.md`, Feature 5
Parent tree/leaf: `DND2024-WEB-INFORMATION-HUB-DEPENDENCY-TREE.md`, Leaf 4A
Product plan: `DND2024-SCOPED-MAP-VIEWS-FUTURE-PLAN.md`
Completed: Slice 1, `DND2024-SCOPED-MAP-VIEWS-SLICE-1-RECEIPT.md`; Slice 2, `DND2024-SCOPED-MAP-VIEWS-SLICE-2-RECEIPT.md`; Slice 5, `DND2024-SCOPED-MAP-VIEWS-SLICE-5-RECEIPT.md`; Slice 7, `DND2024-SCOPED-MAP-VIEWS-SLICE-7-RECEIPT.md`; presentation repairs 8-10, `DND2024-LIVE-MAP-*-RECEIPT.md`

## Outcome and non-goals

**Outcome.** One World-owned map workspace that reads authoritative World state, projects every
scope safely to the effective audience, navigates World, Region, City, and Location scopes, may show
reviewed illustrative Location imagery, and accepts campaign knowledge overlays without ever
changing World geography.

**Non-goals, at every slice.** Travel time, distance, adjacency, routes, reachability, encounter
placement, fog simulation, tactical grids, movement, position writes, visit inference, and any
generated asset that becomes game truth. A map never writes world or campaign state.

## Existing owners and evidence

| Concern | Owner | State | Evidence |
| --- | --- | --- | --- |
| Scope contract, breadcrumbs, per-scope coordinate spaces | Prototype map fixture hierarchy and `ScopedMapWorkspace` | `verified` (fixture only) | Slice 1 receipt |
| Scene-scale reference views at the location scope | Prototype authored location maps | `verified` (fixture only) | Slice 5 receipt |
| Campaign annotation of World maps | Prototype `campaign.mapOverlays` and its projection | `verified` (fixture only) | Slice 7 receipt |
| Layer audience policy, per-audience base variants, fail-closed absence | Prototype hub projector | `verified` (fixture only) | Slice 2 receipt |
| Effective audience selection | Accepted server-issued audience/read envelope | `verified` | Information-hub Slices 2-7 receipts |
| World display placement | `game.core.world.map.anchor`, `procedure.game.core.world.spatial` | `verified` as display coordinates only | Parent tree, geographic placement row |
| Known/current location projection | Parent tree Leaf 3 | `planned` | Parent tree; live-data prerequisites unmet |
| Live world map document authority | none | `missing` | Parent tree Leaf 4 is `planned`; of 66 catalog components only `game.core.world.map.anchor` is map-related |
| Region as an addressable entity | `game.core.world.location`, `kind: "region"` | `verified` | Location schema kind enum; W1 receipt. **Correction:** an earlier revision of this tree recorded Region as missing, reading the prototype fixture rather than the catalog. |
| Placement of a region on the world plane | none | `missing` | `procedure.game.core.world.spatial`: roots and regions never carry an anchor |
| Placement of a site or interior on a settlement plane | none | `missing` | Same procedure: an anchor requires direct containment by a region |
| City/district/place hierarchy | none | `missing` | Parent tree Leaf 4A prerequisite; Slice 1 city features are presentation-local keys |
| Scene-scale location geometry | none | `missing` | Parent tree Leaf 6, visual reference foundation |
| Media storage, audience variants, provenance, review | none | `missing` | Parent tree, images and maps row: filenames cannot become authority |
| Campaign/actor knowledge projection | Campaign projection exists; a knowledge-reveal owner does not | `conflicting` | Information-hub Slice 7 receipt records fixture-backed campaign only |

## Dependency tree

~~~text
Scoped map views (World -> Region -> City -> Location)                       [planned]
├─ 1. Scope contract and fixture hierarchy                                   [verified]
├─ 2. Audience-safe map projection                                           [verified]
├─ 3. Live World and Region maps                                             [blocked]
│  ├─ parent Leaf 3 known-location projection                                [planned]
│  ├─ parent Leaf 4 display-only world map                                   [planned]
│  ├─ placement on a world plane and a settlement plane                      [missing]
│  └─ permanent map document / feature / scope-link IDs                      [missing]
├─ 4. City maps                                                              [n/a]  
│  └─ confirmed city / district / place hierarchy and feature links          [out of scope]
├─ 5. Authored Location reference views                                      [verified]
│  └─ confirmed scene-scale document and feature contract                    [prototype-authored]
├─ 6. Optional generated Location assets                                     [blocked]
│  ├─ approved media storage and audience variants                           [missing]
│  ├─ provenance record and DM approval gate                                 [missing]
│  └─ rollback to the previously approved asset                              [missing]
└─ 7. Campaign knowledge overlays                                            [verified]
   ├─ campaign/actor knowledge projection with revision semantics            [prototype-authored]
   └─ overlay that references but never mutates a World map                  [verified]
├─ 8. Live containment hierarchy repair                                      [verified]
├─ 9. Live location-kind layer controls                                      [verified]
├─ 10. Live authorized campaign-note overlays                                [verified]
├─ 11. Cross-scope atlas search                                               [verified]
├─ 12. Projected faction influence highlighting                              [verified]
└─ 13. Accessible illustrated/list map modes                                  [verified]
~~~

## Conflicts and decisions

- **Two candidate geography sources.** World-scope placement must keep deriving from the accepted
  anchor owner. A live map document may own scope structure, layers, and features, but it may not
  re-declare world placement, or the anchors and the map disagree silently. Decided in Slice 1 and
  carried forward.
- **Region is an entity; placement is what is single-planed.** `game.core.world.location` already
  carries `kind: "region"`, and containment already models root → region → place → interior. What
  W9's anchor contract supports is exactly one plane: a location directly inside a region. Regions
  themselves and sub-settlement places carry no coordinates, so Slice 3's world scope has no live
  placement and Slice 4's city scope has none either. That gap is written up in
  `world/feature-18/WORLD-FEATURE-18-SCOPED-MAP-PLANES-DEPENDENCY-PLAN.md`, which is **on hold**:
  this workspace implements in its prototype and does not change canonical contracts to unblock
  itself. Later slices account for the gap in the prototype's own model instead.
- **Anchors are integers 0–1,000 per plane.** The prototype's percentage anchors are a fixture
  convention, not the live contract. Slice 3 adopts the integer plane and converts only at the
  presentation edge.
- **Until placement lands, the world scope is a list, not a picture.** With no coordinates for
  regions, the honest live world view is ordered region scope links rather than markers. Slice 3
  must not fabricate placement to fill the gap.
- **A safe marker set does not make a safe picture.** Filtering hidden markers out of a base image
  that still shows their labels leaks the secret. Slice 2 therefore requires a Player-safe base
  variant or independently projected label layers, not marker filtering alone.
- **Generated imagery is never authority.** Slice 6 output is illustrative until authored geometry
  is separately confirmed. It may not introduce exits, doors, hazards, creatures, treasure, or
  distances. Any such fact requires a separate authoritative World change.
- **Overlays are campaign-scoped, maps are World-scoped.** Slice 7 must not give a campaign a way to
  move, hide, or re-place a World feature; it may only add campaign-visible annotation.

## Ordered leaves

| Order | Leaf | Depends on | Exit gate |
| --- | --- | --- | --- |
| 2 | Audience-safe map projection | 1 | Per-layer audience policy is enforced server-side; every map document carries a Player-safe base variant or projects labels as a separate layer; Player bytes exclude hidden layers, features, child scopes, asset URLs, alt text, and counts; DM Player-preview equals a real Player projection for map bytes; a denied or missing variant fails closed and never falls back to a DM asset. |
| 3 | Live World and Region maps | 2, parent Leaves 3 and 4, a prototype account of world/settlement-plane placement, permanent IDs | World and Region documents read from authoritative state through `IWorldStore` with no copied campaign geography; permanent IDs and schemas are confirmed; stale or denied reads fail closed; equal safe inputs place markers deterministically. |
| 4 | City maps — **no separate prototype deliverable**; authored in Slice 1, canonical hierarchy out of scope | — | City navigation resolves explicit districts and places through declared links; no proximity, naming, or filename inference; a city without an authored hierarchy remains navigable as information. |
| 5 | Authored Location reference views | 4, confirmed scene-scale contract | Location references show approved areas, entrances, and points with no tactical claim; geometry outside the declared space is rejected; a location with no authored view keeps its existing text detail. |
| 6 | Optional generated Location assets | 5, media storage, provenance, review | Generation is explicit and reviewable; the brief is built only from already authorized structured facts; provenance records source revision, audience variant, brief revision, generator/version, seed, timestamp, and content review; DM approval precedes linking; the prior approved asset is preserved for rollback; failure changes no World state. |
| 7 | Campaign knowledge overlays | 2 and 5, campaign/actor knowledge projection | Campaign reveals and notes overlay a World map without changing World geography; an overlay referencing a hidden or absent feature is dropped without naming it; overlay revision mismatches fail closed. |

## Lowest remaining leaf

**Canonical Slice 3 remains the lowest unresolved leaf.** Presentation Slices 11-13 are verified by
their receipts: cross-scope search, exact projected faction highlighting, and an accessible
illustrated/list switch are complete. They do not unblock or substitute for canonical Slices 3 and
6, which remain blocked by the prerequisites below.

**Slice 3 — live World and Region maps, `blocked`.** It cannot become `ready` until parent tree
Leaves 3 and 4 land, the prototype has its own account of world-plane and settlement-plane placement,
and permanent map document, layer, feature, and scope-link IDs and schemas are confirmed. Slices 1
and 2 deliberately left all of these untouched.

Note the boundary: the canonical single-plane limit is a fact to design around, not a contract this
workspace may relax. Slice 3 reads canonical placement where it exists and keeps its own model for
the scopes where it does not.

The *region* scope is the exception worth knowing: W9 already gives every location directly inside a
region a unique display anchor, and the read procedure already has a trusted-GM map-layout recipe for
exactly that plane. A live Region map is therefore the closest live capability in this whole tree —
held back by audience policy rather than by placement.

Slice 7 (campaign knowledge overlays) now depends only on Slice 5 and a campaign/actor knowledge
projection, since its Slice 2 prerequisite is met.

## Confirmation gates

1. Permanent map document, layer, feature, and scope-link IDs and schemas (blocks Slice 3).
2. How the prototype accounts for world-plane and settlement-plane placement given that canonical
   anchors serve only the region plane (blocks Slices 3 and 4). Changing the canonical contract is
   not an option here; see the on-hold W18 write-up for what the canonical model lacks.
3. City, district, and place hierarchy ownership (blocks Slice 4).
4. ~~Scene-scale location document and feature contract~~ — resolved in the prototype by Slice 5;
   no canonical contract is sought.
5. Media storage, audience variants, provenance, licensing, and the DM approval gate (blocks
   Slice 6).
6. ~~Campaign/actor knowledge projection and revision semantics~~ — resolved in the prototype by
   Slice 7's authored overlays; no canonical contract is sought.

## Planning receipt

- Runtime artifacts created: none.
- Slice 1 is complete and linked above; its detail is collapsed to one verified line in the tree.

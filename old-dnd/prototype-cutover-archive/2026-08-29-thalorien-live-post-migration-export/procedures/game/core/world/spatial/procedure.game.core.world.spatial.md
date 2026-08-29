---
id: procedure.game.core.world.spatial
category: game.core.world.spatial
name: Govern display-only world map anchors
governs: commit(kind: "component") declaring game.core.world.map.anchor; commit(kind: "effects") recording or correcting reviewed map-anchor data on direct region locations
status: active
createdBy: "seed"
changeNote: "Seeded from bootstrap file."
---

## Description
Defines display placement for trusted-GM map consumers. An anchor is optional presentation metadata
on one direct location within one region; containment, adjacency, routes, and time remain the
authoritative world model.

## Instructions
1. Use `game.core.world.map.anchor` only on an active `game.core.world.location` directly
   contained by an active `region` location in containment slot `location`. The direct container is
   the anchor's only region scope; never store a region or map ID in the component.
2. Its complete closed data is exactly integer `x` and `y`, each 0–1,000. `(0, 0)` is the
   top-left and `(1000, 1000)` the bottom-right of a consumer's available region plane. A renderer
   may scale the plane proportionally, but must not derive distance, heading, time, terrain, or
   travel rules from it.
3. For one reviewed region layout, anchor every direct active location presented by that layout and
   keep each `(x, y)` pair unique. Correct an anchor as a complete replacement after reading its
   location and direct containing region.
4. The first fixture anchors are gate `(150, 650)`, market `(500, 500)`, and observatory
   `(850, 250)`. They are display choices only; they neither create nor alter an adjacency or
   route.

## Constraints
- Missing, null, non-object, fractional, negative, out-of-range, or extra coordinate data is
  invalid. Roots, regions, nested interiors, actors, factions, routes, knowledge records, and
  inactive locations never carry an anchor.
- An anchor contains no label, region/map ID, visibility, z-index, scale, image, route, distance,
  terrain, path, position history, or player-discovery state. Entity identity/name and containment
  supply display identity and scope.
- This contract creates no map renderer, browser/UI, query kind, geometry rule, route/path rule,
  mechanic, event, subscription, notification, player filtering, or authorization. A later map
  recipe may consume anchors but cannot make them authoritative.


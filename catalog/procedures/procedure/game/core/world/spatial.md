---
id: procedure.game.core.world.spatial
category: game.core.world.spatial
name: Govern display-only world map metadata
governs: commit(kind: "component") declaring game.core.world.map.anchor or game.core.world.map.visual; commit(kind: "effects") recording or correcting reviewed map anchors and visual variants on World nodes
status: active
createdBy: "seed"
changeNote: "Re-seeded: the bootstrap file changed."
---

## Description
Defines display placement and reviewed media selection for map consumers. An anchor is optional
presentation metadata on one direct location within one active map plane. A map visual is optional
presentation metadata owned by the world root or location whose plane it depicts. Containment,
adjacency, routes, time, and audience knowledge remain the authoritative world model.

## Instructions
1. Use `game.core.world.map.anchor` only on an active `game.core.world.location` directly
   contained by an active map plane. A plane is an active `game.core.world.root` or an active
   `game.core.world.location` whose kind is `region` or `settlement`. Existing topology still owns
   the child slot: a child region uses `region`; a child settlement, site, or interior uses
   `location`. The direct container is the anchor's only map plane; never store a plane, region,
   settlement, or map ID in the component.
2. Its complete closed data is exactly integer `x` and `y`, each 0–1,000. `(0, 0)` is the
   top-left and `(1000, 1000)` the bottom-right of a consumer's available region plane. A renderer
   may scale the plane proportionally, but must not derive distance, heading, time, terrain, or
   travel rules from it.
3. For one reviewed plane layout, anchor every direct active child location presented by that
   layout and keep each `(x, y)` pair unique within that plane. The same pair may be reused on a
   different plane. Correct an anchor as a complete replacement after reading its location and
   direct containing plane.
4. The first fixture anchors are gate `(150, 650)`, market `(500, 500)`, and observatory
   `(850, 250)`. They are display choices only; they neither create nor alter an adjacency or
   route.
5. Use `game.core.world.map.visual` only on an entity carrying an active
   `game.core.world.root` or active `game.core.world.location`. The owning entity is the depicted
   map plane. Its complete closed data is exactly status plus a nonempty closed `variants` object.
   Each optional `player` or `dm` variant records a finalized blob hash, MIME type, dimensions,
   nonempty alt text, optional caption, order, and reviewed provenance.
6. Select only the exact requested audience variant. A missing Player variant exposes no DM blob
   metadata or alt text, and a missing DM variant does not fall back to Player. Delivery re-resolves
   the map through its owning entity before opening the verified blob.
7. Correct a visual as a complete replacement after reading its owner. Visual status controls only
   whether that illustration is current; removing or archiving it must not remove the World node or
   any information view for that node.

## Constraints
- Missing, null, non-object, fractional, negative, out-of-range, or extra coordinate data is
  invalid. Roots, actors, factions, routes, knowledge records, inactive locations, and locations
  outside the direct active-plane topology/slot contract never carry an anchor. Sites and interiors
  are not planes; their direct children cannot carry anchors for that container.
- An anchor contains no label, plane/region/settlement/map ID, visibility, z-index, scale, image, route, distance,
  terrain, path, position history, or player-discovery state. Entity identity/name and containment
  supply display identity and scope.
- A map visual contains no URL, path, asset key, owner ID, child ID, coordinates, crop, scale, visibility,
  discovery, geometry, route, terrain, distance, campaign ID, or game rule. Blob hashes are exact
  lowercase SHA-256 identities and never select an audience implicitly.
- This contract creates no map renderer, browser/UI, query kind, geometry rule, route/path rule,
  mechanic, event, subscription, notification, player filtering, or authorization. A later map
  recipe may consume anchors but cannot make them authoritative.

---
id: procedure.game.core.world.location
category: game.core.world.topology
name: Record shared-game world topology
governs: commit(kind: "component") declaring game.core.world.root or game.core.world.location; commit(kind: "effects") creating or correcting world roots, locations, containment, and game.core.world.location.connected-to relationships
status: active
createdBy: "seed"
changeNote: "Seeded from bootstrap file."
---

## Description
Defines the persistent shared-game topology used by later campaign, movement, and lore features:
one world-root component, reusable location components, containment hierarchy, and canonical travel
adjacency. It does not move actors, decide travel, reveal lore, or create a campaign.

## Instructions
1. Use `game.core.world.root` only on a world entity. It contains exactly status, summary, and
   descriptive visibility. The entity name is the world name; do not store it again.
2. Use `game.core.world.location` on regions and playable places. It contains exactly kind, status,
   summary, and descriptive visibility. Do not store parent, world, coordinate, route, distance, or
   connection-list fields.
3. Model hierarchy only with containment. A world root has no container. A region may be contained
   by a root in slot `region`; a place may be contained by a region or other location in slot
   `location`. Read the intended graph before correction; containment cycles are invalid.
4. Model initial adjacency only with `game.core.world.location.connected-to`. Its data is `{}`;
   both endpoints carry game.core.world.location; it is stored once with lexically smaller entity ID
   as `from`. Readers inspect incoming and outgoing edges because the convention is undirected.
5. For development fixtures, author the component definitions before entity files and preserve the
   canonical edge order in `catalog/world/relationships.json`. For live authored setup, read the
   intended entities/definitions and submit one effects list ordered entity creation, component
   adds, containment moves, then relationship creates.
6. Replace a closed root/location record as a complete object when correcting it. Do not use merge
   to invent partial state. Visibility is descriptive until an authorised audience feature exists.

## Constraints
- Status is exactly draft, active, or archived. Visibility is exactly public, party, or gm.
- Location kind is exactly region, settlement, site, or interior.
- Summary is a trimmed, nonempty string of at most 1,000 Unicode scalar values.
- Root/location components never contain child IDs, parent IDs, world IDs, campaign IDs, adjacency
  arrays, actor positions, facts, factions, clues, time, terrain, or coordinates.
- A reverse, duplicate, self, non-location, or nonempty-data adjacency violates this topology
  contract. Generic direct relationships remain administrative; later guarded authoring may enforce
  these feature conventions for untrusted input.
- No action mechanic, event, subscription, campaign, movement, map, routing, or player-safe
  visibility projection is created by this contract.


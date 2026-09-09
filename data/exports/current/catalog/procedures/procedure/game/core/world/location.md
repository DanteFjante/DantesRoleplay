---
id: procedure.game.core.world.location
category: game.core.world.topology
name: Record shared-game world topology
governs: commit(kind: "system.component-type.register") declaring game.core.world.root, game.core.world.location, or game.core.world.location.furnishing; the focused location-shell, placement, furnishing, adjacency, knowledge-link, and media mechanics; commit(kind: "system.world-state.sync") correcting world roots, locations, containment, and game.core.world.location.connected-to relationships
status: active
createdBy: "seed"
changeNote: "Re-seeded: the bootstrap file changed."
---

## Description
Defines the persistent shared-game topology used by later campaign, movement, and lore features:
one world-root component, reusable location components, containment hierarchy, and canonical travel
adjacency. It does not move actors, decide travel, reveal lore, or create a campaign.

## Matches

## Instructions
1. Use `game.core.world.root` only on a world entity. It contains exactly status, summary, and
   descriptive visibility. The entity name is the world name; do not store it again.
2. Use `game.core.world.location` on regions and playable places. It contains exactly kind, status,
   summary, and descriptive visibility. Do not store parent, world, coordinate, route, distance, or
   connection-list fields.
3. Model hierarchy only with containment. A world root has no container. A region may be contained
   by a root or another region in slot `region`; a settlement, site, or interior may be contained by
   a region or other location in slot `location`. A child region may represent a continent, country,
   province, or other reusable regional scope without adding a second hierarchy field. Read the
   intended graph before correction; containment cycles are invalid.
4. Model initial adjacency only with `game.core.world.location.connected-to`. Its data is `{}`;
   both endpoints carry game.core.world.location; it is stored once with lexically smaller entity ID
   as `from`. Readers inspect incoming and outgoing edges because the convention is undirected.
5. For development fixtures, author the component definitions before entity files and preserve the
   canonical edge order in `catalog/world/relationships.json`. For live authored setup, read the
   intended entities/definitions and submit one effects list ordered entity creation, component
   adds, containment moves, then relationship creates.
6. Replace a closed root/location record as a complete object when correcting it. Do not use merge
   to invent partial state. Visibility is descriptive until an authorised audience feature exists.
7. Prefer the focused location authoring mechanics when building a playable place. Shell creation
   owns only `game.core.world.location`; placement owns only containment; furnishing creation owns
   only `game.core.world.location.furnishing`; furnishing attachment owns only its containment;
   adjacency owns only `game.core.world.location.connected-to`; knowledge attachment owns only the
   `knowledge.about` link; media attachment owns only `game.core.media.visual`. The caller supplies
   all concrete prose, visibility, classifications, and finalized media references.

## Constraints
- Status is exactly draft, active, or archived. Visibility is exactly public, party, or gm.
- Location kind is exactly region, settlement, site, or interior.
- A direct child with kind `region` uses containment slot `region`; a direct child with any other
  location kind uses slot `location`.
- Summary is a trimmed, nonempty string of at most 1,000 Unicode scalar values.
- A furnishing contains exactly status, summary, and visibility. Its location is derived from
  containment slot `furnishing`; an unplaced furnishing is valid draft authoring state.
- Root/location components never contain child IDs, parent IDs, world IDs, campaign IDs, adjacency
  arrays, actor positions, facts, factions, clues, time, terrain, or coordinates.
- A reverse, duplicate, self, non-location, or nonempty-data adjacency violates this topology
  contract. Generic direct relationships remain administrative; later guarded authoring may enforce
  these feature conventions for untrusted input.
- No action mechanic, event, subscription, campaign, movement, map, routing, or player-safe
  visibility projection is created by this contract.

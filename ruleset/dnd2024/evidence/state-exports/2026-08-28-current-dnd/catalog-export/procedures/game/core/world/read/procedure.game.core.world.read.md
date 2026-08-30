---
id: procedure.game.core.world.read
category: game.core.world.read
name: Read bounded trusted-GM world context
governs: query(kind: "graph") for the published world overview, location detail, faction detail, and knowledge detail recipes
status: active
createdBy: "seed"
changeNote: "Seeded from bootstrap file."
---

## Description
Defines five trusted-GM read recipes over the generic bounded graph query. The graph reader is
generic: this procedure owns the world component and relationship vocabulary, the limits, and the
meaning of each recipe. The results are setting context for a GM or future website consumer, not
player-safe views or a map.

## Instructions
1. Read `query(kind: "capabilities")` before using this procedure, then use only
   `query(kind: "graph")` with the exact recipe fields below. Supply the active world-root ID or
   the inspected location/faction ID as the recipe root; no recipe discovers or changes that ID.
2. **World overview:** root the query at the active world root with
   `componentIds: ["game.core.world.root", "game.core.world.location"]`,
   `containmentDepth: 2`,
   `relationshipKinds: ["game.core.world.location.connected-to"]`,
   `relationshipDepth: 1`, `maxNodes: 100`, and `maxEdges: 100`. It returns the root, contained
   regions/places, and canonical adjacency among returned locations.
3. **Location detail:** root the query at one location with
   `componentIds: ["game.core.world.location"]`, `containmentDepth: 1`,
   `relationshipKinds: ["game.core.world.location.connected-to"]`, `relationshipDepth: 1`,
   `maxNodes: 50`, and `maxEdges: 50`. Read the returned node's containment context for its parent
   identity, its direct contents, and incident canonical adjacency; do not treat it as a recursive
   world read.
4. **Faction detail:** root the query at one faction with
   `componentIds: ["game.core.world.faction", "game.core.world.motive"]`,
   `containmentDepth: 0`,
   `relationshipKinds: ["game.core.world.faction.member", "game.core.world.faction.controls", "game.core.world.faction.allied-with", "game.core.world.faction.opposed-to"]`,
   `relationshipDepth: 1`, `maxNodes: 40`, and `maxEdges: 50`. Membership and control are explicit
   non-exclusive relationships; do not infer allegiance, territory, or motives beyond the returned
   data.
5. **Knowledge detail:** root the query at the active world root with
   `componentIds: ["game.core.world.fact", "game.core.world.rumour", "game.core.world.secret", "game.core.world.clue"]`,
   `containmentDepth: 0`,
   `relationshipKinds: ["game.core.world.knowledge.in-world", "game.core.world.knowledge.about", "game.core.world.clue.supports"]`,
   `relationshipDepth: 2`, `maxNodes: 100`, and `maxEdges: 150`. It returns scoped records and
   their target/support provenance; visibility is returned as authored descriptive data.
6. **Map layout:** first query the selected active region with
   `componentIds: ["game.core.world.location", "game.core.world.map.anchor"]`,
   `containmentDepth: 1`, `relationshipKinds: ["game.core.world.location.connected-to"]`,
   `relationshipDepth: 1`, `maxNodes: 50`, and `maxEdges: 100`. Then query its inspected world
   root with `componentIds: ["game.core.world.route"]`, `containmentDepth: 0`,
   `relationshipKinds: ["game.core.world.route.in-world", "game.core.world.route.from", "game.core.world.route.to"]`,
   `relationshipDepth: 2`, `maxNodes: 100`, and `maxEdges: 150`. Construct the documented layout
   only when every active direct region location has one valid unique anchor, every adjacency has
   both displayed endpoints, and every included active route has exactly one valid scope/origin/
   destination link with both endpoints displayed. The output is ordered by permanent IDs and is
   trusted-GM display data, not a route path or player view.
7. Read `truncated` before relying on a result. A `null` value means the recipe fit its declared
   cap; a non-null value names the exhausted cap. Read an individual entity through
   `query(kind: "entities", id: "...")` before changing it.

## Constraints
- These recipes are trusted-GM only. `public`, `party`, and `gm` visibility values are descriptive
  world data, not authorization or player discovery policy.
- The graph reader returns only selected component data, direct containment context, and selected
  relationships in stable order. It does not copy parent/child/adjacency fields into components or
  make a second topology or knowledge model.
- The map-layout recipe returns authored display anchors and route metadata only. It does not
  provide geographic geometry, terrain, distance, route cost, paths, line of sight, travel rules,
  rendered-map data, or a player-facing map. World Feature 8 owns routes/travel and World Feature
  9 owns authored display anchors.
- A query never changes authoritative world state. Its normal query audit entry is expected; it is
  not a world edit. Do not add caching, stored projections, writes, effects, events, subscriptions,
  notifications, or a game-specific C# query to this read contract.


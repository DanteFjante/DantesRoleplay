# World Feature 9 — trusted-GM map-layout handoff

**Audience:** Future website/API implementation  
**Authority:** [`procedure.game.core.world.read`](../../catalog/procedures/game/core/world/procedure.game.core.world.read.md)

Build a map layout with two existing `query(kind: "graph")` calls; no database access, map-specific
API, or inferred spatial rule is required.

1. Query the selected region with location/anchor components, containment depth 1, canonical
   adjacency, relationship depth 1, and caps 50 nodes/100 edges.
2. Query the inspected world root with route components, no containment, route scope/origin/
   destination links, relationship depth 2, and caps 100 nodes/150 edges.
3. Reject the layout if either result is truncated, an active direct location lacks one valid unique
   anchor, an adjacency endpoint is absent, or an active route is malformed/out of region.

The resulting normalized trusted-GM model is ordered by permanent ID:

```text
{
  region: { id, name },
  locations: [{ id, name, kind, summary, x, y }],
  adjacency: [{ fromLocationId, toLocationId }],
  routes: [{ id, name, fromLocationId, toLocationId, mode, durationMinutes, visibility }]
}
```

Coordinates are only display anchors in a 0–1,000 plane. A drawn connector is not a travel path,
distance, terrain claim, or authority over containment, adjacency, routes, or time. This is not a
player-safe response: visibility labels are included but no audience filtering is performed.

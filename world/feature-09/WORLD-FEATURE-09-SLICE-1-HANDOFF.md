# World Feature 9 — Slice 1 implementation handoff

**Assignment ID:** world-feature-09-slice-1-anchor-foundation  
**Status:** Complete and awaiting review  
**Owning plan:** [World Feature 9 dependency plan](WORLD-FEATURE-09-DEPENDENCY-PLAN.md)  
**Exact slice:** Display-only anchor component, spatial procedure, and three confirmed first-region
fixture anchors.  
**Stop point:** Evidence is in the [Slice 1 receipt](WORLD-FEATURE-09-SLICE-1-RECEIPT.md); do not
add a map-specific runtime query or rendering before Slice 2.

## Confirmed anchor layout

- Gate `(150, 650)`, market `(500, 500)`, observatory `(850, 250)`.
- Anchors use a 0–1,000 top-left-origin display plane and apply only to direct active locations of
  the existing fixture region.
- They do not define routes, distance, time, terrain, paths, or player discovery.

## Required Slice 2 boundary

Use only the existing generic graph reader to construct the documented trusted-GM layout model.
It must validate selected-region anchors, topology, and route links without any `game.core` branch
in the generic reader or a new MCP query kind.

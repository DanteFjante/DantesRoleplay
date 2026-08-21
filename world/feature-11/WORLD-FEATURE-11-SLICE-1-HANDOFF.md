# World Feature 11 Slice 1 handoff — faction scope, territory, and front

**Status:** Complete; Slice 2 remains planned  
**Date:** 2026-08-20  
**Plan:** [World Feature 11 dependency plan](WORLD-FEATURE-11-DEPENDENCY-PLAN.md)

## Delivered boundary

- Closed `game.core.world.faction.front` state and one quiet, active Lantern Compact observatory
  front at root minute zero.
- Explicit Compact root scope and exclusive market territorial-controller links, while retaining
  the existing nonexclusive Feature 3 market `controls` claim.
- Explicit front root/faction/observatory links and expanded faction contract ownership.

## Verification and handoff

- Focused fixture tests prove fresh import, closed state, scope/relationship conventions,
  territory conflict rejection, and isolation from agenda, location, clock, route, condition, and
  other world state.
- `roleplay validate catalog` used a disposable database only; no persistent game data changed.
- Slice 2 owns `mechanic.game.core.world.faction.front.advance`, its four-role projections,
  expected-phase action, structural-event/audit evidence, stale/terminal rejection, and full
  Feature 11 acceptance.

## Do not extend in this slice

Do not resolve the front, transfer territory, add a rival faction/front, automate clock-based
pressure, create a subscription, modify routes/maps/conditions, or add a new MCP surface.

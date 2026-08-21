# World Feature 8 — Slice 1 implementation receipt

**Status:** Slice 1 verified  
**Date:** 2026-08-20  
**Roadmap:** [World and lore](../../WORLD_AND_LORE_PLAN.md)

## Delivered

- Added the closed `game.core.world.route` component and schema.
- Added the active `route.feature-08.gate-to-market-on-foot` fixture: one 30-minute `on-foot`
  route from the existing gate to the existing market.
- Added exactly three empty-data route links: route-to-world, route-from-gate, and route-to-market.
- Revised the travel and time procedures to define directional route ownership, Feature 2
  compatibility, and the approved route-derived clock handoff for the later action.
- Added fresh-import and invalid-fixture convention coverage.

## Verification

- Focused Feature 8 plus adjacent movement/clock coverage: **9 passed**.
- `roleplay validate catalog`: **132 records valid** with 15 advisory near-duplicate warnings and
  no catalog error.
- No persistent import or live-data change occurred.

## Deliberate stop point

The on-foot journey mechanic, atomic containment/clock action tests, replay/overflow coverage, and
feature-wide acceptance remain Slice 2. No route selection, reverse route, map geometry, distance,
groups, player authorization, or MCP surface was added.

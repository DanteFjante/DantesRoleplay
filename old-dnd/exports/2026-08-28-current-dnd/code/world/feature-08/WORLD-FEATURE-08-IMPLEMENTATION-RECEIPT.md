# World Feature 8 implementation receipt — named on-foot journey

**Status:** Feature 8 verified  
**Date:** 2026-08-20  
**Roadmap:** [World and lore](../../WORLD_AND_LORE_PLAN.md)

## Delivered

- A closed, directed, 30-minute `on-foot` route fixture from the existing gate to the existing
  market, scoped by explicit world/origin/destination links.
- `mechanic.game.core.world.route.travel-on-foot`, with five explicit roles and exact `{}` input.
- One atomic ordered action: move the active traveller to destination `presence`, then replace the
  same root clock with its route-derived minute/revision values.
- Revisions to the existing travel and time contracts. Feature 2 local adjacent movement stays
  route-free and time-free.

## Verification

- Focused Feature 8 plus travel, clock, projection, and action-runner coverage: **54 passed**.
- Full repository suite: **411 passed, 0 failed**.
- `roleplay validate catalog`: **133 records valid**. It reports 17 advisory near-duplicate
  warnings, including the intentional relationship between local movement and named-route travel;
  no catalog error occurred.
- `git diff --check`: no whitespace errors (only repository-wide LF/CRLF conversion advisories).
- No persistent import or live-data change occurred.

## Deliberate boundary

The feature provides one direct on-foot route only. It does not add route selection, reverse-route
inference, multi-leg journeys, distances, terrain, conditions, waiting, groups, schedules, map
geometry, player authorization, a new MCP surface, a semantic journey event, or new kernel state.

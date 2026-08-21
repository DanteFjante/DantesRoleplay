# World Feature 13 implementation receipt — generic aerial-conveyance journey

**Status:** Feature 13 verified  
**Date:** 2026-08-20  
**Plan:** [generic aerial-conveyance journey](WORLD-FEATURE-13-DEPENDENCY-PLAN.md)

## Delivered

- `mechanic.game.core.world.aerial-conveyance.travel`, with six explicit roles and closed `{}`
  input.
- An atomic three-effect journey: move the aerial conveyance, move its active rider, then replace
  the scoped root clock.
- Exact integer `ceiling(distanceUnits / speedUnitsPerMinute)` timing: the fixture's 600 distance
  and 30 speed produce 20 minutes; non-divisible timing is covered with 600/32 = 19.
- Travel and time contracts now describe explicit aerial journeys without making roads, ground
  routes, adjacency, or map connectors flight authorization.

## Verification

- Focused Feature 13 coverage: **5 passed, 0 failed**.
- Full repository suite: **437 passed, 0 failed**.
- `roleplay validate catalog`: **154 records valid** with 24 advisory near-duplicate warnings and
  no catalog errors. The disposable validator touched no live data.
- `git diff --check`: no whitespace errors; repository-wide line-ending advisories only.

## Proven boundary

The approved non-adjacent gate→observatory route succeeds without a ground connection. A split
rider/conveyance, archived conveyance, clock overflow, or old-origin replay makes no partial move
or clock change. The action accepts no caller-supplied duration, altitude, route, location, or
effects, and introduces no free flight, passenger, cargo, ownership, taming, health, combat,
weather, path, map, campaign, quest, subscription, event type, or MCP-surface behavior.

## Acceptance

No persistent catalog import or live game-data change occurred. The user accepted Feature 13 on
2026-08-20; it is a verified prerequisite for later features.

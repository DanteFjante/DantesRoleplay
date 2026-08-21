# World Feature 12 implementation receipt — generic ground conveyance journey

**Status:** Feature 12 verified  
**Date:** 2026-08-20  
**Plan:** [generic ground-conveyance revision](WORLD-FEATURE-12-GROUND-CONVEYANCE-PLAN.md)

## Delivered

- `mechanic.game.core.world.conveyance.travel-ground`, with six explicit roles and closed `{}`
  input.
- An atomic three-effect journey: move the ground conveyance, move its active driver, then replace
  the scoped root clock.
- Exact integer `ceiling(distanceUnits / speedUnitsPerMinute)` timing: the fixture's 300 distance
  and 15 speed produce 20 minutes; non-divisible timing is covered with 300/16 = 19.
- Travel and time contracts now describe the generic, ground-only action without changing
  adjacent movement or Feature 8 on-foot travel.

## Verification

- Focused Feature 12 coverage: **5 passed, 0 failed**.
- Full repository suite: **432 passed, 0 failed**.
- `roleplay validate catalog`: **149 records valid** with 23 advisory near-duplicate warnings and
  no catalog errors. The disposable validator touched no live data.
- `git diff --check`: no whitespace errors; repository-wide line-ending advisories only.

## Proven boundary

An archived conveyance, clock overflow, or old-origin replay makes no partial move or clock
change. The action accepts no caller-supplied duration, vehicle kind, route, location, or effects.
It introduces no passenger, cargo, horse/engine, ownership, inventory, terrain, condition,
air/water/space, map, campaign, quest, subscription, event type, or MCP-surface behavior.

## Acceptance

No persistent catalog import or live game-data change occurred. The user accepted Feature 12 on
2026-08-20; it is a verified prerequisite for later features.

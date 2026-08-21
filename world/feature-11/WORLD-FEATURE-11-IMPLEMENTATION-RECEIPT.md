# World Feature 11 implementation receipt — faction fronts and territory

**Status:** Feature 11 verified  
**Date:** 2026-08-20  
**Plan:** [World Feature 11 dependency plan](WORLD-FEATURE-11-DEPENDENCY-PLAN.md)

## Delivered

- One active quiet Lantern Compact front contesting the observatory, with explicit compact/root/
  observatory scope links and minute-zero phase evidence.
- One exclusive Compact territorial controller for the market, deliberately distinct from the
  existing nonexclusive Feature 3 `controls` claim.
- `mechanic.game.core.world.faction.front.advance`: a four-role, closed-input, manual transition
  from quiet to rising or rising to pressing. It records the current root minute and returns one
  complete front replacement.

## Verification

- Focused Feature 3 and 11 coverage: **9 passed, 0 failed**.
- Full repository suite: **427 passed, 0 failed**.
- `roleplay validate catalog`: **144 records valid** with 22 advisory near-duplicate warnings and
  no catalog errors. The disposable validator touched no live data.
- `git diff --check`: no whitespace errors; repository-wide line-ending advisories only.

## Proven boundary

Invalid/stale/terminal actions make no change. A successful advance changes only the front and
produces the existing structural component-replacement event and action audit. It never changes
territory, broad control claims, agenda, clock, conditions, routes, topology, map data, knowledge,
campaigns, quests, subscriptions, or notifications.

## Acceptance

No persistent catalog import or live game-data change occurred. Feature 11 is verified and later
plans may depend on its front/territory convention.

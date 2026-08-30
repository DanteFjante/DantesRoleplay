---
id: procedure.game.core.world.time
category: game.core.world.time
name: Govern shared-game world time
governs: commit(kind: "component") declaring game.core.world.clock; commit(kind: "effects") recording or correcting a root clock; commit(kind: "action") advancing one root clock directly or through a declared route, ground-conveyance, or aerial-conveyance journey
status: active
createdBy: "seed"
changeNote: "Re-seeded: the bootstrap file changed."
---

## Description
Defines one explicit in-world coordinate on a world root. It is authored state, not wall-clock time,
a date system, a schedule, travel cost, or campaign duplicate.

## Instructions
1. Attach `game.core.world.clock` directly to one active world root. Its complete data is
   `calendarId`, nonnegative `currentMinute`, and nonnegative `revision` within the declared bounds.
2. The fixture calendar identity is `lantern-compact-epoch`, at minute zero and revision zero.
   Calendar identity is an immutable convention for this feature, not a date-format label.
3. Correct clock state only with an inspected complete component replacement.
   `mechanic.game.core.world.clock.advance` alone advances elapsed play: supply the root as role
   `world` and exact input `{ "minutes": n }`, where n is 1–1,440. The approved
   `mechanic.game.core.world.route.travel-on-foot` may also replace that same clock, but only with
   the selected route's validated `durationMinutes`; it cannot accept caller-supplied time.
   `mechanic.game.core.world.conveyance.travel-ground` may replace it only with validated integer
   `ceiling(distanceUnits / speedUnitsPerMinute)` from its selected ground route and conveyance;
   it likewise cannot accept caller-supplied time.
   `mechanic.game.core.world.aerial-conveyance.travel` may replace it only with validated integer
   `ceiling(distanceUnits / speedUnitsPerMinute)` from its selected aerial route and conveyance;
   it likewise cannot accept caller-supplied time.

## Constraints
- Exactly one root clock exists by this feature convention. Locations, travellers, campaigns,
  routes, events, factions, and knowledge records never carry time fields.
- Clock data is closed. Calendar identity is trimmed nonempty text; minute/revision are bounded
  safe integers. There is no date, duration, scheduler, route, operation ID, or mutable history.
- The advance action preserves calendar identity, adds minutes monotonically, increments revision
  once, and rejects overflow or corrupt root/clock state without changing anything. Its action
  audit, existing structural events, and the scoped `game.core.world.clock.advanced` event are
  evidence. The semantic event follows structural effects in the same root transaction, names the
  root as both `scope` and `worldId`, and records closed before/after minute and revision values.
- A route journey preserves calendar identity, adds its declared duration monotonically, and
  increments revision once in the same transaction as traveller relocation. A ground-conveyance
  journey does the same using its derived duration while relocating its conveyance and driver. An
  aerial-conveyance journey does the same while relocating its conveyance and rider.
  Route data never changes clock ownership, bounds, calendar identity, or correction policy.
- An accepted complete root-clock correction remains administrative evidence. A scoped world
  condition may reconcile its own derived status and route availability from the resulting minute,
  but cannot change clock ownership, correction policy, calendar identity, or clock bounds.
- Other than the accepted scoped clock-advance event, this contract adds no subscription,
  notification, campaign, quest, schedule, real-time synchronization, or MCP surface.

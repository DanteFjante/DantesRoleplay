---
id: procedure.game.core.world.condition
category: game.core.world.condition
name: Govern scheduled world route closures
governs: commit(kind: "component") declaring game.core.world.condition or game.core.world.route.availability; commit(kind: "effects") recording or correcting one reviewed condition or route availability
status: active
createdBy: "seed"
changeNote: "Seeded from bootstrap file."
---

## Description
Defines one deterministic, clock-derived route-closure condition. The condition's scope is explicit
world and route relationships; its stored status and the route's availability are reconciled from
the resulting root-clock minute by one fixed reaction.

## Instructions
1. A condition entity has exactly one closed `game.core.world.condition` record. The only initial
   kind is `route-closure`; its source is descriptive authored text, and its start/end minutes are
   the interval evidence rather than a scheduler or duration.
2. Give every condition exactly one empty-data `game.core.world.condition.in-world` link to an
   active root and exactly one empty-data `game.core.world.condition.affects` link to one active
   route in that same world. The condition's directed links, not component fields, supply scope.
3. A condition-scoped route has exactly one closed `game.core.world.route.availability` record.
   The reviewed fixture begins at root minute zero as `scheduled` and `open` for interval
   `[60, 180)`.
4. For resulting root minute `m`, the required paired state is `scheduled`/`open` when `m < start`,
   `active`/`closed` when `start <= m < end`, and `expired`/`open` when `m >= end`. The fixed
   clock reaction performs that reconciliation from accepted root-clock replacements; it never
   changes the interval evidence itself.
5. Record reviewed condition state with complete component replacements and explicit scope links.
   Do not hand-edit route eligibility from travel. The route-journey integration consumes
   availability but does not own condition state.

## Constraints
- Condition and availability data are closed. Summary/source are trimmed nonempty bounded text;
  start is a bounded nonnegative integer, end is bounded positive integer, and start is strictly
  less than end. Unknown status/kind/visibility, malformed JSON, extra fields, or invalid interval
  data is not a condition.
- Scope links are directed, non-self, exact `{}` records. Missing, duplicate, reversed, nonempty,
  cross-world, or non-route scope violates this feature convention. A condition carries no root,
  route, clock, location, campaign, quest, duration, history, severity, stack, mode, or caller
  supplied effect field.
- Availability is condition-owned current state only. It does not alter route metadata, topology,
  containment, map anchors, knowledge, factions, campaigns, quests, or the root clock.
- The one fixed subscription answers only root-clock replacement events for the reviewed root; it
  may replace only the fixed condition and route availability. Derived replacements cannot match
  its clock-definition filter. This feature creates no scheduler, polling loop, wall-clock use,
  event type, notification, query kind, map behavior, player filtering, authorization,
  campaign/quest state, or MCP surface.

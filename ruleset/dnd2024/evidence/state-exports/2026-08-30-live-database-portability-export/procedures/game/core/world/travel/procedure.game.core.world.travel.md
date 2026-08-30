---
id: procedure.game.core.world.travel
category: game.core.world.travel
name: Govern adjacent movement and named conveyance journeys
governs: commit(kind: "component") declaring game.core.world.traveller, game.core.world.route, game.core.world.conveyance, game.core.world.conveyance-route, game.core.world.aerial-conveyance, game.core.world.aerial-route, or game.core.world.teleport-gate; commit(kind: "effects") recording or removing a traveller marker or reviewed route/conveyance fixture; commit(kind: "action") moving one active traveller between adjacent locations or over one declared route; query(kind: "journey-plan") planning a bounded on-foot itinerary
status: active
createdBy: "seed"
changeNote: "Seeded from bootstrap file."
---

## Description
Defines the shared-game path for placing a traveller in a world fixture and resolving either an
adjacent local move, one named on-foot route journey, one named ground-conveyance journey, or a
later named aerial-conveyance journey. It consumes Feature 1 containment and
canonical adjacency; containment remains the only current-location state.

## Instructions
1. Declare `game.core.world.traveller` once. Its data is exactly `{ "status": "active" }` and it
   marks only that the entity may use this movement capability.
2. Record or remove the marker with an inspected `commit(kind: "effects")` list. A fixture traveller
   is placed directly in an active location using containment slot `presence`; do not store origin
   or destination in component data.
3. A route entity has one closed `game.core.world.route` record: active/archived lifecycle,
   summary, descriptive visibility, exactly `on-foot` mode, and a fixed 1–1,440 minute duration.
   It has exactly one empty-data `game.core.world.route.in-world` link to an active root, one
   `game.core.world.route.from` link to its active origin location, and one
   `game.core.world.route.to` link to its distinct active destination location. The two endpoints
   are active sibling `location` entities with exactly one canonical adjacency. Direction is from
   the two distinct link kinds; adjacency never grants a reverse route.
   A reviewed condition-scoped route may additionally carry the condition-owned closed
   `game.core.world.route.availability` record; the Feature 10 reaction, rather than travel,
   reconciles that record from the root clock.
4. Before resolving a local move, read the active movement mechanic and inspect the traveller, claimed
   origin, and claimed destination. Supply those exact entity IDs in roles `traveller`, `origin`,
   and `destination`, with input `{}`.
5. `mechanic.game.core.world.location.move` proves the traveller is currently in origin, both
   locations are active siblings, and the stored canonical adjacency connects them. It returns one
   `containment.move` effect to destination slot `presence` or rejects without effects.
6. `mechanic.game.core.world.route.travel-on-foot` is the only route journey path. Supply exact
   roles `traveller`, `origin`, `destination`, `route`, and `world` with input `{}`. It proves the
   route direction/scope and condition-owned `open` availability before moving the traveller to
   `presence` while replacing the same root clock from the route's declared duration in one
   transaction. Missing, malformed, or `closed` availability rejects with no effects.
7. Read back the traveller after a successful action. Its current location is the resulting
   containment; `world.containment.moved` is the existing structural evidence. A repeated request
   using the old origin is stale and rejects.
8. A generic ground conveyance has one closed `game.core.world.conveyance` record and is directly
   contained at an active location in `presence`. A distinct ground conveyance route has one closed
   `game.core.world.conveyance-route` record plus exactly one empty-data `.in-world`, `.from`, and
   `.to` link. It is not the Feature 8 on-foot route and stores distance rather than duration;
   conveyance speed and route distance derive time only in the named action below.
9. `mechanic.game.core.world.conveyance.travel-ground` is the only ground-conveyance journey
   path. Supply exact roles `driver`, `conveyance`, `origin`, `destination`, `conveyanceRoute`,
   and `world` with input `{}`. It proves that active driver and conveyance share origin/
   `presence`, validates the route's direction, scope, and canonical adjacency, derives minutes as
   `ceiling(distanceUnits / speedUnitsPerMinute)`, then moves conveyance, moves driver, and
   replaces the same root clock in that order in one transaction. Missing, malformed, archived,
   mismatched, stale, or overflow state rejects with no effects.
10. A generic aerial conveyance has one closed `game.core.world.aerial-conveyance` record and is
   directly contained at an active launch/landing location in `presence`. A distinct aerial route
   has one closed `game.core.world.aerial-route` record plus exactly one empty-data `.in-world`,
   `.from`, and `.to` link. Its endpoints need not be ground-adjacent and it stores distance rather
   than duration; its dedicated aerial action derives time from its speed and distance.
11. `mechanic.game.core.world.aerial-conveyance.travel` is the only aerial-conveyance journey
   path. Supply exact roles `rider`, `conveyance`, `origin`, `destination`, `aerialRoute`, and
   `world` with input `{}`. It proves that active rider and conveyance share origin/`presence`,
   validates the aerial route's direction and scope, derives minutes as
   `ceiling(distanceUnits / speedUnitsPerMinute)`, then moves conveyance, moves rider, and
   replaces the same root clock in that order in one transaction. It never reads ground adjacency,
   roads, ground routes, or map connectors. Missing, malformed, archived, mismatched, stale, or
   overflow state rejects with no effects.
12. `query(kind: "journey-plan")` is a trusted-GM, read-only on-foot planning path. Supply only
   `worldId`, `travellerId`, and `destinationId`; origin is derived from traveller containment. It
   selects only active/open, correctly scoped Feature 8 on-foot routes with valid canonical
   adjacency and active sibling endpoints. It returns a shortest-total-duration sequence of at
   most 20 legs and 14,400 minutes, breaking equal totals by lexicographic route-ID sequence then
   destination IDs. Its closed result is `ready`, `already-there`, `unreachable`, `blocked`, or
   `too-long`, with no partial legs for an empty status. Clock revision is advisory only: execute
   at most the first ready leg through Feature 8, then request a fresh plan from actual
   containment.
13. A fixed teleport gate has one closed `game.core.world.teleport-gate` record and is directly
   contained at its active origin location in `presence`. It has exactly one empty-data `.in-world`
   link and one empty-data `.to` link to a distinct active destination in the same world. It is
   neither a route nor a journey: its dedicated action moves only one co-located traveller
   instantly and never changes the root clock.
14. `mechanic.game.core.world.teleport-gate.teleport` is the only fixed-portal path. Supply exact
   roles `traveller`, `portal`, `origin`, `destination`, and `world` with input `{}`. It proves
   active co-location, exact portal scope/destination, and valid unchanged clock state, then
   returns exactly one traveller containment move. It never reads routes, adjacency, maps, or
   itinerary plans and never changes the clock.

## Constraints
- Traveller data is exactly one active status. Missing, null, extra, malformed, or inactive state
  is not movement eligibility.
- Only the movement mechanic decides whether a travel request succeeds. Do not use direct effects
  to narrate a player move or trust caller-supplied adjacency, result, route, time, or slot values.
- Origin/destination must be distinct active `game.core.world.location` entities directly contained
  by the same parent in slot `location`; the traveller must be directly contained in origin at
  `presence`.
- A connection must be the existing empty-data `game.core.world.location.connected-to` convention.
  A route is neither a location nor another adjacency edge. It has no endpoint, world, clock,
  current position, distance, speed, geometry, terrain, condition, party, campaign, or access
  field; scope and endpoints are its three directed links.
- The only initial route mode is `on-foot`. A route journey requires condition-owned `open`
  availability, uses its declared duration and the root-clock contract, and does not infer a
  reverse route, select a route, accept a duration, or change the local Feature 2 move's
  no-time/no-route behavior.
- Ground conveyance records are ground-only and closed. They carry no vehicle kind, owner, driver,
  passenger, cargo, location, speed-on-route, duration, terrain, path, condition, or clock copy.
  The ground-conveyance action derives duration from conveyance speed and route distance, but may
  not accept it from the caller or grant air/water/space travel.
- Aerial conveyance records are air-only and closed. They carry no vehicle kind, owner, rider,
  passenger, cargo, location, altitude, duration, terrain, path, condition, or clock copy. An
  aerial route is separate from roads, ground routes, map connectors, and ground adjacency; its
  dedicated action derives duration from aerial conveyance speed and route distance, but
  may not accept it from the caller or grant ground/water/space travel.
- The Feature 14 `journey-plan` never moves a traveller, creates a journey, advances or reserves
  time, accepts an origin/leg/route/duration/availability/effect from a caller, or treats a prior
  plan as authorization. It is on-foot only; Feature 16's separately governed `itinerary-plan`
  adds selected conveyance and fixed-portal planning without changing any movement owner.
- A fixed portal carries no traveller, owner, key, charge, spell, item, current-location,
  destination, duration, clock, route, availability, cargo, or passenger field. Its containment
  and directed links are its only first-slice origin/destination/world truth; roads, adjacency,
  cart/flight routes, and a prior itinerary grant it no permission.
- This contract does not move locations, regions, items, factions, or unmarked entities, and does
  not create a clock, event type, subscription, map, campaign, quest, or MCP surface.

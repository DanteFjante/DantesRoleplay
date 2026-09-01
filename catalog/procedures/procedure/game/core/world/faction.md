---
id: procedure.game.core.world.faction
category: game.core.world.faction
name: Govern shared-game factions, fronts, and recurring motives
governs: commit(kind: "component") declaring game.core.world.faction, game.core.world.faction.front, or game.core.world.motive; commit(kind: "effects") recording or correcting faction/front/motive state and faction relationships; commit(kind: "action") advancing one active ready faction agenda or one scoped faction front
status: active
createdBy: "seed"
changeNote: "Seeded from bootstrap file."
---

## Description
Defines shared world-lore state for factions and recurring actors. It records a faction's closed
descriptive state and agenda, an actor's durable motive, one manual faction pressure front, and
explicit faction links. Its action paths advance a ready agenda or a confirmed front phase; it does
not decide why an advance occurred, allegiance, simulation, or campaign/quest state.

## Instructions
1. Declare `game.core.world.faction` and `game.core.world.motive` once. Both are complete closed
   objects; use the entity name as the display name and do not duplicate it in component data.
2. A faction record contains status, summary, visibility, 1–5 goals, 1–5 methods, 0–10 descriptive
   assets, and one agenda with state `ready` or `advanced`. Assets are descriptions, not entity IDs.
   A motive record contains only status, summary, and descriptive visibility.
3. Set up reviewed world state with one inspected `commit(kind: "effects")` list, ordered entity
   creation, complete component additions or replacements, then relationship creation. Correct a
   closed component as a complete replacement; do not merge a partial motive or agenda.
4. Use exact empty object data `{}` for all faction links. `game.core.world.faction.member` is
   directed faction → actor; `game.core.world.faction.controls` is directed faction → claimed
   world entity. Neither asserts exclusive loyalty or control.
5. `game.core.world.faction.allied-with` and `game.core.world.faction.opposed-to` connect two
   faction entities. Store either one once in lexical entity-ID orientation. The two kinds cannot
   coexist for the same unordered pair.
6. Read the resulting entities and all relevant incoming/outgoing links back after authoring.
   `mechanic.game.core.world.faction.agenda` is the only path that advances an agenda. Supply one
   active faction as role `faction` and input `{}`; it replaces the complete component only when
   its agenda is exactly `ready`, making it `advanced` while preserving all other state.
7. A world-scoped faction has exactly one empty-data `game.core.world.faction.in-world` link to
   its active root. `game.core.world.faction.territory-controls` is an empty-data faction → active
   location link in that same root, with at most one faction controller per location. It is a
   narrow exclusive territorial controller, while `controls` stays a broad nonexclusive claim.
8. A front entity has exactly one closed `game.core.world.faction.front` record and exactly one
   each empty-data `front.in-world`, `front.for-faction`, and `front.contests` link. The linked
   active faction and contested active location must share the front's root scope. The reviewed
   front begins `active`, `quiet`, and at root minute zero; it does not transfer territory.
9. `mechanic.game.core.world.faction.front.advance` is the only manual front-advance path. Supply
   exact roles `front`, `faction`, `location`, and `world` with exactly
   `{ "expectedPhase": "quiet" | "rising" }`. It proves all scope links and writes one complete
   front replacement: `quiet → rising` or `rising → pressing`, preserving the closed front record
   except for phase and the root clock's current minute. A pressing, inactive, malformed, or stale
   front rejects without effects.

## Constraints
- Status is exactly draft, active, or archived. Visibility is exactly public, party, or gm. Every
  summary, goal, method, and asset is trimmed, nonempty text within its declared schema limit.
- Faction/motive data contains no entity-ID lists, parent/location/current-position fields,
  campaign state, quest state, NPC/character classifier, clock, history, or derived relationship
  list.
- Member/control links reject self links, reverse orientation, non-faction source, and nonempty
  data as this feature's authoring convention. Multiple affiliations and broad `controls` claims
  are intentionally permitted and are not silently treated as exclusive.
- Faction root scope, territorial controller, and all front links are directed, non-self, exact
  `{}` records. Missing, duplicate, reversed, nonempty, inactive, non-faction, non-location, or
  cross-world endpoints violate this feature convention. A second territorial controller for one
  active location is invalid; this feature never resolves a conflict by choosing or transferring a
  controller.
- Allied/opposed links reject self links, reverse/duplicate orientation, non-faction endpoints,
  nonempty data, and a contradictory kind for the same unordered pair.
- The agenda action accepts no caller-supplied outcome, summary, state, transition, effect, or
  reason. Draft, archived, malformed, missing, unknown, or already-advanced faction state rejects
  without a component replacement. Its existing `world.component.replaced` structural event and
  action audit are the success evidence.
- The front action accepts no caller-supplied next phase, minute, effect, cause, target, territory
  result, or decision. It changes no agenda, broad control claim, territorial controller, clock,
  route, condition, location/topology, map, knowledge, campaign, quest, notification, or
  subscription. Its existing structural replacement event and action audit are its only evidence.
- This contract creates no semantic faction/front event, subscription, notification, campaign,
  quest, player-safe visibility projection, map, clock, autonomous faction simulation, or MCP
  surface.

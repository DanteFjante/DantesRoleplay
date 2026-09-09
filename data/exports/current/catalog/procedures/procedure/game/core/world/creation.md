---
id: procedure.game.core.world.creation
category: game.core.world.creation
name: Create and verify an extensive world
governs: reviewed creation of a new world through system.world-state.sync and application mechanics, followed by intent-driven play verification and durable continuity recording
status: active
createdBy: "seed"
changeNote: "Re-seeded: the bootstrap file changed."
---

## Description
Creates an original, playable world as reviewed durable state rather than as prose held only in a
conversation. It coordinates the narrower location, faction, knowledge, chronology, time, travel,
media, campaign, action, and storytelling contracts without taking ownership away from them.

## Matches

## Instructions
1. Orient first. Read `procedure.system.use`, this procedure, and only the narrower contracts needed
   for the proposed features. Inspect the target application, its active resolution fingerprint,
   the target runtime state space, existing namespaces, and existing world roots before writing.
2. Write a bounded requirements statement before designing details. It must state the intended
   genre and premise, required geography depth, playable locations, factions, recurring people,
   public and hidden knowledge, chronology, travel choices, opening situation, media expectations,
   rule-system boundary, and whether this is a machine simulation. Preserve it as a world-scoped
   fact with appropriate classification so later authors can explain why the world has its shape.
3. Choose one stable world ID. Before creating any other identity, search for collisions and ensure
   every new dotted namespace is reviewed, enabled, and described with the kinds of entities it may
   contain. Never place a record in a convenient but semantically wrong namespace.
4. Establish one active `game.core.world.root` and its single `game.core.world.clock`. The root owns
   the setting premise and visibility; the clock owns only in-world time. Application publication,
   campaigns, schedules, transcript text, and wall-clock time do not belong on the world root.
5. Build geography from broad regions toward playable locations and interiors. Use containment for
   nesting and the location contract for closed descriptive state. Every playable location needs a
   concise sensory and functional summary, a visibility decision, and enough distinction to support
   a choice. Do not flatten every place directly beneath the root.
6. Add explicit travel topology only after containment is correct. Record adjacency, routes,
   availability, portals, or conveyance routes through their owning contracts. Do not infer travel
   permission from similar names, map proximity, or descriptive prose.
7. Add at least two factions with incompatible pressures when the requirements call for active
   conflict. Give each faction explicit goals, methods, assets, visibility, and one actionable
   agenda. Link factions to their world and territories explicitly. Add recurring actors separately
   with durable motives; a faction record is not a substitute for a person.
8. Turn setting claims into knowledge records. Separate public facts, party knowledge, GM facts,
   secrets, clues, and rumours; classify each and link it both to its world and to the entity it is
   about. Record provenance. Never rely on an assistant transcript as the only owner of a truth.
9. Add chronology records for the events needed to understand the present conflict. Use one
   calendar identity shared with the root clock, explicit minute coordinates, readable date labels,
   precision, visibility, and about/in-world links. Chronology describes history; it does not move
   current time.
10. Add maps and media only through authorized blob-backed entity media and map contracts. Store
    role, order, audience, alt text, caption, provenance, and blob identity. Never introduce a
    hand-maintained filesystem path as runtime media authority.
11. Keep campaign and opening-play state separate from world state. A campaign may reference the
    world and select a current scene, but it must not duplicate world facts, locations, or clocks.
    Create a player character only from explicit user choices or an explicitly authorized machine-
    simulation default, and mark it as a traveller before using travel mechanics.
12. Author in coherent bounded phases. Dry-run each exact phase, inspect every proposed event and
    guard result, then commit the identical payload. Use the application world synchronizer for an
    existing application root and generic ECS only for reviewed bootstrap or compatibility work.
    Read back the root, representative records, containment, relationships, and component values.
13. Verify play through intent. Exercise at least one movement or travel action, one rule-system
    check when applicable, one clock advance, and one changing world pressure such as a faction
    agenda. Supply declared roles and closed inputs; never narrate success before the mechanic does.
14. After each play turn, preserve the exact player text and player-visible assistant reply in the
    durable play conversation. Update the typed situation with participants and location when the
    response establishes them, and persist only justified durable truths with their establishing
    message and situation. Mechanic effects remain authoritative over narrative continuity.
15. If intent resolution or the Inner AI cannot complete the requested operation, do not invent a
    successful receipt. Teach a reusable route only after a verified execution exists; create or
    revise an owning action only when no suitable action exists; otherwise submit an immutable
    system feedback report with reproduction steps, expected behavior, related operation IDs, and
    consulted procedures.
16. Finish with an acceptance readback: requirements fact present; namespaces conforming; one root
    and clock; nested geography; usable travel choices; factions and motives; classified knowledge;
    chronology; optional authorized media; application fingerprint binding; play transcript;
    current situation; durable truths; and no fabricated player choices, visits, outcomes, or rules.

## Constraints
- This procedure coordinates existing owners; it does not define a new world schema or game rule.
- C# remains a generic transactional host. Application-specific rules and projections remain in
  catalog JavaScript and contracts.
- Disabled or deleted records are not silently treated as current world content.
- Player, party, GM, and private visibility must be enforced before information reaches a website,
  AI prompt, tool result, transcript continuation, or media response.
- Exact qualified inspection remains possible for authorized operators even when normal discovery
  hides disabled, malformed, or shadowed records.
- A machine simulation may make its own design decisions, but it must label them as simulated and
  must not present them as choices, visits, or statements made by a real player.

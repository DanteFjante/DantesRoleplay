# World and lore implementation plan

Status: **Draft — planning only; no world-content slice is authorised**  
Last updated: 2026-08-20

## Execution rule

Use [GAME_SYSTEM_MASTER_PLAN.md](GAME_SYSTEM_MASTER_PLAN.md) for cross-subsystem ownership,
[TERRA-FEATURE-PLANNING-GUIDE.md](ruleset/dnd2024/TERRA-FEATURE-PLANNING-GUIDE.md) for plan quality,
and a populated [SUBSYSTEM_IMPLEMENTATION_HANDOFF.md](SUBSYSTEM_IMPLEMENTATION_HANDOFF.md) for the active assignment. Implement one reviewed delivery
slice, meet its exit gate, record evidence, and stop.

## Goal

Provide a persistent world that a fresh GM model can inspect and reason about: places, physical
containment, travel links, factions, NPC motives, facts, rumours, secrets, and an explicit campaign
clock. The world remains useful without a campaign generator, website, map, or local model.

World concepts are dynamic content over the generic entity-component model. No location, faction,
lore, travel, or time vocabulary belongs in the C# kernel.

## First playable world

The first verified world contains:

- one world root;
- one region and three locations;
- explicit parent/containment and travel-adjacency relationships;
- one faction with a current agenda;
- two NPCs linked to locations/faction and carrying motives;
- three knowledge records: one public fact, one rumour, and one GM-only truth;
- one clue linking a visible discovery to a hidden truth;
- one current campaign time value;
- one safe movement mechanic and one fact-reveal mechanic.

This is enough for campaign, quest, and session plans to reference a real setting without building
a full map or simulation.

## Ownership decisions

### World root

Proposed component: world.root.

It owns world name, short premise, lifecycle status, calendar/time convention, creation source,
default visibility policy, and optional campaign-root relationships. It does not contain lists of
every location, faction, character, or fact. Those are entities linked to it.

### Locations

Proposed component: world.location.

It owns display summary, location kind, status, visibility, and optional map/display metadata.
Physical membership uses containment: a room belongs to a building, a building to a settlement,
and actors/items are contained at their current location.

Travel adjacency is a relationship, not containment. A closed relationship convention records
origin/destination, directionality, and stable travel-edge identity. Distance, terrain, time, and
risk remain absent until the travel feature owns them.

### Factions

Proposed component: world.faction.

It owns faction goals, methods, current agenda, known assets, status, and visibility. Membership,
alliance, rivalry, control, and interest use relationships. A faction does not directly embed full
NPC/location lists.

Faction agenda progression is a mechanic/event concern. The component records current authoritative
state, not speculative future narration.

Recurring NPC motive state is also owned by this plan because it describes what a world actor
wants across campaign and session boundaries. Slice 0 must choose its exact component ID and
shape after an ownership search. The campaign and storytelling plans consume it; neither defines a
second motive representation.

### Knowledge

Proposed component family:

- world.fact for an asserted persistent fact;
- world.rumour for a claim whose truth is uncertain to the audience;
- world.secret for GM-only authoritative truth;
- world.clue for discoverable evidence pointing to a fact/secret/entity.

Every knowledge record identifies source/provenance, visibility, certainty semantics, summary, and
relevant entity relationships. A rumour becoming confirmed is an explicit state transition, not
the GM silently treating prose as fact.

Initial trusted MCP use is GM scope. Visibility metadata is descriptive until an authenticated
audience policy enforces it.

### Time

Proposed component: world.clock.

It owns one explicit current campaign time value, calendar identity, revision, and last-advance
operation. Time advances only through a named mechanic/effect-producing action. There is no
background scheduler and no dependence on wall-clock time.

## Proposed relationships

- world.contains-region or generic containment for world/region/location hierarchy;
- location.connected-to for travel adjacency;
- faction.member for faction-to-character membership;
- faction.controls for faction-to-location/item relationship;
- faction.allied-with and faction.opposed-to;
- knowledge.about for fact/rumour/secret/clue targets;
- clue.supports for clue-to-fact/secret relationships;
- character.located-at through containment, not a duplicated location-id component.

Exact relation IDs and directionality must be ratified before Slice 1. Symmetric relationships need
one canonical storage/order rule so duplicates cannot be created in reverse order.

## Mechanics and procedures

Proposed versioned mechanics:

- mechanic.world.location.move validates allowed containment/travel transition and proposes
  containment.move;
- mechanic.world.fact.record creates or corrects one knowledge record under a governed authoring
  context;
- mechanic.world.fact.reveal changes audience-visible knowledge without changing the hidden truth;
- mechanic.world.faction.agenda advances one explicit agenda state;
- mechanic.world.clock.advance advances time deterministically from validated input;
- mechanic.world.opportunity.evaluate is deferred to campaign/quest opportunity planning.

Required procedures:

- procedure.world.create
- procedure.world.location
- procedure.world.travel
- procedure.world.knowledge
- procedure.world.faction
- procedure.world.time
- procedure.world.inspect

Each contract states component owner, closed state vocabulary, visibility assumption, source,
normal/correction path, event behavior, tests, and recovery calls.

## Delivery slices

### Slice 0 — ratify the fixture world and IDs

Write the complete small-world example and decide exact component/relation IDs, including recurring
NPC motive ownership, status vocabulary, missing-versus-empty behavior, visibility fields, and the
world-root relationship convention. Search existing live components/mechanics/relationships for
owners and overlaps. Record how the campaign references the world root without copying it.

**Acceptance:** every starting entity and link has one owner, and no field duplicates containment
or a relationship.

### Slice 1 — world root and locations

The full dependency, ownership, fixture, validation, and implementation contract is in
[World Feature 1](world/feature-01/WORLD-FEATURE-01-DEPENDENCY-PLAN.md). That plan's permanent
vocabulary must be confirmed before this slice is assigned.

Add world.root and world.location contracts/definitions plus their safe recording path. Create the
fixture world root, region, and three locations through catalog-owned records or governed effects.
Add containment hierarchy and location adjacency.

**Acceptance:** query reconstructs the hierarchy and connections; invalid parent, self-link,
duplicate/reversed duplicate, and containment-cycle attempts fail without state change.

### Slice 2 — movement and current location

Author the movement procedure/mechanic using existing containment.move. It validates actor,
current/destination locations, known adjacency when required, and forbidden transitions. It emits
the existing structural event and no duplicated location state.

**Acceptance:** one move changes containment exactly once; invalid/disconnected moves, wrong actor,
missing location state, guard denial, and replay boundaries are verified.

### Slice 3 — factions and motives

Add world.faction plus faction relationship conventions and one agenda-advance mechanic. Add the
ratified world-owned recurring-NPC motive contract rather than storing motives inside faction JSON
or relying on a campaign-only model.

**Acceptance:** one NPC joins a faction, one faction relates to a location/rival, and agenda
advancement is atomic/auditable; duplicate and contradictory relationship cases are explicit.

### Slice 4 — knowledge, rumours, secrets, and clues

Add the knowledge components, source/visibility semantics, relationships, and reveal/confirm
mechanics. Record the query contract needed by storytelling/session work. Publish or revise the
storytelling procedure only when the campaign chapter identifiers it also references are ratified.

**Acceptance:** a fresh GM sees truth plus visibility; a party projection sees only allowed facts;
revealing a clue does not rewrite the hidden truth; invalid provenance/target fails unchanged.

### Slice 5 — explicit world time

Add world.clock and its advance mechanic. Define time input units, monotonicity, calendar ownership,
maximum advance, and event payload. Do not add scheduling, durations, or travel-time formulas yet.

**Acceptance:** time advances deterministically, emits one registered event, replays from recorded
input, and rejects reversal/overflow/corrupt state atomically.

### Slice 6 — reactive world changes

Add one bounded event/subscription example, such as a faction agenda reacting to an accepted quest
milestone or a clue becoming available after entering a location. Use registered event types and
existing chain limits.

**Acceptance:** matching committed event changes the intended world record once; nonmatching,
repeated, rolled-back, and chain-limit cases change nothing.

### Slice 7 — read projections and map preparation

Add bounded world/location/faction/knowledge projections for MCP and the website. Map metadata
remains optional display data. Coordinates, paths, terrain, distance, and line of sight require a
separate spatial/travel plan.

**Acceptance:** server-rendered world/location views can show hierarchy and adjacency without
recursive expansion or revealing hidden facts to an enforced player audience.

## Acceptance matrix

- exact readback of root, hierarchy, adjacency, factions, knowledge, and clock;
- closed status/kind/visibility input and missing/null/empty semantics;
- containment cycles, invalid relationship endpoints, reverse duplicates, and dangling links;
- movement happy path, disconnected path, guard denial, rollback, and structural event evidence;
- faction membership/agenda state and conflicting links;
- fact/rumour/secret/clue provenance and visibility projection;
- clock boundaries, invalid reversal, deterministic event, and corrupt state;
- catalog import/export and source-hash/version preservation;
- fresh-session reconstruction with no chat history;
- exact fixture cleanup/restoration and full repository checks.

## Non-goals

No geographic simulator, coordinate grid, route finding, weather, economy, autonomous factions,
background scheduler, fog of war, dynamic lighting, procedural world generator, or player
authorization is included. These require separate evidence and plans.

## Dependencies and handoff

Entity/component/containment/relationship persistence, effects, mechanics, events, and audit are
existing foundations. Campaign, quest, item, character, website, and travel plans consume this
world model.

Each implementation handoff names exactly one slice and fills
SUBSYSTEM_IMPLEMENTATION_HANDOFF.md. Terra High is required for Slice 0 ownership decisions; a
lower model may implement a later ratified mechanical slice only when artifact IDs, schemas,
expected tests, and cleanup are fully explicit.

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
containment, travel links, factions, NPC motives, facts, rumours, secrets, and an explicit world
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
- three clues linking visible discoveries to a hidden truth;
- one current campaign time value;
- safe movement, clue-reveal, rumour-confirm, and clock-advance mechanics.

This is enough for campaign, quest, and session plans to reference a real setting without building
a full map or simulation.

## Ownership decisions

### World root

Proposed component: game.core.world.root.

It owns lifecycle status, a short premise, and descriptive visibility. The entity name is the
world name. Calendar/time convention, creation source, default policy, campaign links, and lists
of locations, factions, characters, or facts are absent; later features must own them explicitly.

### Locations

Proposed component: game.core.world.location.

It owns display summary, location kind, status, and descriptive visibility. Map/display metadata
is not yet stored. Physical membership uses containment: a room belongs to a building, a building
to a settlement, and actors/items are contained at their current location.

Travel adjacency is a relationship, not containment. A closed relationship convention records
origin/destination, directionality, and stable travel-edge identity. Distance, terrain, time, and
risk remain absent until the travel feature owns them.

### Factions

Proposed component: game.core.world.faction.

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

- game.core.world.fact for an asserted persistent fact;
- game.core.world.rumour for a claim whose truth is uncertain to the audience;
- game.core.world.secret for GM-only authoritative truth;
- game.core.world.clue for discoverable evidence pointing to a fact/secret/entity.

Every knowledge record identifies source/provenance, visibility, certainty semantics, summary, and
relevant entity relationships. A rumour becoming confirmed is an explicit state transition, not
the GM silently treating prose as fact.

Initial trusted MCP use is GM scope. Visibility metadata is descriptive until an authenticated
audience policy enforces it.

### Time

Proposed component: game.core.world.clock.

It owns one explicit current campaign time value, calendar identity, and revision. Time advances
only through a named mechanic/effect-producing action; the action and structural-event ledger,
not a copied operation ID field, records last-advance evidence. There is no background scheduler
and no dependence on wall-clock time.

## Proposed relationships

- generic containment only for world/region/location hierarchy;
- game.core.world.location.connected-to for travel adjacency;
- game.core.world.faction.member for faction-to-character membership;
- game.core.world.faction.controls for faction-to-location/item relationship;
- game.core.world.faction.allied-with and game.core.world.faction.opposed-to;
- game.core.world.knowledge.in-world for a knowledge record's one world-root scope;
- game.core.world.knowledge.about for fact/rumour/secret/clue targets;
- game.core.world.clue.supports for clue-to-fact/secret relationships;
- character.located-at through containment, not a duplicated location-id component.

Exact relation IDs and directionality must be ratified before Slice 1. Symmetric relationships need
one canonical storage/order rule so duplicates cannot be created in reverse order.

## Mechanics and procedures

Proposed versioned mechanics:

- mechanic.game.core.world.location.move validates allowed containment/travel transition and proposes
  containment.move;
- mechanic.game.core.world.clue.reveal reveals one clue without changing its supported truth;
- mechanic.game.core.world.rumour.confirm makes one rumour's resolution explicit without copying a fact;
- mechanic.game.core.world.faction.agenda advances one explicit agenda state;
- mechanic.game.core.world.clock.advance advances time deterministically from validated input;
- mechanic.game.core.world.opportunity.evaluate is deferred to campaign/quest opportunity planning.

Required procedures:

- procedure.game.core.world.create
- procedure.game.core.world.location
- procedure.game.core.world.travel
- procedure.game.core.world.knowledge
- procedure.game.core.world.faction
- procedure.game.core.world.time
- procedure.game.core.world.inspect

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
[World Feature 1](world/feature-01/WORLD-FEATURE-01-DEPENDENCY-PLAN.md). Slice 1 is verified;
[its receipt](world/feature-01/WORLD-FEATURE-01-RECEIPT.md) records the evidence. Movement and all
later world/lore work still require their own reviewed plan.

Add game.core.world.root and game.core.world.location contracts/definitions plus their safe recording path. Create the
fixture world root, region, and three locations through catalog-owned records or governed effects.
Add containment hierarchy and location adjacency.

**Acceptance:** query reconstructs the hierarchy and connections; invalid parent, self-link,
duplicate/reversed duplicate, and containment-cycle attempts fail without state change.

### Slice 2 — movement and current location

The full dependency and ownership plan is in [World Feature 2](world/feature-02/WORLD-FEATURE-02-DEPENDENCY-PLAN.md).
Its generic declared relationship-projection prerequisite and one-hop movement are verified; see
the [Slice 2 receipt](world/feature-02/WORLD-FEATURE-02-SLICE-2-RECEIPT.md). The travel
procedure/mechanic uses existing `containment.move`, validates actor, current/destination
locations and known adjacency, emits the existing structural event, and duplicates no location
state.

**Acceptance:** one move changes containment exactly once; invalid/disconnected moves, wrong actor,
missing location state, guard denial, and replay boundaries are verified.

### Slice 3 — factions and motives

`game.core.world.faction`, `game.core.world.motive`, their relationship conventions, the small
recurring-actor fixture, and the one-time agenda action are verified in the
[Slice 2 receipt](world/feature-03/WORLD-FEATURE-03-SLICE-2-RECEIPT.md). Motives remain world-owned
rather than being stored in faction JSON or a campaign-only model.

Implementation dependency and confirmation boundary: [World Feature 3 dependency plan](world/feature-03/WORLD-FEATURE-03-DEPENDENCY-PLAN.md).

**Acceptance:** one NPC joins a faction, one faction relates to a location, duplicate and
contradictory relationship cases are explicit, and one agenda transition is atomic/auditable.

### Slice 4 — knowledge, rumours, secrets, and clues

The full dependency and confirmation plan is in [World Feature 4](world/feature-04/WORLD-FEATURE-04-DEPENDENCY-PLAN.md).
Its scoped knowledge foundation and reveal/confirmation actions are verified in the [Slice 2 receipt](world/feature-04/WORLD-FEATURE-04-SLICE-2-RECEIPT.md).
Add the knowledge components, source/visibility semantics, relationships, and reveal/confirm
mechanics. Record only the trusted-GM query boundary needed by later storytelling/session work;
visibility remains descriptive until a separate authorized audience projection exists. Publish or
revise the storytelling procedure only when the campaign chapter identifiers it also references are
ratified.

**Acceptance:** a fresh trusted GM sees truth plus visibility labels; revealing a clue does not
rewrite the hidden truth; rumour confirmation is explicit; invalid provenance/target fails
unchanged. Party-safe projection is later authorization work.

### Slice 5 — explicit world time

The full dependency and confirmation plan is in [World Feature 5](world/feature-05/WORLD-FEATURE-05-DEPENDENCY-PLAN.md).
Add game.core.world.clock and its advance mechanic. Define time input units, monotonicity, calendar ownership,
maximum advance, and existing structural-event/audit evidence. Do not add scheduling, durations,
or travel-time formulas yet.

**Acceptance:** time advances deterministically, produces one existing structural replacement
event plus action audit, replays from recorded input, and rejects reversal/overflow/corrupt state
atomically.

### Slice 6 — reactive world changes

The full dependency and confirmation plan is in [World Feature 6](world/feature-06/WORLD-FEATURE-06-DEPENDENCY-PLAN.md).
Add one bounded event/subscription example: the accepted Feature 3 fixture faction agenda advance
reveals one fixed Feature 4 fixture clue. Reuse `world.component.replaced` and existing chain
limits; do not add quest dependencies, new event types, or autonomous processing. Its first
delivery is the generic fresh-catalog fixture-binding import gate, which must pass before the
reaction subscription is exercised. The gate and bounded reaction are implemented and verified in
the [Feature 6 receipt](world/feature-06/WORLD-FEATURE-06-IMPLEMENTATION-RECEIPT.md).

**Acceptance:** the matching committed `ready → advanced` agenda event reveals only the designated
clue once; nonmatching, repeated, rolled-back, invalid, and chain-limit cases create no partial or
duplicate world change.

### Slice 7 — read projections and map preparation

The full dependency and public-surface plan is in [World Feature 7](world/feature-07/WORLD-FEATURE-07-DEPENDENCY-PLAN.md).
[Feature 7 implementation](world/feature-07/WORLD-FEATURE-07-IMPLEMENTATION-RECEIPT.md) has
verified the generic bounded graph query and the trusted-GM world/location/faction/knowledge
recipes. The
recipes expose existing topology as map preparation only; coordinates, paths, terrain, distance,
line of sight, and rendering remain separate spatial/travel work.

**Acceptance:** discoverable, read-only, bounded projections show the stated hierarchy and
adjacency without recursive expansion, silently dropped records, world-specific C# branches, or a
claim of enforced player visibility.

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

## Post-foundation feature roadmap

These are separately planned features, not extensions automatically authorised by Slices 1–7.
They start only after the relevant foundation has played evidence. Each feature must first receive
its own dependency plan, ownership search, ratified permanent vocabulary, and one-slice handoff.

| Feature | Product result | Prerequisites | First bounded delivery | Exit gate |
| --- | --- | --- | --- | --- |
| W8 — routes and travel modes | A marked traveller can take a named one-way route between connected locations with declared on-foot mode and deterministic time cost. | W1, W2, and verified W5; [World Feature 8 dependency plan](world/feature-08/WORLD-FEATURE-08-DEPENDENCY-PLAN.md) and [implementation receipt](world/feature-08/WORLD-FEATURE-08-IMPLEMENTATION-RECEIPT.md) | Add a route entity, scope/origin/destination links, and one on-foot journey mechanic; it consumes adjacency and the root clock. | A valid journey changes location and time atomically; unknown, reversed, unavailable, malformed, or invalid traveller/clock state leaves both unchanged. **Verified.** |
| W9 — spatial/map projection | A trusted GM can obtain a useful display layout for one region without making a map the source of truth. | Verified W7 and W8; [World Feature 9 dependency plan](world/feature-09/WORLD-FEATURE-09-DEPENDENCY-PLAN.md) and [implementation receipt](world/feature-09/WORLD-FEATURE-09-IMPLEMENTATION-RECEIPT.md) | Add authored normalized anchors to the first region's direct locations and a bounded read-only map-layout recipe. | The layout agrees with containment, adjacency, and route records; malformed anchors or links fail without altering topology. Player views wait for audience enforcement. **Verified.** |
| W10 — world conditions | One scheduled route closure temporarily changes a named route's explicit availability. | Verified W5, W6, and an isolated disposable W8 journey; [World Feature 10 dependency plan](world/feature-10/WORLD-FEATURE-10-DEPENDENCY-PLAN.md), [Slice 1 receipt](world/feature-10/WORLD-FEATURE-10-SLICE-1-RECEIPT.md), and [implementation receipt](world/feature-10/WORLD-FEATURE-10-IMPLEMENTATION-RECEIPT.md) | A fixed root-clock reaction reconciles the confirmed condition/route-availability pair between scheduled, active, and expired state. | The closure has source, route scope, start/end evidence, and atomic clock-driven route denial/reopening; no scheduler changes state. **Verified.** |
| W11 — faction fronts and territory | A world-scoped faction can press one contested location through a manual front, while exclusive territorial control is explicit. | Verified W3, W5, W6, and confirmed vocabulary; [World Feature 11 dependency plan](world/feature-11/WORLD-FEATURE-11-DEPENDENCY-PLAN.md), [Slice 1 receipt](world/feature-11/WORLD-FEATURE-11-SLICE-1-RECEIPT.md), and [implementation receipt](world/feature-11/WORLD-FEATURE-11-IMPLEMENTATION-RECEIPT.md) | One scoped front advances from an expected phase with current clock evidence; exclusive territorial control stays separate from general faction claims. | Scope/control conflicts reject; an allowed advance replaces only the front with current clock evidence; stale/terminal calls create no progress. **Verified.** |
| W12 — generic ground conveyance | One driver and an active ground conveyance travel together over a dedicated ground route with deterministic vehicle-derived time. | Verified W5 and W8; [World Feature 12 revision](world/feature-12/WORLD-FEATURE-12-GROUND-CONVEYANCE-PLAN.md), [Slice 1 receipt](world/feature-12/WORLD-FEATURE-12-SLICE-1-RECEIPT.md), and [implementation receipt](world/feature-12/WORLD-FEATURE-12-IMPLEMENTATION-RECEIPT.md) | Slice 1 adds generic ground state/distance. Slice 2 adds a bounded journey mechanism that moves driver and conveyance. | A valid journey moves driver, conveyance, and root clock atomically; time derives from distance/speed, while invalid route, co-location, mode, or clock state changes none. **Verified.** |
| W13 — generic aerial conveyance | One rider and aerial conveyance travel together over an explicit aerial route independent of ground adjacency. | Verified W5 and W12; [World Feature 13 dependency plan](world/feature-13/WORLD-FEATURE-13-DEPENDENCY-PLAN.md), [Slice 1 receipt](world/feature-13/WORLD-FEATURE-13-SLICE-1-RECEIPT.md), and [implementation receipt](world/feature-13/WORLD-FEATURE-13-IMPLEMENTATION-RECEIPT.md) | Add one generic aerial-conveyance entity, one aerial route, and a bounded co-travel journey mechanism; a dragon is only the first fixture. | A valid journey moves rider, conveyance, and root clock atomically; ground roads, routes, and adjacency neither grant nor deny flight. **Verified.** |
| W14 — distant on-foot itinerary | A trusted GM can request a bounded multi-leg on-foot plan to a stored destination, then execute one rechecked leg at a time. | Verified W7, W8, and W10; [World Feature 14 dependency plan](world/feature-14/WORLD-FEATURE-14-DEPENDENCY-PLAN.md), [Slice 1 receipt](world/feature-14/WORLD-FEATURE-14-SLICE-1-RECEIPT.md), and [implementation receipt](world/feature-14/WORLD-FEATURE-14-IMPLEMENTATION-RECEIPT.md) | Add a read-only itinerary query over active/open on-foot routes; it never batches movement or stores a journey. | Every leg remains separately audited and re-planned from actual containment; a closure or later blocker stops the next leg without skipped locations or time. **Verified.** |
| W15 — fixed teleport portals | A traveller can cross one explicit portal from its contained origin to its exact destination instantly. | Verified W2 and W5; [World Feature 15 dependency plan](world/feature-15/WORLD-FEATURE-15-DEPENDENCY-PLAN.md) confirmation gate | Add one fixed portal entity with world/destination links and a one-effect relocation action. | Only the traveller moves; no clock, route, intermediate-location, ration, or roadside-encounter state changes. |
| W16 — mode-aware distant itinerary | A trusted host can request a bounded far-destination plan using only the traveller's explicitly available on-foot, ground, air, and fixed-portal legs. | Verified W8, W12, W13, and W15; [World Feature 16 dependency plan](world/feature-16/WORLD-FEATURE-16-MODE-AWARE-ITINERARY-PLAN.md) confirmation gate | Add a read-only mixed-mode itinerary query and a one-leg coordinator that re-plans after every mode action. | Every intermediate leg is still individually validated and auditable; later rations, encounters, closures, or unavailable conveyances can block the next leg at the actual reached location. |

### Recommended order

Prioritize W8 first if travel is needed for the next playtest; otherwise W11 is the stronger
story-first follow-on. W9 is a consumer of established topology/travel, and W10 should follow one
real travel scenario. Catalog package import/export remains owned by
CATALOG_PORTABILITY_PLAN.md rather than this roadmap.

W12 follows a verified on-foot journey with a deliberately separate generic ground-conveyance
route. W13 then establishes generic aerial topology as its own authority: roads, ground routes,
and adjacency do not automatically authorize aerial travel. The dragon is a first fixture, not the
only supported conveyance.

W14 makes a far destination usable without bypassing journey rules: it proposes on-foot legs and
requires a fresh plan after each accepted leg. Travel supplies and encounters remain separate
follow-on features that must explicitly block a future leg.

W15 is intentionally not a journey: a fixed portal is an explicit instant-relocation boundary.
W16 can select that fixed portal as one leg in a long journey while leaving spell/item
teleportation and portal networks to later character/item and routing plans.

### Future-boundary rules

- Route distance, terrain, mode restrictions, and travel cost belong to W8; containment remains
  the only parent/location hierarchy and campaigns do not copy route state.
- W9 may cache or render topology, but map geometry never overrides containment, adjacency, or
  route records. Player discovery is only a projection once audience enforcement exists.
- W10 and W11 record current authoritative state in world-owned components. Events and operations
  remain historical evidence; no scheduler, polling process, or model narration may advance them.

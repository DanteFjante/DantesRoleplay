# Campaign creation plan

Status: **Draft — design plan only; no generated campaign content is authorised by this document**  
Last updated: 2026-08-20

## Execution rule

Use [GAME_SYSTEM_MASTER_PLAN.md](GAME_SYSTEM_MASTER_PLAN.md) for cross-subsystem ownership,
[TERRA-FEATURE-PLANNING-GUIDE.md](ruleset/dnd2024/TERRA-FEATURE-PLANNING-GUIDE.md) for plan quality,
and a populated [SUBSYSTEM_IMPLEMENTATION_HANDOFF.md](SUBSYSTEM_IMPLEMENTATION_HANDOFF.md) for the active assignment. Implement one reviewed delivery
slice, meet its exit gate, record evidence, and stop.

## Goal

Create a persistent campaign from either a human-filled brief or an AI-generated proposal. The
result is a structured campaign world: goals, setting, chapters, story arcs, quests and future
quest hooks, factions, characters, locations, items, knowledge, and event-driven opportunities.

Creation must be reviewable and atomic. An AI may propose a campaign blueprint, but it may not
silently create a world, activate a quest, invent authoritative facts during play, or write raw
effects. The same final validated blueprint powers MCP creation now and a future website wizard.

## Campaign model

A campaign is an application/ruleset feature composed from existing generic entities, components,
relationships, mechanics, effects, events, and audit records. The C# kernel does not gain
campaign-specific tables, effects, or special-case game vocabulary.

On the story-first existing-world path, one campaign has:

- a campaign root: identity, premise, genre/tone, goals, ruleset scope, lifecycle status, and world
  root reference;
- a reference to a world root owned by [WORLD_AND_LORE_PLAN.md](WORLD_AND_LORE_PLAN.md); the
  campaign does not copy its setting, calendar, locations, factions, or knowledge state;
- chapters: dramatic questions, chapter status, summaries, entry/exit conditions, and planned
  themes—not rigid scene scripts;
- story arcs: long-running stakes/questions that may cross several chapters;
- optional quest links, added only after the quest plan's manual lifecycle exists;
- references to world entities and knowledge owned by their respective plans;
- event/subscription registrations that activate approved future opportunities or react to
  meaningful committed changes.

A campaign may start in one SQLite file. Its campaign ID/scoped identifiers must still be explicit
so later multi-campaign creation, catalog export, and shared-ruleset lookup are not redesigned.

## Input modes

### Manual brief

A host or player fills a structured brief. Minimum inputs are campaign title, premise, one or more
goals, intended tone, initial setting, desired starting situation, and the audience/visibility
assumption. Optional inputs include selected ruleset scope, named characters, locations, factions,
quests, chapter ideas, safety/tone boundaries, and a desired campaign length.

### AI-assisted proposal

A stronger story model may turn a short prompt into a CampaignBlueprint proposal. It is a
suggestion, not a write. The model must return schema-bound data with source/assumption markers,
open questions, and a compact human-readable summary. It does not receive direct database,
filesystem, network, or MCP write access.

A local Ollama operator may normalise a brief, find existing campaign templates, or validate schema
shape, but does not generate final campaign fiction unless an explicit model profile permits it.

### Existing-world attachment

If the caller supplies an existing world root, the blueprint references it after compatibility and
scope checks. It must not overwrite existing entities. The plan may add new entities/links only
through explicit reviewed additions.

## Blueprint and creation boundary

CampaignBlueprint is the single input contract. It contains stable IDs or temporary local keys,
campaign-owned component data, entity names, relationships, visibility, and chapter/arc links.
Quest links and future quest opportunities become optional extensions only after their owning
quest slices exist. It never contains SQL, JavaScript, arbitrary event filters, opaque effects, or
a raw transcript.

The creation surface is one semantic operation, tentatively:

    commit(kind: "campaign", operation: "validate" | "create", payload: CampaignBlueprint)

- validate performs schema, ID, reference, scope, visibility, ruleset, relationship, and
  opportunity checks without writes. It returns named checks, resolved IDs, creation counts,
  warnings, and all blocking problems.
- create accepts the validated blueprint or its immutable review fingerprint, creates the campaign
  as one root transaction, records a campaign-creation operation, and returns the root entity plus
  initial playable summary.
- correction, import, clone, and archive are later operations with separate contracts.

The implementation lives outside the package-free kernel and uses semantic internal command
services or a registered campaign-bootstrap workflow. It must not call MCP transport handlers or
make a caller submit a list of arbitrary world effects.

## Core campaign data

### Campaign root and world reference

The campaign component records title, premise, goals, tone, status, ruleset scope, world root ID,
creation method, blueprint/review fingerprint, and creation operation ID. Goals distinguish player
visible goals from GM-only campaign intentions.

The referenced world root records setting/name, time/calendar convention, and world-knowledge
policy under the world/lore plan. Campaign creation validates its identity, scope, lifecycle, and
compatibility but does not rewrite it. A later new-world creation mode composes the world plan's
governed operation; it never defines a second world-root contract.

### Chapters and arcs

A chapter entity has a question, status, summary, planned themes, start/end evidence, and visibility.
An arc entity describes a longer question/stake and may relate to many chapters and quests.

Relationships keep ownership and cross-chapter planning inspectable:

- campaign.has-chapter;
- campaign.has-arc;
- chapter.part-of-arc;
- quest.part-of-arc;
- quest.appears-in-chapter;
- chapter.follows-chapter, only for intended ordering, never as a forced narrative rail.

A quest may span multiple chapters through its arc/chapter links. Chapter completion does not
automatically end a quest unless an explicit quest rule says it does.

### World knowledge, factions, and world entities

Knowledge is represented by the world/lore plan's entities/components with source, visibility,
certainty, and summary. Campaign projections reference those records so the narrative layer can
show only what the current audience knows.

A faction is a world/lore-owned entity. Relationships connect factions to NPCs, locations, quests,
arcs, and rivals. The campaign may add approved links, but it does not define or copy faction
goals, agenda, visibility, or status.

NPCs, player characters, locations, items, clues, and creatures use their appropriate existing or
ruleset components. Campaign creation creates only the small starting set required by the approved
blueprint; it does not bulk-generate a whole encyclopedia.

## Future quests and controlled randomness

A planned future quest is a dormant quest opportunity, not a promise that an LLM will remember later.

A quest opportunity stores:

- its linked dormant quest/arc/chapter context;
- state: dormant, eligible, offered, activated, expired, or archived;
- intended audience/visibility;
- activation mode: manual, registered event, or deterministic opportunity roll;
- optional weight, one-time flag, cooldown/expiry, and source/creation evidence.

Eligibility stays closed and inspectable. For event activation, an approved subscription listens to
a registered event and a quest mechanic decides whether the frozen event satisfies the opportunity.
For random activation, a named campaign clock/opportunity event invokes a registered mechanic using
the engine's seeded random source. It records the roll, candidate set, selected opportunity, and
reason; it never uses Math.random or hidden prompt choice.

Only eligible opportunities can be selected. The mechanic returns no activation effects when the
candidate set is empty. Repeated events, a guard denial, or transaction failure cannot offer the
same one-time quest twice.

## Campaign lifecycle mechanics and procedures

Use versioned mechanics/workflows and procedure contracts, not an opaque campaign service that
invented campaign state:

- campaign.bootstrap creates the validated entity/component/relationship graph;
- campaign.chapter.activate and campaign.chapter.close own chapter transitions and summaries;
- campaign.arc.reconcile evaluates long-running arc state from explicit quest/chapter evidence;
- campaign.opportunity.evaluate and campaign.opportunity.select handle approved future-quest
  eligibility and deterministic selection;
- campaign.session.resume provides a bounded current-campaign digest for a fresh host;
- existing quest mechanics own quest/objective lifecycle.

Required governing procedures include procedure.campaign.create,
procedure.campaign.modify, procedure.campaign.inspect, procedure.campaign.chapter,
procedure.campaign.opportunity, procedure.campaign.session, and the existing quest procedures.
Each names audience/visibility, input shape, transaction/event behaviour, sources, test fixtures,
and recovery calls.

## Delivery slices

### Slice 0 — ratify one first campaign blueprint

Choose one short campaign attached to the World/Lore fixture (one premise, 1–3 goals, one starting
location reference, 2–3 NPC references, one faction stake, one active chapter, and one arc). Define
exactly which campaign fields are party-visible or GM-only. Record one future quest-shaped problem
as planning prose only; it is not quest state and receives no quest ID yet.

**Acceptance:** the complete first campaign can be drawn as entities, components, relationships,
and approved mechanics with no undefined “the AI will remember it” field.

### Slice 1 — campaign root, existing-world reference, and manual validation

Define the CampaignBlueprint schema, campaign-owned component/relationship vocabulary, scoped-ID
convention, existing-world resolution, validation checks, creation counts, review fingerprint, and
catalogue/import format. Add only the manual validate path. A new-world creation mode is deferred.

**Acceptance:** an invalid reference, duplicate ID, incompatible existing-world attachment, or
visibility violation is rejected before any write; a valid brief produces a deterministic creation
preview.

### Slice 2 — atomic campaign-root bootstrap

Implement the semantic campaign create runner or registered bootstrap workflow. It resolves the
approved existing world, creates only the campaign root and campaign-owned starting relationships,
writes one root audit record, and integrates structural event processing inside its transaction.

**Acceptance:** creation succeeds as one coherent root result; a failure at any creation step rolls
back every new entity, component, relationship, event, notification, and success audit record.

### Slice 3 — chapters, arcs, and quest-free resume

Add campaign-owned chapter/arc records and relationships. Implement activate/close/reconcile
mechanics and a compact resume digest that identifies the active chapter, arc stakes, unresolved
world clues, relevant factions, and recent milestones. Quest links are optional and absent from the
first acceptance fixture.

**Acceptance:** a fresh host reconstructs the current campaign state without any quest record, and
closing one chapter does not silently resolve an unrelated arc or alter world-owned knowledge.

### Slice 4 — manual quest integration

After [QUEST_IMPLEMENTATION_PLAN.md](QUEST_IMPLEMENTATION_PLAN.md) Slices 0–3, add relationships
from existing quests to arcs/chapters and include active quest/objective summaries in the campaign
resume digest. Campaign code never changes quest/objective lifecycle directly.

**Acceptance:** a quest can span two planned chapters, a fresh host reconstructs its campaign and
quest links, and closing a chapter does not silently complete/fail a quest or unrelated arc.

### Slice 5 — world knowledge, factions, and visibility projections

Consume the world/lore plan's fact/rumour/secret/clue and faction components. Add only bounded
campaign relationship inspection and player/GM projection contracts. Do not claim data secrecy
until caller authorisation exists; initial trusted MCP use is explicitly GM scope.

**Acceptance:** the campaign preserves facts and faction agendas across sessions, and the
player-facing projection excludes marked hidden facts when a real audience policy is enabled.

### Slice 6 — future quest opportunities and event activation

Implement dormant opportunity state, registered subscriptions, deterministic chance selection,
cooldown/one-time enforcement, quest activation effects, and notification/history evidence. Start
with one event-based opportunity before adding a clock/random opportunity.

**Acceptance:** a matching committed event activates one intended future quest exactly once; all
nonmatching, rejected, repeated, and rolled-back cases leave it dormant.

### Slice 7 — AI-assisted proposal with review

Add the schema-constrained campaign-proposal adapter, deterministic template retrieval, explicit
assumptions/open questions, preview/diff, and human approval gate. The proposal is always followed
by normal validate/create; no model response becomes state directly.

**Acceptance:** manual and AI-assisted paths produce the same validated blueprint shape; malformed
or unapproved proposals make no state change.

### Slice 8 — session operations and read-only campaign view

Add start/resume/end-session mechanics, summary updates, snapshot guidance, and the read-only
campaign/world website pages. Use SSE only to refresh committed projections. The UI never creates
or advances campaign state in this release.

This initial slice is S0–S4 of the [Session Operations Plan](SESSION_OPERATIONS_PLAN.md); C8 owns
the concrete session-record contract. Later participant control, gameplay handoff, narrative
artifacts, player controls, and collaboration stay with their named session successors.

**Acceptance:** a fresh model and a human reader can see the current chapter, active quests,
relevant world facts, and recent milestones without reading previous chat history.

### Slice 9 — controlled expansion

Only after a played campaign, add one expansion at a time: campaign templates/cloning, time/clock
events, quest rewards, faction clocks, multiple campaign worlds, a website creation wizard, or
interactive map integration.

**Acceptance:** every addition retains explicit ownership, visible validation, deterministic
event/randomness evidence, and full rollback semantics.

## Campaign feature dependency-plan index

The slices above remain the authoritative campaign delivery sequence. The companion plans below
make each stop point, ownership boundary, dependency, test matrix, and exit gate explicit for a
future implementation pass. They are planning artifacts only and do not author campaign runtime
content.

| Slice/feature | Dependency plan | Current boundary |
| --- | --- | --- |
| C0 | [Ratify first existing-world blueprint](campaign/feature-00/CAMPAIGN-FEATURE-00-DEPENDENCY-PLAN.md) | Host-confirmed brief only; no runtime IDs or records. |
| C1 | [Validate existing-world blueprint](campaign/feature-01/CAMPAIGN-FEATURE-01-DEPENDENCY-PLAN.md) | Read-only validation/fingerprint after C0. |
| C2 | [Atomic existing-world bootstrap](campaign/feature-02/CAMPAIGN-FEATURE-02-DEPENDENCY-PLAN.md) | One campaign root references an existing world atomically. |
| C3 | [Chapters, arcs, and quest-free resume](campaign/feature-03/CAMPAIGN-FEATURE-03-DEPENDENCY-PLAN.md) | Campaign continuity without a quest dependency. |
| C4 | [Manual quest integration](campaign/feature-04/CAMPAIGN-FEATURE-04-DEPENDENCY-PLAN.md) | Campaign consumes quest context without owning quest lifecycle. |
| C5 | [Knowledge, factions, and audience projections](campaign/feature-05/CAMPAIGN-FEATURE-05-DEPENDENCY-PLAN.md) | Blocked until real audience authorization exists. |
| C6 | [Future quest opportunities](campaign/feature-06/CAMPAIGN-FEATURE-06-DEPENDENCY-PLAN.md) | One event-triggered activation; no random/clock opportunity yet. |
| C7 | [AI-assisted proposal with review](campaign/feature-07/CAMPAIGN-FEATURE-07-DEPENDENCY-PLAN.md) | Proposal remains untrusted until host approval and C1 validation. |
| C8 | [Session operations and read-only view](campaign/feature-08/CAMPAIGN-FEATURE-08-DEPENDENCY-PLAN.md) | S0–S4 of the [Session Operations Plan](SESSION_OPERATIONS_PLAN.md): stored continuity, factual recap, checkpoint boundary, and read-only consumer. |
| C9 | [Controlled expansion selection](campaign/feature-09/CAMPAIGN-FEATURE-09-DEPENDENCY-PLAN.md) | Select one post-play expansion and plan it separately. |
| C10 | [Compose a new world](campaign/feature-10/CAMPAIGN-FEATURE-10-DEPENDENCY-PLAN.md) | Blocked by a world-owned composer and cross-root transaction decision. |
| C11 | [Campaign fork preview](campaign/feature-11/CAMPAIGN-FEATURE-11-DEPENDENCY-PLAN.md) | Read-only fork feasibility after checkpoint/snapshot evidence. |
| C12 | [Parallel and branching arc continuity](campaign/feature-12/CAMPAIGN-FEATURE-12-PARALLEL-ARC-CONTINUITY-PLAN.md) | Explicit successor to C3's one-arc proof; blocked by C3 evidence and cardinality/migration confirmation. |
| C13 | [Deterministic opportunity pool and campaign-time selection](campaign/feature-13/CAMPAIGN-FEATURE-13-DETERMINISTIC-OPPORTUNITY-POOL-PLAN.md) | Explicit successor to C6's one fixed event opportunity; blocked by C6/Q2/clock/random evidence and state-migration confirmation. |
| C14 | [Advancement policy and authorization](campaign/feature-14/CAMPAIGN-FEATURE-14-ADVANCEMENT-AUTHORIZATION-PLAN.md) | Campaign-owned XP/milestone policy and one-time authorization for Character CH9; blocked on active character attachment and Feature 36 XP eligibility. |
| C15 | [Campaign-owned character participation](campaign/feature-15/CAMPAIGN-FEATURE-15-CHARACTER-PARTICIPATION-PLAN.md) | Slices 1–2 verify active scope and atomic attachment; withdrawal remains gated on CH13 composition. |

## Test matrix

- manual and generated blueprint schema/validation, stable review fingerprints, and catalog
  round-trip;
- ID/local-key resolution, scope isolation, existing-world attachment, and duplicate prevention;
- root creation transaction rollback at each entity/component/relationship/event/audit failure;
- chapter/arc/quest cross-linking, cross-chapter persistence, and bounded graph inspection;
- knowledge/faction visibility projections and no accidental GM-only leakage once authorised views
  exist;
- opportunity eligibility, matching/nonmatching/repeated/rolled-back events, cooldown, deterministic
  seeded selection, and one-time activation;
- AI proposal schema failures, content limits, review rejection, and no direct model writes;
- fresh-session campaign resume, snapshot/restore, and event/audit traceability;
- browser rendering and SSE freshness after the read-only website work exists.

## Non-goals

This plan does not add a campaign-specific kernel, autonomous world generation, unrestricted
AI-created mechanics, raw procedural generation, a global scheduler, player authentication,
multiplayer collaboration, a full world editor, an interactive map, or forced plot outcomes.
Campaign planning creates opportunities and consistent facts; players still choose what happens.

## Dependencies

The entity-component/effect/mechanic/audit foundations and the completed event/subscription runtime
are required. World roots, locations, factions, and knowledge follow
WORLD_AND_LORE_PLAN.md. Quest lifecycle and objective data follow QUEST_IMPLEMENTATION_PLAN.md;
Campaign Slice 4 cannot start until Quest Slices 0–3 are complete. The
executable-workflow plan is the preferred orchestration path for larger bootstrap work, but a
dedicated semantic campaign-create runner may establish the first vertical slice earlier if it
preserves the same transaction and audit guarantees. The website, local-model, and semantic
retrieval plans are optional consumers of the campaign model, not prerequisites for manual
creation.

## Post-foundation feature roadmap

These features refine how campaigns are assembled and sustained after the manual existing-world
path has completed a played campaign. They do not turn campaign creation into a second owner of
world, character, quest, item, or ruleset state. Each one needs a separate dependency plan and a
confirmed creation/transaction boundary before implementation.

| Feature | Product result | Prerequisites | First bounded delivery | Exit gate |
| --- | --- | --- | --- | --- |
| C10 — compose a new world | A campaign brief can create and attach one new small world through the world owner's governed operation. | Campaign Slice 2; World Slices 1 and 4; played existing-world evidence; ratified cross-root transaction design; [Campaign Feature 10 dependency plan](campaign/feature-10/CAMPAIGN-FEATURE-10-DEPENDENCY-PLAN.md) confirmation gate | Support a fixed small-world blueprint with no generated content and an explicit review preview. | Success creates the approved world and campaign as the ratified unit of atomicity; any failure leaves neither partially created. |
| C11 — campaign-fork design | A host can preview a campaign fork from a named audited checkpoint with explicit inclusion and provenance rules. | Slice 8 and snapshot/restore evidence; [Campaign Feature 11 dependency plan](campaign/feature-11/CAMPAIGN-FEATURE-11-DEPENDENCY-PLAN.md) confirmation gate | Add one read-only checkpoint/fork preview. | The preview classifies each state domain as reference, copy, or unsupported; it writes no campaign state. |
| C12 — parallel and branching arc continuity | A campaign can sustain several active arcs and explicitly branch a chapter while retaining C3 lifecycle ownership. | Verified and played C3; C4 compatibility inspection; [Campaign Feature 12 dependency plan](campaign/feature-12/CAMPAIGN-FEATURE-12-PARALLEL-ARC-CONTINUITY-PLAN.md) confirmation gate | Add bounded multi-arc lifecycle and explicit predecessor links. | Cardinality, graph validity, migration, and fresh resume evidence remain deterministic and atomic. |
| C13 — deterministic opportunity pool | A campaign-time trigger chooses at most one eligible future quest from a bounded weighted pool through the recorded seeded random source. | Verified C6, Quest Q2, root-clock, and random-source evidence; [Campaign Feature 13 dependency plan](campaign/feature-13/CAMPAIGN-FEATURE-13-DETERMINISTIC-OPPORTUNITY-POOL-PLAN.md) confirmation gate | Migrate the C6 state shape and add read-only eligibility before one atomic seeded selection. | Candidate set, seed/roll, selected opportunity, quest activation, and rollback evidence are complete and auditable. |
| C14 — advancement authorization | A campaign selects XP or milestone policy and provides the exact one-time authorization consumed by a character level-up. | Campaign-bound playable-character evidence, Feature 36 XP eligibility, CH9 consume seam, and [Campaign Feature 14 plan](campaign/feature-14/CAMPAIGN-FEATURE-14-ADVANCEMENT-AUTHORIZATION-PLAN.md) confirmation gate | Milestone policy plus one exact `N→N+1` authorization with no character mutation. | Policy, authorization lifecycle, scope, replay, revocation, and CH9 rollback handoff are auditable. |
| C15 — character participation | A campaign owns one durable actor participation and its availability for character consumers. | C2 campaign root/transactions and [Campaign Feature 15 plan](campaign/feature-15/CAMPAIGN-FEATURE-15-CHARACTER-PARTICIPATION-PLAN.md) confirmation gate | One active campaign attaches one existing actor, with no character data copied into campaign state. | CH1/CH5 receive a canonical active-scope verifier; CH13 can atomically withdraw participation while preserving history. |

### Recommended order

C10 is only worthwhile after an existing-world campaign has proved the model; until then it adds
cross-owner transactional risk without more story value. C11 resolves long-running-play fork
semantics only after the existing session lifecycle and snapshot work have real evidence. Existing Slice 7 owns AI-assisted
campaign proposals, Slice 8 owns session operations/read-only views, and Slice 9 owns templates,
cloning, and player-facing expansion choices.

### Cross-plan creation boundaries

| Boundary | Root owner to ratify before work | Called capability / invariant |
| --- | --- | --- |
| New world plus campaign (C10) | One campaign-or-world orchestration root | The called plan validates its own blueprint and returns typed effects/results; neither layer performs an independent nested commit. |
| Campaign fork (C11) | Campaign lifecycle root | World, character, item, quest, and session state must be explicitly classified before an implementation slice is authorised; no implicit deep copy follows relationships. |

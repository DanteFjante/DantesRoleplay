# Game system master implementation plan

Status: **Draft integration map — planning only; no subsystem slice is authorised by this document**  
Last updated: 2026-08-20

## Purpose

This document is the single map of how DantesRoleplay becomes a complete game system: world,
campaigns, characters, items, quests, events, rules, sessions, websites, retrieval, local models,
and later interactive play.

It answers four questions:

1. What concepts exist and which subsystem owns each one?
2. Which features depend on which others?
3. Which smaller plan must an implementation model read?
4. What is the next safe unit of work, and when must the model stop?

This master plan does not replace subsystem plans, live procedure contracts, catalog artifacts, or
the ruleset roadmap. It connects them. A model implements exactly one reviewed slice from one
subsystem plan, verifies it, updates evidence, and stops.

## Authority order

When two documents disagree, use this order:

1. Verified catalog artifacts and their imported live database state.
2. Live governing procedure contracts.
3. The currently reviewed subsystem dependency plan and explicit slice handoff.
4. This master plan for ownership and dependency order.
5. Ruleset roadmaps for broad feature ordering.
6. STATUS.md and receipts for evidence of what is already implemented.
7. Architecture and discussion documents for rationale.

Repository plans describe decisions and tests. They do not become runtime game content merely by
existing as Markdown.

## Product outcome

A finished vertical campaign should allow this loop:

    create or attach a world
      -> create a campaign blueprint
      -> create player characters and starting items
      -> activate chapters, arcs, quests, factions, and world facts
      -> play actions through versioned mechanics
      -> apply effects transactionally
      -> emit and route events and notifications
      -> update quests, factions, time, and world state
      -> persist a session summary
      -> resume from database state in a fresh model context
      -> inspect the same state through a read-only human website

The story model decides intent and narration. Stored rules decide mechanics. SQLite remains the
authority for state and history. Optional local models reduce lookup/orchestration work but never
become a correctness dependency.

## Architectural invariants

1. The package-free C# kernel knows generic entities, components, containment, relationships,
   mechanics, effects, events, subscriptions, operations, and contracts. It does not learn D&D,
   quest, faction, item, campaign, or plot vocabulary.
2. Game concepts are versioned data, JavaScript mechanics, registered event types/subscriptions,
   and procedure contracts.
3. Mechanics propose effects; the engine validates and applies them.
4. Every state-changing root operation is transactional and auditable.
5. Generated content is a proposal until explicitly validated and committed.
6. Derived values are computed from authoritative facts and are not independently editable.
7. Browser code, local models, remote story models, and authored content never access SQLite
   directly.
8. Randomness uses the recorded seeded engine source. No hidden model choice or Math.random decides
   persistent outcomes.
9. Current state and historical evidence have separate owners: components hold current truth;
   operations/events explain how it changed.
10. Every capability remains usable with Ollama disabled and with the website unavailable.

## Concept and ownership map

| Concept | Runtime representation | Owner | Must not become |
| --- | --- | --- | --- |
| World | Root entity plus location, lore, faction, time, and relationship components | World/lore plan | One giant JSON document |
| Campaign | Campaign root referencing a world, goals, arcs, chapters, opportunities | Campaign creation plan | Chat context or autonomous plot generator |
| Chapter | Entity carrying one dramatic question, status, summary, and arc links | Campaign/story plan | Forced scene script |
| Story arc | Entity linking long-running stakes across chapters and quests | Campaign creation plan | Hidden prose only |
| Character | Campaign actor entity referencing source definitions and acquired state | Character creation plan | Copied rulebook record |
| NPC/creature | Actor entity plus motives, faction links, ruleset components | World/ruleset plans | Special kernel table |
| Item definition | Immutable versioned content entity describing an item type | Items/inventory plan | Mutable player possession |
| Item instance | Campaign entity referencing a definition, contained/equipped/consumed | Items/inventory plan | Embedded inventory array |
| Quest | Campaign entity with lifecycle state and objective relationships | Quest plan | Hard-coded quest engine |
| Objective | Independent entity with dependencies, evidence, visibility, and status | Quest plan | Opaque array inside quest JSON |
| Faction | Entity with goals, agenda, assets, status, and relationships | World/lore plan | Personality simulation in the kernel |
| Knowledge | Fact/rumour/secret entity or component with source and visibility | World/lore plan | Unscoped narration memory |
| Mechanic | Versioned sandboxed JavaScript rule with projection and typed output | Ruleset plans | Database or network script |
| Event | Immutable accepted change/semantic occurrence with causation evidence | Events plan | Current world state |
| Subscription | Versioned guard/reaction registration | Events plan | Arbitrary webhook or polling loop |
| Workflow | Registered typed semantic-command sequence in one transaction | Workflow plan | Caller-supplied command batch |
| Procedure | Versioned operating contract retrieved by agents | Procedure/retrieval plans | Executable permission by itself |
| Website page | Server HTML plus bounded JavaScript component enhancement | Website/API plan | Second authority over game state |
| Local AI operator | Optional schema-bound router/summariser through Ollama | Local routing plan | Autonomous GM or direct tool executor |

## Dependency map

    Verified kernel foundation
    ├─ entity/component/containment/relationship persistence
    ├─ mechanic storage, sandbox, projections, effects, transactions
    ├─ operation audit and three-verb MCP surface
    └─ events, guards, reactions, notifications, chain limits
       |
       +-> World and lore foundation
       |   ├─ world root and locations
       |   ├─ factions and knowledge
       |   └─ time/travel ownership
       |
       +-> Items and inventory
       |   ├─ immutable definitions
       |   ├─ campaign instances and containment
       |   └─ equipped/consumable state
       |
       +-> Character creation
       |   ├─ existing abilities/levels/proficiencies/HP/AC
       |   ├─ origins/classes/grants
       |   └─ starting equipment from item definitions
       |
       +-> Campaign foundation
       |   ├─ existing-world attachment
       |   └─ goals, chapters, arcs, and resume digest
       |       |
       |       +-> Quest system
       |           ├─ campaign-owned quest/objective state
       |           ├─ chapter/arc links after manual lifecycle exists
       |           └─ later event-driven progression and notifications
       |
       +-> Session lifecycle and complete play
       |   ├─ start/resume/end summary
       |   ├─ combat/travel/social/spells/rest/advancement
       |   └─ campaign snapshot/restore
       |
       +-> Human website
           ├─ read API and world/campaign projections
           ├─ SSE invalidation after commits
           └─ later map and input features

    Cross-cutting optional capabilities
    ├─ executable workflows reduce MCP round trips
    ├─ procedure semantic retrieval reduces contract lookup cost
    ├─ local Ollama profiles reduce routine story-model work
    └─ hierarchical catalogs improve discovery at scale

World, items, and character definitions may be implemented as independent vertical slices. The
story-first campaign foundation needs only an existing world; its first blueprint and digest do not
require a quest, item, or created character. Quest records are campaign-scoped, so the first quest
runtime fixture follows the campaign root/chapter foundation. Quest integration is then added back
to the campaign digest. The website and local AI are consumers; neither blocks correctness.

## Subsystem plan registry

| Subsystem | Detailed implementation authority | Current role |
| --- | --- | --- |
| Story-first delivery priority | [STORY_FIRST_ROADMAP.md](STORY_FIRST_ROADMAP.md) | Product sequence for persistent world, exploration, lore, quests, and continuity |
| Core architecture | [ARCHITECTURE.md](ARCHITECTURE.md) | Invariants and rationale |
| Verified kernel/runtime status | [STATUS.md](STATUS.md) and [NEXT_STEPS.md](NEXT_STEPS.md) | Current evidence and immediate MVP work |
| World, locations, factions, knowledge, time | [WORLD_AND_LORE_PLAN.md](WORLD_AND_LORE_PLAN.md) | Focused plan added beside this master |
| World Feature 1 — persistent topology | [World Feature 1 dependency plan](world/feature-01/WORLD-FEATURE-01-DEPENDENCY-PLAN.md) | New-standard Slice 1 plan; awaiting permanent-vocabulary confirmation |
| Campaign bootstrap, goals, chapters, arcs, opportunities | [CAMPAIGN_CREATION_PLAN.md](CAMPAIGN_CREATION_PLAN.md) | Draft implementation slices |
| Character creation and sourced grants | [CHARACTER_CREATION_PLAN.md](CHARACTER_CREATION_PLAN.md) | Draft implementation slices |
| Items, inventory, equipment, consumption | [ITEMS_AND_INVENTORY_PLAN.md](ITEMS_AND_INVENTORY_PLAN.md) | Focused plan added beside this master |
| Quests and objectives | [QUEST_IMPLEMENTATION_PLAN.md](QUEST_IMPLEMENTATION_PLAN.md) | Draft implementation slices |
| Events, guards, reactions, notifications | [EVENTS_AND_SUBSCRIPTIONS_PLAN.md](EVENTS_AND_SUBSCRIPTIONS_PLAN.md) and [receipt](EVENTS_AND_SUBSCRIPTIONS_RECEIPT.md) | Implemented foundation and rationale |
| D&D 2024 rules | [DND_RULESET_IMPLEMENTATION_PLAN.md](DND_RULESET_IMPLEMENTATION_PLAN.md) | High-level rules track |
| Complete D&D play | [ROADMAP-COMPLETE-PLAY.md](ruleset/dnd2024/ROADMAP-COMPLETE-PLAY.md) | Ordered capability inventory |
| Per-feature planning method | [TERRA-FEATURE-PLANNING-GUIDE.md](ruleset/dnd2024/TERRA-FEATURE-PLANNING-GUIDE.md) | Mandatory quality standard |
| Executable workflows | [EXECUTABLE_WORKFLOW_PLAN.md](EXECUTABLE_WORKFLOW_PLAN.md) | Planned orchestration feature |
| Procedure semantic retrieval | [PROCEDURE_SEMANTIC_RETRIEVAL_PLAN.md](PROCEDURE_SEMANTIC_RETRIEVAL_PLAN.md) | Planned hybrid FTS/vector memory |
| Local Ollama routing | [LOCAL_INTENT_ROUTING_PLAN.md](LOCAL_INTENT_ROUTING_PLAN.md) | Planned optional runtime profiles |
| Catalog hierarchy | [HIERARCHICAL_CATALOGS_PLAN.md](HIERARCHICAL_CATALOGS_PLAN.md) | Planned scale/discovery feature |
| Catalog portability | [CATALOG_PORTABILITY_PLAN.md](CATALOG_PORTABILITY_PLAN.md) | File/database authority and transfer |
| Website and API | [WEBSITE_AND_API_PLAN.md](WEBSITE_AND_API_PLAN.md) | Planned read-only human surface |
| Implementation handoff | [SUBSYSTEM_IMPLEMENTATION_HANDOFF.md](SUBSYSTEM_IMPLEMENTATION_HANDOFF.md) | Copyable bounded assignment format |

## Recommended implementation order

### Phase 0 — prove and preserve the existing foundation

Finish the current cold-walk/played-session evidence, keep the event receipt current, snapshot the
campaign database before play tests, and repair only failures found by those tests.

Exit gate: a fresh host can orient, query, run an action, observe effects/events, and resume state
from the same database.

### Phase 1 — minimum persistent world

Implement the first slices of WORLD_AND_LORE_PLAN.md: world root, location hierarchy/adjacency,
basic world facts, one faction, recurring NPC motives, and bounded inspection. Record the query
contract that the later storytelling/session procedure will consume.

Exit gate: a fresh model can answer where the party is, what places/NPCs/factions are known, and
which facts are public or hidden.

### Phase 2 — campaign spine and storytelling contract

Attach one manual campaign to the existing world, then add one chapter, one arc, and a bounded
resume digest. Align and publish the storytelling procedure only after its world/chapter state IDs
are ratified. Do not require a quest, generated proposal, item, or newly created character yet.

Exit gate: a fresh host retrieves the story contract and reconstructs the premise, active chapter,
arc stakes, relevant lore/faction state, and recent milestones from stored state.

### Phase 3 — one manually playable quest

Implement one campaign-owned quest with three objectives, evidence links, manual progress,
reconciliation, and narrative handoff. Add campaign chapter/arc links only after the quest's manual
lifecycle exists.

Exit gate: the quest can be accepted, investigated through more than one route, progressed or
resolved atomically, and resumed in a fresh session without transcript memory.

### Phase 4 — continuity proof and one reactive consequence

Play the first story through session close/restart before automating it. Then add exactly one
event-driven objective or world reaction proven by the manual session.

Exit gate: a fresh host gives a faithful recap from stored facts, and one qualifying committed
event changes exactly the intended state once while negative/rollback cases change nothing.

### Phase 5 — player-ready items and character creation

In a parallel lane, implement immutable item definitions, instances, containment/equip, and one
level-1 non-spellcasting SRD character path. Ratify the Item Slice 6 / Character Slice 5 atomic
starting-equipment boundary before assigning either integration slice.

Exit gate: a fresh MCP session discovers, validates, creates, queries, and plays one action with a
legal player-created protagonist and starting items, without raw component editing.

### Phase 6 — session lifecycle and richer play

Add start/resume/end-session handling, then follow the complete-play roadmap by dependency:
turns/action economy, conditions, damage types, healing/death, movement, equipment depth, classes,
spells, rests, advancement, travel, and social interaction.

Exit gate: each selected feature satisfies its own Terra dependency plan. There is no “implement
Tier F” or “finish combat” batch.

### Phase 7 — read-only human experience

Implement the server-rendered world/campaign/quest/character/item views, stable read API, and SSE
invalidation. Add the read-only map only after locations and spatial contracts are stable.

Exit gate: the human UI accurately reflects committed state and remains usable without JavaScript.

### Phase 8 — orchestration and retrieval optimisation

Implement registered workflows after recurring multi-call sequences are evidenced. Strengthen FTS
and confirmed aliases before vectors. Add Qwen3 embedding and Ollama routing profiles only after
their evaluation gates pass.

Exit gate: disabling workflows, vectors, and Ollama reduces convenience/performance but does not
change game correctness.

### Phase 9 — controlled generated content and interactive UI

Add reviewed AI campaign proposals, future quest generation, website creation forms, travel/battle
map commands, and content packs one vertical slice at a time.

Exit gate: generated proposals pass the same validation and transaction boundaries as hand-authored
content.

## Cross-subsystem integration contracts

### Identity and scope

Every runtime entity has one permanent ID. Ruleset definitions are shared/versioned content;
characters, item instances, quests, chapters, factions, and world facts are campaign-owned.
Relationships always reference IDs, never display names.

Missing, explicit empty, inactive, hidden, and unknown are different states. Every subsystem plan
must define them rather than relying on null/default behavior.

### Visibility

Visibility metadata may classify party, GM, character-specific, or public information. It is not a
security boundary until an authenticated caller/audience policy enforces it. Trusted MCP sessions
are GM scope initially; the website must not claim player-safe filtering before authorization
exists.

### Transactions and events

A semantic root operation allocates its operation/correlation ID before effects. Its state,
accepted events, reactions, notifications, and success audit commit together. Failure evidence is
recorded only after rollback and cannot imply partial success.

### Definitions and instances

World/ruleset definitions are immutable and versioned. Campaign instances reference exact source
identity/version and own mutable state. Updating a source definition never silently rewrites an
existing character, item, quest, or campaign.

### Randomness

Random encounters, future quest opportunities, loot, dice, and similar choices use a named
mechanic and recorded seed. Candidate set, selection order, roll, result, and source versions are
auditable. AI generation may propose candidate content but does not count as mechanical randomness.

### Correction and migration

Normal gameplay uses mechanics/semantic commands. Administrative corrections have separate
governing procedures, explicit change reasons, and audit evidence. Migration between content
versions is never implied by reading “latest”.

## Model-sized execution strategy

### High-capability planning model

Use Terra High or an equivalent model when a feature has unresolved ownership, new data contracts,
official rule interpretation, migrations, cross-subsystem dependencies, or competing architecture
choices. It performs the planning pass, dependency search, and plan-quality audit. It stops before
implementation.

### Standard implementation model

Use a standard model after a slice is fully ratified. The handoff must give exact artifact IDs,
owners, dependencies, contract/source reads, closed input semantics, tests, cleanup, and exit gate.
It implements one slice and may not redesign adjacent subsystems.

### Small implementation model

Use a lower model only for a mechanical, already-designed slice with no open decisions. Good tasks
include adding one catalog record from an approved schema, extending a test matrix with exact
expected results, implementing a named adapter behind an existing interface, or updating read-only
documentation/evidence.

A small model must stop and escalate if it finds:

- an ownership conflict or missing dependency;
- a new schema field, status, effect, event, command kind, migration, or public API decision;
- a discrepancy between catalog and live database;
- an acceptance case whose expected result is not explicit;
- a need to modify an adjacent subsystem to make its slice work.

Model strength never changes the quality gate. It changes how much ambiguity may be present in the
handoff.

## Per-slice execution rule

Every implementation turn receives:

1. one subsystem plan;
2. one named slice;
3. the live governing procedures to re-read;
4. exact existing dependencies to query;
5. the current catalog/runtime baseline;
6. exact allowed files/artifact IDs;
7. closed data/input semantics and non-goals;
8. acceptance matrix and cleanup/restoration obligations;
9. exit gate;
10. stop/escalation conditions.

The model then follows:

    orient/read contracts
      -> inspect live owners and overlaps
      -> confirm prerequisite evidence
      -> implement only named slice
      -> dry-run where supported
      -> commit/import and query back
      -> run behavioral and repository tests
      -> restore/delete fixtures
      -> update receipt/status/handoff
      -> stop

No model receives “implement campaigns,” “build quests,” or “finish character creation” as an
implementation assignment.

## Whole-system acceptance scenario

The integrated system is proven by one compact campaign:

- a campaign attaches to or creates a small world;
- one faction pursues a persistent agenda;
- one player character is created legally and owns/equips an item;
- one open chapter asks a dramatic question;
- one active quest has three objectives and linked clues/NPC/location;
- one dormant future opportunity is eligible through an event and deterministic roll;
- player intent resolves through an active mechanic and transactional effects;
- an accepted event routes a reaction and updates quest/campaign state;
- the session ends with current chapter/quest/world summaries;
- a fresh model resumes using only database queries;
- the read-only website displays the same authoritative state;
- with Ollama disabled, all mechanical outcomes remain identical.

This scenario is an integration milestone, not permission to implement multiple subsystem slices in
one pass.

## Plan maintenance

When a subsystem decision changes:

1. update the owning subsystem plan first;
2. update this master only if ownership, dependency order, or integration behavior changed;
3. update roadmap/status/handoff evidence without copying runtime payloads;
4. identify downstream plans that now need re-review;
5. do not silently reinterpret an already completed slice.

## Immediate planning decision

The story-first roadmap is the current product priority. The first new feature plan is
`world/feature-01/WORLD-FEATURE-01-DEPENDENCY-PLAN.md`, which follows the Terra planning standard
for a persistent world topology. Its Slice 1 is the next implementation candidate after reviewer
confirmation of the permanent vocabulary. Repository planning and disposable catalog validation may
continue despite catalog/database drift; resolving drift is required only before persistent import
for integration play or release.

No runtime implementation is authorised merely by creating this master plan.

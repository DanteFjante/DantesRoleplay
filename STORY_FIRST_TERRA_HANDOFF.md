# Story-first Terra High handoff

Status: **Superseded research handoff — not an active implementation assignment**  
Assignment: **G1 cross-plan story-state ownership ratification**  
Roadmap: [STORY_FIRST_ROADMAP.md](STORY_FIRST_ROADMAP.md)

The repository-first feature plan for the next runtime candidate is now
[World Feature 1](world/feature-01/WORLD-FEATURE-01-DEPENDENCY-PLAN.md). This document remains a
cross-plan research record; do not use it to block disposable catalog validation because of
catalog/database drift.

## Requested outcome

Close the data ownership and dependency contract for the first persistent story, amend every
affected owning plan, and leave World and Lore Slice 1 ready for a separate populated runtime
handoff.

## Why Terra High owns this pass

This pass crosses world, campaign, quest, storytelling, items, and character boundaries and must
resolve existing circular dependencies without inventing duplicate state. It includes permanent-ID
and schema-meaning decisions, which require confirmation under `AGENTS.md`.

## Required reads

Read these completely before changing a plan:

1. `AGENTS.md`
2. [Story-first roadmap](STORY_FIRST_ROADMAP.md)
3. [Game system master plan](GAME_SYSTEM_MASTER_PLAN.md)
4. [World and lore plan](WORLD_AND_LORE_PLAN.md)
5. [Campaign creation plan](CAMPAIGN_CREATION_PLAN.md)
6. [Quest implementation plan](QUEST_IMPLEMENTATION_PLAN.md)
7. [Storytelling procedure source](storytelling.md)
8. [Items and inventory plan](ITEMS_AND_INVENTORY_PLAN.md)
9. [Character creation plan](CHARACTER_CREATION_PLAN.md)
10. [Subsystem handoff template](SUBSYSTEM_IMPLEMENTATION_HANDOFF.md)
11. `catalog/procedures/system/procedure.system.create-feature.md`

Then search `catalog/`, repository code, migrations, and tests for every proposed owner and ID.
Inspect the live catalog/database only through the normal read/verify surface. Do not import while
the documented drift remains unresolved.

## Known baseline

- The generic entity/component/containment/relationship, mechanic/effect, transaction/audit, and
  event/subscription foundations exist.
- `procedure.play.storytelling` is now catalog-authored; `storytelling.md` is a pointer to that
  canonical contract. The publication is implemented but awaits global-suite acceptance.
- World/lore proposes ownership of world roots, locations, factions, facts, rumours, secrets, and
  clues.
- Campaign owns campaign roots, chapters, arcs, and session digests and must reference an existing
  world on the story-first path.
- Quest owns quest/objective state even though each quest is campaign-scoped.
- Item Slice 6 and Character CH5 currently overlap on the atomic starting-equipment creation
  transaction. Record a recommended root owner, but do not implement either in this pass.
- The previously observed catalog comparison reported catalog-only `lantern` and `orban`, plus
  database-only `mechanic.lock.pick`, `lock`, `coldwalk.lantern`, `coldwalk.orban`, and
  `coldwalk.practice-lock`. Re-run the read-only comparison and report any change; do not decide
  which side wins without the repository owner.

## Allowed changes

Planning/documentation only:

- `WORLD_AND_LORE_PLAN.md`
- `CAMPAIGN_CREATION_PLAN.md`
- `QUEST_IMPLEMENTATION_PLAN.md`
- `ITEMS_AND_INVENTORY_PLAN.md`
- `CHARACTER_CREATION_PLAN.md`
- `storytelling.md`
- `GAME_SYSTEM_MASTER_PLAN.md`
- `STORY_FIRST_ROADMAP.md`
- this handoff and, if useful, a new planning receipt

Do not add or change catalog records, runtime code, migrations, tests, the live database, or MCP
surface in this assignment.

## Decisions that must be closed

### Ownership

For each item, name exactly one owning plan, runtime representation, and consuming plans:

- world root and campaign-to-world reference;
- location hierarchy, current location, and travel adjacency;
- faction state and recurring NPC motive state;
- fact, rumour, secret, clue, discovery/reveal state, and provenance;
- campaign root, chapter, arc, factual session summary, and optional attributed recap prose;
- quest, objective, objective evidence, and quest history;
- item definition/instance/containment/equip state;
- actor creation and starting-equipment transaction root.

No authoritative field may be stored both as a component property and a relationship/containment
edge. No story state may exist only in the narration contract.

### IDs and shapes

For the World Slice 1 contract, propose and confirm:

- component IDs and JSON shapes for world root and location;
- entity/scoped-ID convention for one fixture world, one region, and three locations;
- containment hierarchy and whether the world/region boundary uses containment or a named relation;
- adjacency relation ID, directionality, stable identity, reverse-duplicate rule, and ordering;
- lifecycle/status and visibility vocabulary;
- missing, null, and empty semantics;
- provenance fields and correction/version behavior;
- exact query shapes needed to reconstruct hierarchy and connections.

Also reserve or explicitly defer IDs for faction, motive, fact, rumour, secret, clue, campaign,
chapter, arc, session summary, quest, and objective. Do not confirm a permanent ID only because it
appears as prose in an older plan.

### Dependency corrections

The amended plans must express all of these boundaries:

1. Campaign existing-world validation/bootstrap references World-owned records and does not create
   a second world/knowledge model.
2. Campaign chapter/arc state and a resume digest work before quest integration.
3. Quest Slices 0–3 establish manual quest state before campaign quest-link integration.
4. `procedure.play.storytelling` is published only after its referenced canonical state contracts
   exist; its implementation awaits global-suite acceptance before dependent work may rely on it.
5. An existing verified actor is allowed only for the internal story proof. Player-ready play
   requires the Items and Character plans.
6. Item Slice 6 and Character CH5 receive one agreed root-transaction owner and separate future
   handoffs.

## Required fixture blueprint

Describe, without adding runtime records:

- one world and one region;
- three locations with a valid hierarchy and at least two navigable connections;
- one faction and two recurring NPCs with distinct motives;
- one public fact, one disputed rumour, one hidden truth, and at least three clues;
- one campaign premise, one goal, one active chapter question, and one arc;
- one later quest with three required objectives, one optional objective, at least three solution
  routes, and exactly one future event-driven transition;
- one existing actor fixture for P1 and the separate supported character target for P2.

Every fixture key must state scope, owner, visibility, provenance, and the relationship/containment
edges it participates in. The blueprint is test data design, not permission to write catalog or
database content.

## World Slice 1 handoff requirements

Before G1 can close, produce a separate populated copy of
`SUBSYSTEM_IMPLEMENTATION_HANDOFF.md` for **World and Lore Slice 1 only**. It must include:

- exact component/relation/entity IDs and file paths;
- closed schemas and authoritative input/state;
- validation and canonical ordering rules;
- expected effects, events, audit result, and recovery queries;
- positive, negative, reverse-duplicate, self-link, missing-reference, and containment-cycle cases;
- failure injection/rollback cases appropriate to the chosen recording path;
- fixture cleanup/isolation strategy;
- focused tests, catalog validation, full-suite gate, and stop condition;
- an explicit statement that no campaign, quest, movement, faction, clue, item, or character runtime
  artifact is included.

If live/catalog drift prevents trustworthy ID inventory or validation, the runtime handoff remains
blocked and must say exactly why.

## Acceptance checklist

- All proposed IDs have search evidence and owner confirmation.
- Every story-state concept has one owner and no duplicate representation.
- The campaign/quest ordering cycle is removed from both owning plans.
- The item/character transaction overlap has a recorded decision or explicit confirmation blocker.
- `procedure.play.storytelling` uses only the ratified replacements; `storytelling.md` points to
  that canonical catalog contract.
- The first fixture graph is complete enough to derive every entity, component, relationship,
  visibility boundary, and later test reference.
- A populated World Slice 1 implementation handoff passes the template's exit gate.
- No runtime/catalog/database change occurred.

## Escalate and stop when

- the same concept has two plausible owners after reading both plans;
- an existing catalog/live ID conflicts with a proposed permanent ID;
- resolving drift would discard or overwrite live-only work;
- a schema decision changes a public surface, migration, or semantic meaning without confirmation;
- the World Slice 1 contract cannot be closed without also implementing a later subsystem.

## Exit gate

This assignment is complete only when the owning plans agree, the roadmap links remain accurate,
the permanent decisions are confirmed, and World Slice 1 has a separate implementation-ready
handoff. Stop there. Do not implement World Slice 1 in the same assignment.

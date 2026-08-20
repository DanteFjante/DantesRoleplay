# Story-first gameplay roadmap

Status: **Verified integration roadmap — planning only; it does not authorise a runtime slice**  
Last verified: 2026-08-20

## Product outcome

The earliest enjoyable version of DantesRoleplay is a small, consistent fantasy world that a
player can enter, explore, learn about, and influence. People want things, rumours can be checked
against clues, a quest presents a problem rather than a script, and a later session remembers what
happened.

Combat, character depth, and a visual map remain valuable, but they support this experience rather
than define the first playable release.

## What this roadmap governs

This document orders work owned by the existing subsystem plans. It does not replace those plans
and must not be used as a blanket implementation assignment. Every runtime pass still implements
one reviewed lowest slice through a populated
[subsystem implementation handoff](SUBSYSTEM_IMPLEMENTATION_HANDOFF.md).

The roadmap has been checked against:

- [Game system master plan](GAME_SYSTEM_MASTER_PLAN.md)
- [World and lore plan](WORLD_AND_LORE_PLAN.md)
- [Campaign creation plan](CAMPAIGN_CREATION_PLAN.md)
- [Quest implementation plan](QUEST_IMPLEMENTATION_PLAN.md)
- [Items and inventory plan](ITEMS_AND_INVENTORY_PLAN.md)
- [Character creation plan](CHARACTER_CREATION_PLAN.md)
- [Storytelling procedure source](storytelling.md)
- [D&D complete-play roadmap](ruleset/dnd2024/ROADMAP-COMPLETE-PLAY.md)

## First story worth playing

A player begins in a three-location setting, meets NPCs with persistent motives, hears a public
fact and a disputed rumour, chooses where to go, uncovers clues, and accepts a three-objective
quest with more than one possible approach. Ending the session stores the active chapter, quest
state, discovered knowledge, changed location, and a factual recap. A fresh GM reconstructs the
situation from database state without reading the prior chat.

There are two explicit readiness levels:

1. **Internal story-play proof:** may use one existing, catalog-owned actor fixture whose ability
   checks already work. This proves world, lore, campaign, quest, and continuity behavior.
2. **Player-ready story release:** the player can create the protagonist through the governed
   item/inventory and character-creation path. Existing fixture actors are not a product creation
   path.

Neither readiness level requires combat.

## Authority and ownership alignment

| Concern | Single owning plan | Consumers | Boundary |
| --- | --- | --- | --- |
| World root, locations, adjacency, movement | [World and lore](WORLD_AND_LORE_PLAN.md) | Campaign, quest, storytelling | Campaign references an existing world; it does not redefine location state. |
| Factions, recurring NPC motive state, facts, rumours, secrets, clues | [World and lore](WORLD_AND_LORE_PLAN.md) | Campaign, quest, storytelling | Exact NPC-motive ID remains a World Slice 0 ratification decision. |
| Campaign root, premise, chapters, arcs, session digest | [Campaign creation](CAMPAIGN_CREATION_PLAN.md) | Storytelling, quest links | Chapters frame questions; they do not own quest/objective transitions. |
| Quest and objective lifecycle, evidence, quest history | [Quest implementation](QUEST_IMPLEMENTATION_PLAN.md) | Campaign digest, storytelling | A quest is campaign-scoped, but the quest plan owns its state machine. |
| Item definitions, instances, possession, equipment | [Items and inventory](ITEMS_AND_INVENTORY_PLAN.md) | Character creation, campaign views | Characters and campaigns reference items; they do not copy item state. |
| Legal protagonist assembly and creation transaction | [Character creation](CHARACTER_CREATION_PLAN.md) | Campaign and play | No alternate story-only character creator is permitted. |
| GM voice, agency, clue discipline, state-to-fiction translation | [Storytelling](storytelling.md) | All play | The procedure owns behavior, not persistent story state. |
| Ability checks and later combat rules | [D&D ruleset roadmap](ruleset/dnd2024/ROADMAP-COMPLETE-PLAY.md) | Play | Story plans consume verified mechanics and do not reproduce their rules. |

The current `storytelling.md` uses the old shorthand names `chapter`, `motive`, and `clue`. Those
names are descriptive, not ratified runtime IDs. The procedure must be aligned to the identifiers
chosen by World Slice 0 and Campaign Slice 0 before it is published to the catalog.

## Dependency graph

```text
G1 World Feature 1 ownership/contract plan + permanent-vocabulary confirmation
  -> W1 world root and locations
          -> W2 governed movement
          -> W3 factions and recurring NPC motives
              -> W4 facts, rumours, secrets, and clues
                  -> C0 campaign blueprint ratification
                      -> C1 existing-world validation
                          -> C2 atomic campaign root bootstrap
                              -> C3 chapters, arcs, and resume digest
                                  -> S1 publish aligned storytelling procedure
                                  -> Q0-Q3 manual quest lifecycle and evidence
                                      -> C4 campaign-to-quest links and combined digest
                                          -> P1 internal played-session continuity proof
                                          -> R1 event-driven quest/world reaction

Parallel player-ready lane after G1:
I0-I4 item definitions, instances, possession, and equip
  -> CH0-CH4 supported character content and grants
      -> I6/CH5 ratified starting-equipment transaction boundary
          -> CH6 discoverable character-creation/play handoff
              -> P2 player-ready story release
```

G0 catalog/database reconciliation is an integration-play and release gate. It does not block
repository planning, focused tests, or `roleplay validate catalog` against a disposable database.

`I6/CH5` is shown as an integration boundary, not permission to implement two slices at once. Item
Slice 6 and Character Slice 5 currently describe the same atomic creation transaction from
opposite sides. Terra High must ratify which service owns that root transaction and split the two
handoffs accordingly before either slice is assigned.

## Operating rules

- Implement one reviewed lowest slice at a time. Planning gates and implementation passes are
  separate review points.
- Search the catalog and live database for an existing owner before choosing permanent IDs.
- Reconcile catalog/database drift before importing into the persistent database for integration
  play or release. It does not block repository-authoritative planning or disposable catalog
  validation. Live-only work must be exported or deliberately discarded by the owner; never
  overwrite it with `--force-files`.
- The database is story memory. A fact, motive, clue, quest change, chapter resolution, location,
  or session recap that matters later becomes governed state, not only narration.
- The GM may narrate within committed facts but may not silently reveal secrets, complete
  objectives, move a character, grant items, or create consequences that no rule recorded.
- Use verified ability-check and saving-throw mechanics for early uncertainty. Do not add a
  shortcut character model or duplicate a ruleset rule inside a story component.
- Manual, inspected transitions come before subscriptions or generated content. Automation is
  added only after played evidence shows a stable repeated transition.
- SQLite, deterministic catalog lookup, and the three-verb surface are sufficient for this route.
  PostgreSQL, vector search, and local-model routing are not prerequisites.

## Ordered implementation ledger

Every row is a separate assignment and stop point. `Ready` means its prerequisites and its owning
plan are closed enough to receive a populated handoff; it does not mean implementation is already
authorised.

| Pass | Owning slice | Prerequisites | Required deliverable and exit signal | Readiness |
| --- | --- | --- | --- | --- |
| G1 | World Feature 1 planning and semantic review | Repository inventory and cross-plan ownership review | [World Feature 1 dependency plan](world/feature-01/WORLD-FEATURE-01-DEPENDENCY-PLAN.md) proposes exact root/location/topology vocabulary, fixture, evidence, and exit gate. Permanent IDs/schema meanings require reviewer confirmation. | **Planned; awaiting confirmation** |
| W1 | World Feature 1 Slice 1 | G1 confirmation | World root, region, three locations, containment hierarchy, and canonical adjacency exist through catalog/governed paths. The feature plan owns exact validation limits and readback evidence. | Waiting on G1 |
| W2 | World Slice 2 | W1 | One existing actor moves between adjacent locations through the governed mechanic; invalid/disconnected/replayed moves are unchanged and audited. | Waiting on W1 |
| W3 | World Slice 3 | W1, G1 motive decision | One faction and two recurring NPC motives are stored with explicit links; agenda transition is atomic and inspectable. | Waiting on W1 |
| W4 | World Slice 4 | W1, W3 | One fact, rumour, secret, and at least three clues have provenance, visibility, and support/about links. Reveal/confirm changes audience knowledge without rewriting hidden truth. | Waiting on W3 |
| C0 | Campaign Slice 0 | W4 | Ratify a manual existing-world campaign blueprint with premise, goal, one chapter question, and one arc. Quest attachment is absent or optional at this gate. | Waiting on W4 |
| C1 | Campaign Slice 1 | C0 | Deterministic validation resolves the existing world and rejects bad scope, references, visibility, and duplicate IDs before writes. | Waiting on C0 |
| C2 | Campaign Slice 2 | C1 | Campaign-root creation is atomic and references, rather than recreates, the world. Injected failures leave no campaign state, events, notifications, or success audit. | Waiting on C1 |
| C3 | Campaign Slice 3 | C2 | Chapter/arc transitions and a bounded resume digest work without requiring a quest. A fresh host sees premise, current chapter, arc stakes, relevant lore/faction state, and recent milestones. | Waiting on C2 |
| S1 | Storytelling publication pass | G1, W4, C3 | Align old shorthand state names to ratified IDs, package `procedure.play.storytelling` in the catalog, validate it, and prove fresh retrieval. It must not claim unavailable quest/combat behavior. | Waiting on C3 |
| Q0 | Quest Slice 0 | W4, C3 | Ratify one creative, non-combat quest: three objectives, at least three routes/clues, one optional objective, explicit visibility, and exact manual transition ownership. | Waiting on C3 |
| Q1 | Quest Slice 1 | Q0 | Quest/objective components, relationships, procedures, and fixture records round-trip without a kernel migration. | Waiting on Q0 |
| Q2 | Quest Slice 2 | Q1 | Manual accept/advance/complete/fail/reopen rules are atomic; invalid state, dependency, authority, or replay changes nothing. | Waiting on Q1 |
| Q3 | Quest Slice 3 | Q2, S1 | Evidence/history and audience summaries are queryable; storytelling retrieves active quest state without revealing hidden truth. | Waiting on Q2 |
| C4 | Campaign Slice 4 | C3, Q3 | Link the existing quest to its chapter/arc and include it in the bounded campaign digest. Closing a chapter does not silently change quest state. | Waiting on Q3 |
| P1 | Internal played-session proof | W2, W4, C4, S1, Q3 | Complete the seven-step scenario below with an existing actor, close the process, reopen fresh, and continue from stored state only. Record a short evidence receipt. | Waiting on C4 |
| R1 | Quest Slice 4, then one selected reactive slice | P1 | One committed event progresses exactly one intended objective or world consequence once. Nonmatching, denied, repeated, and rolled-back events do nothing. | After played evidence |
| P2 | Player-ready story release | P1 and item/character lane through CH6 | A player creates one legal protagonist with starting items, enters the existing campaign, performs a supported check, and resumes later without raw component edits. | Parallel lane incomplete |
| G0 | Integration/release synchronization gate | Any persistent database import | Owner reconciles catalog/database drift; snapshot and restore are proven against the integration database. No feature is implemented by this gate. | Required only for integration play/release |

Do not combine adjacent rows merely because they touch the same fixture. A completed row updates its
owning plan/receipt, then the next row receives a new handoff.

## Player-ready item and character lane

This lane can proceed in parallel after G1 when implementation capacity allows, but it must not
delay P1.

1. Run [Items and inventory](ITEMS_AND_INVENTORY_PLAN.md) Slice 0, then Slices 1–4 as separate
   passes: definition discovery, instances/possession, transfer, and equipped state.
2. Run [Character creation](CHARACTER_CREATION_PLAN.md) Slice 0, then Slices 1–4 as separate
   passes: supported build, actor/provenance, ability integration, origin choices, and level-one
   class grants.
3. Terra High ratifies the root-transaction boundary between Item Slice 6 starting-equipment
   grants and Character Slice 5 atomic creation. The resulting handoffs must identify one root
   owner, rollback injection points, and which plan merely provides a called capability.
4. Implement the ratified Item Slice 6 capability and Character Slice 5 runner in their decided
   order, one handoff at a time.
5. Implement Character Slice 6 discovery/procedure/play handoff. This is the gate from a fixture
   actor to a player-created protagonist.

Item Slice 5 consumption, advanced inventory, a website character builder, additional classes,
and advancement are not required for P2.

## Milestone design constraints

### World before plot

The first fixture is deliberately small: one region, three locations, one faction, two recurring
NPCs, one public fact, one rumour, one hidden truth, and at least three clues pointing toward the
first important conclusion. Movement comes before distance, terrain, clocks, or a map.

### Campaign attaches to the world

The first campaign uses manual, reviewed existing-world attachment. It contains one premise, one
goal, one starting location reference, one active chapter question, one arc, and one faction stake.
It must be playable and resumable before a quest link exists. AI generation, multiple worlds, and
dormant random opportunities wait.

### Quests preserve agency

The first quest cannot depend on combat, a single NPC, a single clue, or a single roll. It offers
several approaches to the same central problem: conversation, location investigation, rumour
verification, physical evidence, or another world-consistent plan. A failed check changes cost,
position, time, or attention; it does not erase the only route forward.

### Consequences follow manual proof

The first reaction is deliberately narrow, such as a committed clue discovery progressing one
named objective. The event runtime supplies transaction and audit behavior; it does not author
plots or choose consequences autonomously.

### Recap is a projection, not canon

Session closure records structured factual changes and a bounded factual summary. Entertaining,
biased, or in-world recap prose may be stored separately with attribution, but never replaces
facts, quest status, chapter state, or event history.

## First played-session acceptance scenario

1. A fresh GM retrieves the storytelling procedure and current campaign/world digest.
2. The player begins at the stored location and hears a public fact plus a conflicting rumour from
   an NPC whose stored motive explains what they want.
3. The player chooses a connected destination; governed movement records the new containment.
4. A creative action or supported check uncovers one permitted clue. Hidden truth remains hidden.
5. The player accepts or advances a quest objective. Evidence, reason, and lifecycle state are
   inspectable.
6. Session closure updates the chapter or records it as open, writes the factual summary, and
   preserves the unresolved decision.
7. The process is restarted. A fresh GM queries the same database, gives a faithful in-world
   recap, and returns control at a concrete next decision without using the old transcript.

The roadmap succeeds when this feels like returning to a living place, not reloading a checklist.

## Terra High execution contract

Terra High may use this roadmap to select the next pass, but implements only from the owning plan
plus a populated handoff. For every pass it must:

1. Read this roadmap, the master plan, the owning plan, directly consumed plans, `AGENTS.md`, and
   the live `procedure.system.create-feature` contract.
2. Search repository/catalog owners and inspect live state when the pass depends on it. Record
   unresolved drift instead of forcing a persistent import. Continue repository planning/validation
   where the catalog is authoritative.
3. Close the slice contract: exact IDs, schemas, authoritative inputs/state, derived values,
   missing/null/empty semantics, ordering, transitions, result/effects, errors, fixtures, cleanup,
   and rollback points.
4. Populate [the handoff template](SUBSYSTEM_IMPLEMENTATION_HANDOFF.md) for exactly one row in the
   ledger. If a permanent ID, schema meaning, migration, public surface, or cross-plan owner is not
   ratified, stop at planning and request confirmation.
5. Implement catalog/contracts first, then the smallest necessary runtime code. Do not add story,
   quest, campaign, item, or character vocabulary to the generic kernel.
6. Run focused tests, `roleplay validate catalog` after catalog edits, the full suite at feature
   acceptance, and protocol walk only if the MCP surface/dependency registration changed.
7. Query back the accepted state, prove negative/no-change and rollback cases, record evidence,
   update readiness here, and stop.

The next implementation candidate is World Feature 1 Slice 1 in
[its dependency plan](world/feature-01/WORLD-FEATURE-01-DEPENDENCY-PLAN.md). It is not authorised
until its proposed permanent IDs and schema meanings receive semantic confirmation. The older
[story-first Terra handoff](STORY_FIRST_TERRA_HANDOFF.md) is retained as a cross-plan research
record, not an active implementation assignment.

## What deliberately waits

| Later capability | Reason |
| --- | --- |
| Combat Feature 11 onward | Useful when the story reaches a fight, but not required for exploration, investigation, or a creative first quest. |
| Spells, monsters, tactical maps | Depend on later rules/spatial foundations and do not improve the first continuity proof. |
| AI campaign generation | A manual reviewed campaign must prove the data model first. |
| Website and interactive map | Consumers of stable world/campaign projections, not authorities. |
| Workflows, local routing, vector search, PostgreSQL | Scaling and convenience work; none is required for correctness or first play. |

## Plan-maintenance rule

When this roadmap discovers a shared ownership or sequencing conflict, amend the owning subsystem
plan first and then this document. Runtime receipts record completed evidence; plans remain
prospective. No runtime implementation is authorised merely by this roadmap.

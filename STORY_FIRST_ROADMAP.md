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
- [Session operations plan](SESSION_OPERATIONS_PLAN.md)
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
| Session lifecycle, factual recap, checkpoints, participation, and later table controls | [Session operations](SESSION_OPERATIONS_PLAN.md) | Campaign C8, World, Quest, Character, Items, ruleset, Website/API | Session records identify bounded continuity; they never replace owner state or chat as authority. |
| Session Feature S0 — continuity-boundary ratification | [S0 record](session/feature-00/SESSION-FEATURE-00-RATIFICATION.md) | C3/C4/C5 projection inventory and checkpoint decision | **Ratified:** first fixture is C3-only and trusted-host; quest/audience policy is omitted and checkpoint evidence waits for S4. |
| Session Feature S1 — start one active session | [S1 plan](session/feature-01/SESSION-FEATURE-01-DEPENDENCY-PLAN.md) | Ratified S0; C3/C4/C5; campaign-scoped uniqueness guard | Start one campaign-scoped active session atomically, without changing any story or gameplay owner state. |
| Session Feature S2 — fresh-host resume | [S2 plan](session/feature-02/SESSION-FEATURE-02-DEPENDENCY-PLAN.md) | Accepted S0/S1; approved C3/C4/C5 projections; read-surface confirmation | Reconstruct bounded current factual session context with zero writes and no transcript dependency. |
| Session Feature S3 — end and factual recap | [S3 plan](session/feature-03/SESSION-FEATURE-03-DEPENDENCY-PLAN.md) | Accepted S0–S2; source projection/version guards; C8 root | Close one active session once with a source-bound factual record while preserving all owner state. |
| Session Feature S4 — checkpoint and recovery evidence | [S4 plan](session/feature-04/SESSION-FEATURE-04-DEPENDENCY-PLAN.md) | Accepted S0–S3; snapshot owner; C11 scope classification | Record one named checkpoint reference and prove interruption recovery; restore stays gated by complete atomic domain ownership. |
| Session Feature S5 — roster and character eligibility | [S5 plan](session/feature-05/SESSION-FEATURE-05-DEPENDENCY-PLAN.md) | Session lifecycle; campaign character attachment; CH13 lifecycle | Enroll one eligible same-campaign character by immutable session reference, without player identity or action authority. |
| Session Feature S6 — gameplay handoff and audit correlation | [S6 plan](session/feature-06/SESSION-FEATURE-06-DEPENDENCY-PLAN.md) | Active session/roster; one opt-in action; ActionRunner and audit context | Run one existing compatible action under session scope and correlate its root audit without a session activity log. |
| Session Feature S7 — attributed narrative artifacts | [S7 plan](session/feature-07/SESSION-FEATURE-07-DEPENDENCY-PLAN.md) | Ended factual S3 recap; trusted-host retention decision; C5/CH14 for player exposure | Publish one immutable, source-bound, noncanonical trusted-host narrative recap without copying or changing game truth. |
| Session Feature S8 — player-safe view and bounded controls | [S8 plan](session/feature-08/SESSION-FEATURE-08-DEPENDENCY-PLAN.md) | Identity/C5/CH14 policy; S5/C8 projections; Website/API; S6 action for control | Provide one fixed participant view, then delegate one existing player-safe session action without browser-owned authority. |
| Session Feature S9 — remote concurrent collaboration | [S9 plan](session/feature-09/SESSION-FEATURE-09-DEPENDENCY-PLAN.md) | Accepted S8; remote security/identity; bounded SSE; action conflict contract | Let two remote participants reauthorize and refresh after commits, then prove one ordinary action’s concurrent conflict behavior. |
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
G1 World Feature 1 ownership/contract plan + permanent-vocabulary confirmation [complete]
  -> W1 world root and locations [complete]
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
                                              -> C10 compose a new world
                  -> W5 explicit world time
                      -> W6 agenda-triggered clue reveal
                          -> W7 bounded trusted-GM world reads
                              -> W8 named on-foot journeys
                                  -> W9 trusted-GM map layout
                                      -> W10 clock-driven route closure
                                          -> W11 manual faction front
                                              -> W12 generic ground-conveyance journey
                                                  -> W13 generic aerial-conveyance journey
                                                      -> W14 multi-leg on-foot itinerary
                                                          -> W15 fixed teleport portal
                                                              -> W16 mode-aware distant itinerary

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
Slice 6 and CH5 currently describe the same atomic creation transaction from
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
| G1 | World Feature 1 planning and semantic review | Repository inventory and cross-plan ownership review | [World Feature 1 dependency plan](world/feature-01/WORLD-FEATURE-01-DEPENDENCY-PLAN.md) ratified exact root/location/topology vocabulary, fixture, evidence, and exit gate. | **Complete** |
| W1 | World Feature 1 Slice 1 | G1 confirmation | World root, region, three locations, containment hierarchy, and canonical adjacency exist through catalog/governed paths; [receipt](world/feature-01/WORLD-FEATURE-01-RECEIPT.md) records fresh-import/readback evidence. | **Verified** |
| W2 | World Feature 2 — governed movement | W1; [World Feature 2 dependency plan](world/feature-02/WORLD-FEATURE-02-DEPENDENCY-PLAN.md) | One fixture traveller moves between adjacent locations through the governed mechanic; invalid/disconnected/replayed moves are unchanged and audited. [Slice 2 receipt](world/feature-02/WORLD-FEATURE-02-SLICE-2-RECEIPT.md). | **Verified** |
| W3 | World Feature 3 — factions and recurring motives | W1; [World Feature 3 dependency plan](world/feature-03/WORLD-FEATURE-03-DEPENDENCY-PLAN.md) | One faction and two recurring NPC motives are stored with explicit links; the agenda transition is atomic and inspectable. [Slice 2 receipt](world/feature-03/WORLD-FEATURE-03-SLICE-2-RECEIPT.md). | **Verified** |
| W4 | World Feature 4 — knowledge and clues | W3; [World Feature 4 dependency plan](world/feature-04/WORLD-FEATURE-04-DEPENDENCY-PLAN.md) | One fact, rumour, secret, and at least three clues have provenance, descriptive visibility, and scope/support/about links. Reveal/confirm changes knowledge state without rewriting hidden truth. [Slice 2 receipt](world/feature-04/WORLD-FEATURE-04-SLICE-2-RECEIPT.md). | **Verified** |
| W5 | World Feature 5 — explicit world time | W4; [World Feature 5 dependency plan](world/feature-05/WORLD-FEATURE-05-DEPENDENCY-PLAN.md) confirmation gate | One root-owned clock advances monotonically through a deterministic action; its structural event and action audit provide evidence. | **Planned; sequenced after Features 3–4 and permanent vocabulary confirmation** |
| W6 | World Feature 6 — agenda-triggered clue reveal | Verified W3 agenda action; W4 clue foundation; [World Feature 6 dependency plan](world/feature-06/WORLD-FEATURE-06-DEPENDENCY-PLAN.md) and [implementation receipt](world/feature-06/WORLD-FEATURE-06-IMPLEMENTATION-RECEIPT.md) | The committed fixture agenda `ready → advanced` event reveals `clue.feature-04.oren-letter` exactly once through a bounded event reaction. | **Verified** |
| W7 | World Feature 7 — bounded trusted-GM world reads | W4 fixture/readback; W5–W6 sequence; [World Feature 7 dependency plan](world/feature-07/WORLD-FEATURE-07-DEPENDENCY-PLAN.md) and [implementation receipt](world/feature-07/WORLD-FEATURE-07-IMPLEMENTATION-RECEIPT.md) | A generic capped graph query powers fixed world/location/faction/knowledge recipes without a world-specific C# branch or player-safe visibility claim. | **Verified** |
| W8 | World Feature 8 — named on-foot journeys | W1, W2, and verified W5; [World Feature 8 dependency plan](world/feature-08/WORLD-FEATURE-08-DEPENDENCY-PLAN.md) and [implementation receipt](world/feature-08/WORLD-FEATURE-08-IMPLEMENTATION-RECEIPT.md) | One directed fixture route moves an active traveller and advances the root clock together, without changing Feature 2 local movement. | **Verified** |
| W9 | World Feature 9 — trusted-GM map layout | Verified W7 and W8; [World Feature 9 dependency plan](world/feature-09/WORLD-FEATURE-09-DEPENDENCY-PLAN.md) and [implementation receipt](world/feature-09/WORLD-FEATURE-09-IMPLEMENTATION-RECEIPT.md) | Authored display anchors and a bounded layout recipe expose one region's topology/routes without making coordinates authoritative or rendering a website map. | **Verified** |
| W10 | World Feature 10 — clock-driven route closure | Verified W5, W6, and an isolated disposable W8 journey; [World Feature 10 dependency plan](world/feature-10/WORLD-FEATURE-10-DEPENDENCY-PLAN.md), [Slice 1 receipt](world/feature-10/WORLD-FEATURE-10-SLICE-1-RECEIPT.md), and [implementation receipt](world/feature-10/WORLD-FEATURE-10-IMPLEMENTATION-RECEIPT.md) | A fixed root-clock reaction synchronizes one scoped condition and explicit route availability; an active closure denies its journey with no partial travel/time change. | **Verified** |
| W11 | World Feature 11 — manual faction front | Verified W3, W5, W6, and confirmed vocabulary; [World Feature 11 dependency plan](world/feature-11/WORLD-FEATURE-11-DEPENDENCY-PLAN.md), [Slice 1 receipt](world/feature-11/WORLD-FEATURE-11-SLICE-1-RECEIPT.md), and [implementation receipt](world/feature-11/WORLD-FEATURE-11-IMPLEMENTATION-RECEIPT.md) | One scoped front advances from an expected phase with current clock evidence; exclusive territorial control stays separate from general faction claims. | **Verified** |
| W12 | World Feature 12 — generic ground conveyance | Verified W5 and W8; [World Feature 12 revision](world/feature-12/WORLD-FEATURE-12-GROUND-CONVEYANCE-PLAN.md), [Slice 1 receipt](world/feature-12/WORLD-FEATURE-12-SLICE-1-RECEIPT.md), and [implementation receipt](world/feature-12/WORLD-FEATURE-12-IMPLEMENTATION-RECEIPT.md) | Slice 1 establishes generic ground state/distance. Slice 2 moves driver and conveyance atomically with elapsed time from route distance and conveyance speed. | **Verified** |
| W13 | World Feature 13 — generic aerial conveyance | Verified W5 and W12; [World Feature 13 plan](world/feature-13/WORLD-FEATURE-13-DEPENDENCY-PLAN.md), [Slice 1 receipt](world/feature-13/WORLD-FEATURE-13-SLICE-1-RECEIPT.md), and [implementation receipt](world/feature-13/WORLD-FEATURE-13-IMPLEMENTATION-RECEIPT.md) | A rider and aerial conveyance co-move over an explicit aerial route, which may join non-adjacent locations and is not granted by ground routes. A dragon is only the first fixture. | **Verified** |
| W14 | World Feature 14 — distant on-foot itinerary | Verified W7, W8, and W10; [World Feature 14 plan](world/feature-14/WORLD-FEATURE-14-DEPENDENCY-PLAN.md), [Slice 1 receipt](world/feature-14/WORLD-FEATURE-14-SLICE-1-RECEIPT.md), and [implementation receipt](world/feature-14/WORLD-FEATURE-14-IMPLEMENTATION-RECEIPT.md) | A read-only route planner proposes a bounded destination itinerary, but each leg is separately re-planned and executed through Feature 8. | **Verified** |
| W15 | World Feature 15 — fixed teleport portals | Verified W2 and W5; [World Feature 15 plan](world/feature-15/WORLD-FEATURE-15-DEPENDENCY-PLAN.md), [Slice 1 receipt](world/feature-15/WORLD-FEATURE-15-SLICE-1-RECEIPT.md), and [implementation receipt](world/feature-15/WORLD-FEATURE-15-IMPLEMENTATION-RECEIPT.md) | A fixed world portal instantly relocates one co-located traveller to one explicit destination; time and route state do not change. | **Verified** |
| W16 | World Feature 16 — mode-aware distant itinerary | Verified W8, W12, W13, and W15; [World Feature 16 plan](world/feature-16/WORLD-FEATURE-16-MODE-AWARE-ITINERARY-PLAN.md) confirmation gate | A far-destination planner selects only explicitly available on-foot, ground, air, and fixed-portal legs, then executes and re-plans one leg at a time. | **Planned; blocked by individual travel-mode verification and itinerary vocabulary confirmation** |
| C0 | Campaign Slice 0 | W4; [Campaign Feature 0 plan](campaign/feature-00/CAMPAIGN-FEATURE-00-DEPENDENCY-PLAN.md) host-confirmation gate | Ratify a manual existing-world campaign blueprint with premise, goal, one chapter question, and one arc. Quest attachment is absent or optional at this gate. | Planned; blocked by host brief confirmation. |
| C1 | Campaign Slice 1 | C0; [Campaign Feature 1 plan](campaign/feature-01/CAMPAIGN-FEATURE-01-DEPENDENCY-PLAN.md) confirmation gate | Deterministic validation resolves the existing world and rejects bad scope, references, visibility, and duplicate IDs before writes. | Waiting on C0. |
| C2 | Campaign Slice 2 | C1; [Campaign Feature 2 plan](campaign/feature-02/CAMPAIGN-FEATURE-02-DEPENDENCY-PLAN.md) confirmation gate | Campaign-root creation is atomic and references, rather than recreates, the world. Injected failures leave no campaign state, events, notifications, or success audit. | Waiting on C1. |
| C3 | Campaign Slice 3 | C2; [Campaign Feature 3 plan](campaign/feature-03/CAMPAIGN-FEATURE-03-DEPENDENCY-PLAN.md) | Chapter/arc transitions and a bounded trusted-host resume digest work without requiring a quest. A fresh host sees premise, current chapter, arc stakes, canonical references, and recent milestones. | **Verified in disposable campaign test.** |
| S1 | Storytelling publication pass | G1, W4, C3; [S1 plan](storytelling/feature-01/STORYTELLING-FEATURE-01-DEPENDENCY-PLAN.md) | Align old shorthand state names to ratified IDs, package `procedure.play.storytelling` in the catalog, validate it, and prove fresh retrieval. It must not claim unavailable quest/combat behavior. | **Implemented; global acceptance pending** the unrelated turn-budget regression recorded in the [S1 validation](storytelling/feature-01/STORYTELLING-FEATURE-01-VALIDATION.md). |
| Q0 | Quest Slice 0 | W4, C3; [canonical quest plan](QUEST_IMPLEMENTATION_PLAN.md#verified-q0--first-quest-editorial-review) | Ratify one creative, non-combat quest: three objectives, at least three routes/clues, one optional objective, explicit visibility, and exact manual transition ownership. | **Ratified test review.** |
| Q1 | Quest Slice 1 | Q0; [canonical quest plan](QUEST_IMPLEMENTATION_PLAN.md#verified-q1--closed-draft-creation) | Quest/objective components, relationships, procedures, and fixture records round-trip without a kernel migration. | **Verified.** The canonical plan records closed creation, title ownership, C3/reference validation, replay, surface, and repository evidence. |
| Q2 | Quest Slice 2 | Verified Q1; [Q2 dependency plan](quest/feature-02/QUEST-FEATURE-02-DEPENDENCY-PLAN.md) | Manual accept/advance/complete/fail/reopen rules are atomic; invalid state, dependency, authority, or replay changes nothing. | **Verified:** Q2.1–Q2.3 establish the complete manual lifecycle: offer/accept, owned progression, explicit reconciliation, and terminal correction. |
| Q3 | Quest Slice 3 | Q2; [Q3 dependency plan](quest/feature-03/QUEST-FEATURE-03-DEPENDENCY-PLAN.md) | A trusted host reads one bounded current quest/evidence-reference/lifecycle timeline without raw hidden payloads. Real audience authorization remains C5. | **Planned:** Q3.0 must confirm `procedure.quest.inspect`, `quest-summary`, fixed bounds, and descriptive-only visibility. Q3.2 waits for S1; C4 consumes Q3.1 later. |
| C4 | Campaign Slice 4 | C3, Q3; [Campaign Feature 4 plan](campaign/feature-04/CAMPAIGN-FEATURE-04-DEPENDENCY-PLAN.md) confirmation gate | Link the existing quest to its chapter/arc and include it in the bounded campaign digest. Closing a chapter does not silently change quest state. | Waiting on Q3. |
| CH0 | Character Feature CH0 | [Character Feature 0 plan](character/feature-00/CHARACTER-FEATURE-00-DEPENDENCY-PLAN.md) host-confirmation gate | Ratify one complete SRD 5.2.1 level-one non-spellcasting build and map every choice/result to its future owner. | Planned; no character runtime content or state is created. |
| CH1 | Character Feature CH1 | [Character Feature 1 plan](character/feature-01/CHARACTER-FEATURE-01-DEPENDENCY-PLAN.md); CH0 and campaign-attachment gates | Establish immutable character-content provenance, then a campaign-scoped actor profile without copied rules, campaign state, or items. | Planned; Slice 1 awaits CH0 and Slice 2 awaits the campaign-owned attachment contract. |
| CH2 | Character Feature CH2 | [Character Feature 2 plan](character/feature-02/CHARACTER-FEATURE-02-DEPENDENCY-PLAN.md); CH0/CH1 gates | Validate a source-cited ability assignment and compose the existing ability and level owners; leave grants and vital stats to later owners. | Planned; assignment policy awaits CH0 and actor-scope integration awaits CH1. |
| CH3 | Character Feature CH3 | [Character Feature 3 plan](character/feature-03/CHARACTER-FEATURE-03-DEPENDENCY-PLAN.md); CH0–CH2 and owner-map gates | Resolve separate immutable species/background grants and closed choices without creating item state or opaque unsupported facts. | Planned; each selected grant requires a real target owner. |
| CH4 | Character Feature CH4 | [Character Feature 4 plan](character/feature-04/CHARACTER-FEATURE-04-DEPENDENCY-PLAN.md); CH0–CH3 and class/HP/AC/item gates | Resolve one level-one non-spellcasting class and dispatch its grants without duplicating class, hit-die, vital-stat, or item state. | Planned; a playable fixture awaits dedicated class/HP and AC/equipment derivation owners. |
| CH5 | Character Feature CH5 | [Character Feature 5 plan](character/feature-05/CHARACTER-FEATURE-05-DEPENDENCY-PLAN.md); CH0–CH4, campaign, items, and composition gates | Create one complete character and starting items in one ActionRunner transaction, with no partial actor state. | Planned; generic staged composition must first support validated effects for a new, unpersisted actor. |
| CH6 | Character Feature CH6 | [Character Feature 6 plan](character/feature-06/CHARACTER-FEATURE-06-DEPENDENCY-PLAN.md); accepted CH5 and surface-inspection gates | Let a fresh MCP session discover one supported build, create it through the existing action path, inspect it, and make one safe first action. | Planned; no new tool/kind is presumed. |
| CH7 | Character Feature CH7 | [Character Feature 7 plan](character/feature-07/CHARACTER-FEATURE-07-DEPENDENCY-PLAN.md); played CH6 and evidence gates | Preserve created characters while adding regression evidence, narrow profile correction, and one reviewed source-content option. | Planned; no content migration or mechanical correction is authorised. |
| CH8 | Character Feature CH8 | [Character Feature 8 plan](character/feature-08/CHARACTER-FEATURE-08-DEPENDENCY-PLAN.md); CH7, CH5 validation, and Website/API gates | Guide a build with stateless questions and later present the exact same create request in a local server-rendered builder. | Planned; browser work is not creation authority and needs separate semantic-write approval. |
| CH9 | Character Feature CH9 | [Character Feature 9 plan](character/feature-09/CHARACTER-FEATURE-09-DEPENDENCY-PLAN.md); played CH6/CH7 evidence, campaign advancement authorization, ruleset class/HP, and transaction gates | Advance one supported non-spellcasting character atomically from class/total level 1 to 2 through versioned declarations and actual owner calls. | Planned; it neither chooses XP/milestone policy nor supports later levels, subclasses, spellcasting, feats, or multiclassing. |
| CH10 | Character Feature CH10 | [Character Feature 10 plan](character/feature-10/CHARACTER-FEATURE-10-DEPENDENCY-PLAN.md); played CH9 evidence, ruleset Feature 31, ratified caster sources, and atomic composition | Integrate one source-cited caster path with ruleset-owned spellcasting resource state. | Planned; CH10 owns no parallel spells/slots or casting calculations, and spell execution remains ruleset Feature 32. |
| CH11 | Character Feature CH11 | [Character Feature 11 plan](character/feature-11/CHARACTER-FEATURE-11-DEPENDENCY-PLAN.md); a level-appropriate CH9 slice, ruleset Feature 28, and effect-owner gates | Apply one source-cited non-spellcasting feat-or-ASI entitlement atomically as part of advancement. | Planned; Feature 28 owns feat/ability state, while spell, subclass, item, origin, and multiclass families remain separate. |
| CH12 | Character Feature CH12 | [Character Feature 12 plan](character/feature-12/CHARACTER-FEATURE-12-DEPENDENCY-PLAN.md); higher-level CH9, ruleset Feature 27, CH10 compatibility, and membership-migration gates | Migrate to one canonical plural membership model and atomically add one legal non-spellcasting second class. | Planned; it does not allow mixed class representations, third classes, caster slot aggregation, subclass, or respec. |
| CH13 | Character Feature CH13 | [Character Feature 13 plan](character/feature-13/CHARACTER-FEATURE-13-DEPENDENCY-PLAN.md); CH6 surface, campaign participation lifecycle, and transaction gates | Retire then archive one campaign-attached character while preserving their history and state. | Planned; it is not deletion, NPC conversion, item transfer, a death state, reactivation, or authorization. |
| CH14 | Character Feature CH14 | [Character Feature 14 plan](character/feature-14/CHARACTER-FEATURE-14-DEPENDENCY-PLAN.md); real identity/policy enforcement, CH6/CH13, campaign administration, and one player-safe action | Let one authenticated principal control one explicitly granted active character through bounded reads and actions. | Planned; profile/visibility/client input do not authorize, and co-control, self-service, or remote exposure remain separate. |
| P1 | Internal played-session proof | W2, W4, C4, S1, Q3 | Complete the seven-step scenario below with an existing actor, close the process, reopen fresh, and continue from stored state only. Record a short evidence receipt. | Waiting on C4 |
| C10 | Campaign Feature 10 — compose a new world | Verified C2, P1, W1/W4, a world-owned small-world composer, and [Campaign Feature 10 plan](campaign/feature-10/CAMPAIGN-FEATURE-10-DEPENDENCY-PLAN.md) cross-root confirmation gate | One fixed world blueprint and campaign are previewed, then created atomically by one ratified outer coordinator. | **Planned; blocked by campaign/world composer and transaction-boundary evidence** |
| R1 | Quest Slice 4, then one selected reactive slice | P1 | One committed event progresses exactly one intended objective or world consequence once. Nonmatching, denied, repeated, and rolled-back events do nothing. | After played evidence and a Q4 plan for the selected transition |
| P2 | Player-ready story release | P1 and item/character lane through CH6 | A player creates one legal protagonist with starting items, enters the existing campaign, performs a supported check, and resumes later without raw component edits. | Parallel lane incomplete |
| G0 | Integration/release synchronization gate | Any persistent database import | Owner reconciles catalog/database drift; snapshot and restore are proven against the integration database. No feature is implemented by this gate. | Required only for integration play/release |

Do not combine adjacent rows merely because they touch the same fixture. A completed row updates its
owning plan/receipt, then the next row receives a new handoff.

## Player-ready item and character lane

This lane can proceed in parallel after G1 when implementation capacity allows, but it must not
delay P1.

1. Run [Items and inventory](ITEMS_AND_INVENTORY_PLAN.md) Slice 0, then Slices 1–4 as separate
   passes: definition discovery, instances/possession, transfer, and equipped state.
2. Run [Character creation](CHARACTER_CREATION_PLAN.md) CH0, then CH1–CH4 as separate
   passes: supported build, actor/provenance, ability integration, origin choices, and level-one
   class grants.
3. The reviewer ratifies the root-transaction boundary between Item Slice 6 starting-equipment
   grants and CH5 atomic creation. The resulting handoffs must identify one root
   owner, rollback injection points, and which plan merely provides a called capability.
4. Implement the ratified Item Slice 6 capability and CH5 runner in their decided
   order, one handoff at a time.
5. Implement CH6 discovery/procedure/play handoff. This is the gate from a fixture
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

World Features 1 and 2 are verified in their
[topology](world/feature-01/WORLD-FEATURE-01-RECEIPT.md) and
[movement](world/feature-02/WORLD-FEATURE-02-SLICE-2-RECEIPT.md) receipts. World Feature 3 is
verified in its [Slice 2 receipt](world/feature-03/WORLD-FEATURE-03-SLICE-2-RECEIPT.md). The next
story-first candidate is World Feature 4's separate knowledge-and-clues confirmation boundary.
The older [story-first Terra handoff](STORY_FIRST_TERRA_HANDOFF.md) is retained as a cross-plan
research record, not an active implementation assignment.

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

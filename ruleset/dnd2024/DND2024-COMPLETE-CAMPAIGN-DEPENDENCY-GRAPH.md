# D&D 2024 complete-campaign dependency graph

Status: **G1/G7 accepted; G7N implemented, acceptance pending unrelated D&D suite repair**
Ruleset alignment: **dnd2024-compatible** at the complete-campaign root; each generic host leaf is
`ruleset-neutral`, and each rule leaf is `dnd2024-owned`
Source: **not applicable to the compatible root**; every `dnd2024-owned` implementation leaf must
bind `source.dnd2024.srd-5.2.1` to an exact locator before it can become ready
Owner: [D&D 2024 roadmap](ROADMAP.md)

## Outcome and non-goals

Deliver one persistent D&D 5e 2024 table that a DM can create, populate, run, save, resume, and
inspect while players can join, create or use characters, act, and see only authorized information.
The complete outcome includes:

- stable application activation and private-table access;
- reusable worlds, campaigns, sessions, chapters, arcs, and quests;
- nested locations from world through continent, country/region, settlement, site, and interior;
- reviewed connections, routes, maps, time, factions, NPCs, knowledge, secrets, and consequences;
- transactional character creation, advancement, inventory, spellcasting, rests, and recovery;
- exploration, conversation, encounters, tactical combat, monsters, hazards, rewards, and downtime;
- one shared DM/Player workspace with server-enforced audience filtering; and
- deterministic replay/rollback, autosave, export/import, and reviewed old-data migration.

Homebrew, 2014 compatibility rules, public anonymous hosting, billing, voice/video, and unreviewed
copyrighted content are not silently included. They require separate sources or product plans.
Generated art remains presentation-only unless an explicit state owner references it.

## State legend

| State | Meaning |
| --- | --- |
| `verified` | Current catalog/code/tests prove the owner and usable boundary. |
| `ready` | Independently specifiable as one implementation document. |
| `planned` | Owner is known, but a prerequisite or feature contract remains. |
| `missing` | No current owner or execution seam exists. |
| `blocked` | A named missing/conflicting prerequisite prevents safe work. |
| `conflicting` | Two authorities, duplicate identities, or incompatible contracts exist. |

`implemented`, `partial`, and `superseded` may appear only as historical evidence/dispositions, not
as dependency-node readiness states.

## Existing owners and evidence

Snapshot date: **2026-08-30** at Git base `76d3da25283f5177d3a377c742ccb51d4e5a312e` plus the
preserved dirty worktree. Input fingerprints are SHA-256
`afea242fb9201cf5a8d28893319dca41879c7e4b76924524d95b16bd55cd0b74` for the canonical
crosswalk, `ac081fea8706ec93cff82078b753b00018ca1b2c255cfeb908687151cf121523` for Slice 8 closure,
and `2b07ed18f7c55fe116171dd4a70a2746d2b1542b71f15a192598c15418b5666b` for coverage matrix
1B. G1 owns a fresh whole-input fingerprint; these evidence hashes are not that future ledger.

| Concern | Current owner | Evidence | State |
| --- | --- | --- | --- |
| Generic application kernel | Generic C# application/ECS/effect/operation hosts | Registered applications, exact schemas, declared projections, sandboxed JavaScript, typed effects, transactions, operation history, replay, and adoption exist. | `verified` |
| Generic world topology | `game.core.world.root`, `game.core.world.location`, containment, `game.core.world.location.connected-to` | World read/spatial/travel procedures and focused tests exist. | `verified` |
| World/campaign convergence | Live `dnd2024.game.core.*` component types; parallel prototype roots are migration inputs only | G7 established the runtime owner; G7N has removed application-local identity inversion and unqualified D&D references. | `implemented` |
| Campaign, quest, and session structures | Generic campaign/chapter/arc/session/quest owners | Participation, checkpoints, recaps, objectives, and procedures exist, but creation, restore, and general quest transitions are incomplete. | `conflicting` |
| World knowledge | Generic facts, secrets, clues, rumours, factions, motives, fronts, and audience knowledge | Read/authoring structures exist; complete Player-byte secrecy and live authoring remain acceptance work. | `planned` |
| D&D structure/content | D&D application schemas/archetypes/authored records | 154 component schemas, 71 archetypes, and 2,329+ authored records provide broad structural coverage. | `verified` |
| Executable authored content | D&D grant/rule/activity facets | 382 activity memberships are empty; all 9 species, 24 progressions/480 levels, 4 backgrounds, 17 feats, 130 choice options, and 229 features lack executable grants/rules. | `missing` |
| Active D&D mechanics | Catalog JavaScript mechanics | 69 current mechanics exist; 13 active contracts still request retired owners. | `conflicting` |
| Weapons | Current weapon definitions plus attack activities | All 38 weapons reference 51 schema-valid exact-metre activities; focused tests pass 7/7 in the [weapon implementation evidence](DND2024-MECHANIC-REPAIR-WEAPON-ACTIVITIES-IMPLEMENTATION.md). | `verified` |
| Archived mechanic gap | Retained archived implementation | 27 archived IDs are absent: 17 remain useful after adaptation and 10 are intentionally superseded. | `planned` |
| Content identity/category health | Current authored choice/tool records | Fourteen gaming-set/instrument choices are duplicated; Navigator's Tools and Thieves' Tools are miscategorized. | `conflicting` |
| Tactical state | D&D encounter/position/turn components | Participation, Size, movement budget, and combat position exist; battlefield topology/passability and side hostility do not. | `planned` |
| Conditions | Active-effect schemas plus current condition mechanics | Definitions, target relationships, timed lifecycle, and related-effect projection are absent; current mechanics still request `dnd2024.conditions`. | `planned` |
| Rests | `dnd2024.exploration.rest` | Start/progress/interruption structure exists; completion, authoritative clock events, and reset-provider closure do not. | `planned` |
| DM/Player table | Information-hub/audience contracts | Shared projections exist; authentication, membership, control, requested view, live transport, and two local-DM-seat expectations are not all closed. | `planned` |
| Location map owner | `dnd2024.game.core.world.map.visual` and `procedure.game.core.world.spatial` | The owner is confirmed in the [scoped-map tree](../../web/DND2024-SCOPED-MAP-VIEWS-DEPENDENCY-TREE.md); asset registration, guarded writes, safe projection, and component-driven rendering remain. | `verified` |
| Live world authoring | `system.world-state.sync` and `IApplicationWorldAuthoringSynchronizer` | Private, replay-safe graph authoring code and focused tests exist; full cross-worktree acceptance remains a separate gate. | `planned` |
| Durable restore/export | Operation history plus evidence-only session checkpoints | Checkpoints do not capture restorable domain state; no complete package listing/download/restore/migration owner exists. | `missing` |

Primary evidence: [canonical component crosswalk](evidence/modeling/canonical-component-crosswalk.json),
[coverage matrix 1B](adoption/evidence/coverage-matrix-1b.json),
[Slice 8 closure](adoption/evidence/slice-8-closure.json),
[complex-family gate map](adoption/evidence/DND-CODE-ADOPTION-SLICE-11-REMAINING-COMPLEX-FAMILY-GATES.md),
[information-hub tree](../../web/DND2024-WEB-INFORMATION-HUB-DEPENDENCY-TREE.md),
[scoped map tree](../../web/DND2024-SCOPED-MAP-VIEWS-DEPENDENCY-TREE.md),
and the linked focused implementation evidence.

## Conflicts and decisions

1. SQLite owns live campaigns/world/events/operations/knowledge. Catalog files own authored
   rules/content. The website and plans own neither.
2. `game.core.*` remains a reusable generic catalog namespace outside the D&D application. Inside
   the installed D&D application every authored identity and dependency reference is explicitly
   `dnd2024.*`, including `dnd2024.game.core.*`. The parallel `dnd2024.world.*` and
   `dnd2024.campaign.*` prototype shapes are migration inputs only, never aliases or new live-write
   owners. The [G7 convergence record](adoption/evidence/complete-campaign-world-campaign-owner-convergence.json)
   captures the confirmed disposition; backup and migration gates still apply before any rewrite.
3. Location hierarchy is containment only. Connections in the D&D application are reviewed
   `dnd2024.game.core.world.location.connected-to` edges; routes are separate directed entities.
4. A country is a `dnd2024.game.core.world.location` with `kind: "region"`. A continent may be a nested
   Region. Settlements, sites, and interiors keep their distinct kinds.
5. Current-style IDs/categories win. Search current owners before adding an ID. Retired tactical
   category branches do not return under a different prefix.
6. C# may add only generic projection, orchestration, event, clock, transaction, and security
   capabilities. D&D decisions/calculations stay in catalog JavaScript.
7. The DM/Player switch requests a permitted projection; it is never authorization. Player
   responses contain no secret records, IDs, counts, text, relationships, or media keys.
8. Each D&D-owned leaf must review the exact SRD 5.2.1 locator and relevant licensed Foundry dnd5e
   flow. Foundry is engineering evidence, not rule authority or a runtime dependency.
9. Authentication, campaign membership, DM/Player permission, player-to-character control, and
   requested projection are five separate facts. Player-preview is a DM-requested Player projection,
   not a third seat or a privilege change.
10. A verified full-state backup/export must exist before the first live owner rewrite, hierarchy
    migration, or old-campaign migration. Evidence checkpoints alone are not restore authority.
11. Campaign creation and the authored D&D catalog both use
    `dnd2024.game.core.campaign.root`; `dnd2024.campaign.root` remains a migration-only prototype
    shape.
12. Duplicate gaming-set/instrument choices and the categories for Navigator's Tools and Thieves'
    Tools are a data conflict. G1 inventories it; C1 performs the confirmed rewrite and alias
    retirement before grants or character creation consume those identities.
13. Retired mechanic owners are never restored wholesale. Each current contract receives one
    disposition: adapt to a current owner, replace with a confirmed missing owner, or document the
    current derived/composed owner that supersedes it.
14. `dnd2024.game.core.world.map.visual`, nested Region containment, and
    `location.thalorien.thalos` are confirmed. TypeScript filename/location tables are the conflicting
    presentation path and are removed only after guarded component authoring and projection work.

## Branch alignment and source gate

| Branch | Alignment | Source/engineering gate |
| --- | --- | --- |
| G2–G9 and generic persistence seams | `ruleset-neutral` | Repository kernel/world contracts; no D&D rule locator or Foundry review applies. |
| G1, W, A, P adapters, and Q orchestration | `dnd2024-compatible` | Existing state/procedure/security owners; they may consume but not redefine rules. |
| C, R, I, S, M, and D rule behavior | `dnd2024-owned` | Each implementation document names an exact `source.dnd2024.srd-5.2.1` locator and records relevant Foundry dnd5e review before becoming ready. |
| Presentation-only UI/media leaves | `dnd2024-compatible` | Authoritative state and audience contracts; no presentation field becomes rule/world authority. |

## Master dependency graph

```text
Run a complete persistent D&D 2024 campaign                                      [planned]
├── G. Integrity and generic host foundations                                     [planned]
│   ├── G1. Owner/ID/category/reference/empty-behavior ledger                      [verified]
│   ├── G2. Related-endpoint and optional-component projection                    [missing]
│   ├── G3. Bounded list/path reference projection                                [missing]
│   ├── G4. Immutable parent/child operation identity reaches JavaScript          [verified]
│   ├── G5. Generic application events, reactions, and timing windows             [missing]
│   ├── G6. Authoritative application/world clock bridge                          [verified]
│   ├── G7. One canonical world/campaign owner set and rewrite plan               [verified]
│   ├── G8. Exact activation/source profile/private-table server lifecycle        [verified]
│   └── G9. Reviewed live application-ECS authoring transaction                   [verified]
├── W. Reusable world, locations, and campaign                                     [planned]
│   ├── W1. World root, location containment, adjacency, and route structures     [verified]
│   ├── W2. World validate/create/select/archive and initial clock/reference      [missing]
│   ├── W3. Thalorien → Thalos → seven country Regions                            [blocked]
│   ├── W4. Location create/edit/archive plus child/occupant/reference policy     [missing]
│   ├── W5. Cross-level gateways plus distinct ground/aerial/water/teleport paths [missing]
│   ├── W6. Location-owned map schema/spatial procedure                           [verified]
│   │   └── asset registry, guarded write, safe projection, component rendering  [missing]
│   ├── W7. Calendar/environment/weather/light/hazards/travel time                [planned]
│   ├── W8. Narrative NPCs, creature links, factions, motives, fronts, knowledge [planned]
│   ├── W9. Campaign validate/create/select bound to an existing world            [conflicting]
│   ├── W10. Chapters/arcs/sessions/recaps/checkpoints                            [planned]
│   └── W11. General quests/rewards/consequences/history/current-view selectors  [planned]
├── A. Authentication, authorization, control, and workspace                      [planned]
│   ├── A1. Private-link authentication and expiry/revocation                     [planned]
│   ├── A2. Campaign invitation/join/leave/membership                             [planned]
│   ├── A3. DM/Player permission independent of player-character control          [planned]
│   ├── A4. Requested DM or Player projection; DM Player-preview mode             [planned]
│   ├── A5. Audience-safe World/Campaign/Party/Current View/Rules envelopes       [planned]
│   ├── A6. Paired world/campaign/location/NPC/quest authoring UI                 [missing]
│   ├── A7. Character builder/sheet/inventory/spellbook/advancement UI            [planned]
│   ├── A8. Player decisions/actions and transparent dice                         [planned]
│   └── A9. Keyboard/mobile/loading/denied/stale/reconnect/conflict states        [planned]
├── C. Character content, creation, control, and advancement                       [planned]
│   ├── C1. Tool/content deduplication and exact category correction              [conflicting]
│   ├── C2. Ability assignment and background increases                          [missing]
│   ├── C3. Bounded grant/choice projection                                       [blocked]
│   ├── C4. Species/background/language/feat/equipment grants                    [missing]
│   ├── C5. Class/subclass progressions, features, and resource providers         [missing]
│   ├── C6. Atomic Fighter MVP character creation                                [blocked]
│   ├── C7. Campaign participation and player-to-character control               [planned]
│   ├── C8. Spell/class-capable full character creation                           [blocked]
│   ├── C9. Atomic advancement: eligibility/choices/grants/HP/resources/spells   [planned]
│   └── C10. Guarded character correction and portable character view            [planned]
├── R. Core D&D rules                                                              [planned]
│   ├── R1. Dice/D20 Tests/checks/saves/proficiency/Initiative                   [planned]
│   ├── R2. Level/AC/HP/Temporary HP/healing/damage defenses                     [planned]
│   ├── R3. Canonical weapon activities, attack, and base damage                 [verified]
│   ├── R4. Condition/Exhaustion definitions and apply/clear/stack/immunity      [missing]
│   ├── R5. Timed/source-linked effect expiry/repeats/suppression                 [blocked]
│   ├── R6. Zero HP/instant death/dying/Death Saves/stable/recovery              [blocked]
│   ├── R7. Rest episode completion/Hit Dice/HP/one reset delegate               [blocked]
│   ├── R8. Complete rest reset-provider coverage                                [blocked]
│   ├── R9. Encounter/Initiative/round/turn/end/cleanup lifecycle                [planned]
│   ├── R10. Action/Bonus Action/reaction/movement refresh and standard actions  [planned]
│   ├── R11. Armor/equipment consequences and hand/slot capacity                [planned]
│   ├── R12. Unarmed/Grapple/Shove/improvised/multiattack                       [missing]
│   ├── R13. Battlefield/sides/placement/movement/range/cover/visibility         [missing]
│   ├── R14. Reactions/opportunity attacks/Ready/interrupt ordering              [blocked]
│   ├── R15. Heroic Inspiration consume/reroll/overflow transfer                 [missing]
│   ├── R16. Travel/jump/fall/suffocation/hazard/chase/environment               [planned]
│   └── R17. Conversation/Influence/Search/Study/Help/Hide/social contests       [planned]
├── I. Items, equipment, economy, magic items, and vehicles                        [planned]
│   ├── I1. Definition/quantity/containment/equipment/burden/capacity            [verified]
│   ├── I2. Transfer/stack/split/consume/activity and currency conservation       [planned]
│   ├── I3. Draw/stow/don/doff/hands/armor/shield consequences                  [planned]
│   ├── I4. Ammo/loading/light/heavy/reach/thrown/mastery                       [planned]
│   ├── I5. Containers/custody/shops/merchants/services/prices                  [planned]
│   ├── I6. Recipe/material/tool structures for crafting                         [verified]
│   ├── I7. Mounts/vehicles/crew/passengers/cargo/travel                         [planned]
│   └── I8. Magic-item attunement/charges/curses/knowledge/activation           [missing]
├── S. Spellcasting                                                                [planned]
│   ├── S1. Spell identity/profile fidelity and executable activities            [planned]
│   ├── S2. Spell-list entitlement, known/prepared changes, casting sources      [missing]
│   ├── S3. Ability/attack/save DC/slots/Pact/upcast/cantrip/multiclass          [missing]
│   ├── S4. Cast transaction: timing/components/focus/free hand/material spend   [missing]
│   ├── S5. Target/range/area/cover/line-of-effect validation                    [missing]
│   ├── S6. Attack/save/damage/heal/condition/movement outcomes                  [missing]
│   ├── S7. Duration ticks/repeat saves/concentration/rituals/reactions          [missing]
│   ├── S8. Summon/create/transform creature outcomes                            [blocked]
│   └── S9. All 339 rule-bearing spell records executable or explicitly excluded [blocked]
├── M. Creatures, monsters, and encounters                                         [planned]
│   ├── M1. Creature/statblock bootstrap and derived state                       [missing]
│   ├── M2. Generic spawn/control/location/participation transaction             [missing]
│   ├── M3. Actions/attacks/saves/recharge/legendary/multiattack                 [missing]
│   ├── M4. Spellcasting-monster adapter                                          [blocked]
│   ├── M5. Encounter builder/sides/placement/difficulty/reinforcements          [missing]
│   ├── M6. Defeat/flee/death/cleanup and world-actor synchronization            [missing]
│   └── M7. All 330 rule-bearing monsters executable or explicitly excluded      [blocked]
├── D. Rewards, quests, downtime, and crafting                                     [planned]
│   ├── D1. Transactional reward/consequence envelope                            [missing]
│   ├── D2. XP/milestone/advancement eligibility                                 [planned]
│   ├── D3. Currency/item/treasure/Inspiration/quest consequences                [missing]
│   ├── D4. Downtime activity time/cost/check/interruption/completion            [missing]
│   ├── D5. Crafting consumes D4 plus recipe/item/tool owners                    [missing]
│   └── D6. Training/services/lifestyle and long-term consequences               [missing]
├── X. Source-complete executable content cohorts                                  [planned]
│   ├── X1. Species/background/class/subclass/feat cohorts                        [blocked]
│   ├── X2. Rule-bearing spell cohorts                                            [blocked]
│   ├── X3. Non-spell monster cohorts                                             [blocked]
│   ├── X4. Spellcasting-monster cohorts                                          [blocked]
│   └── X5. Magic-item cohorts                                                    [blocked]
├── P. Persistence, backup, export, migration, and recovery                        [planned]
│   ├── P1. Full-state capture/restore owner; checkpoints are evidence only      [missing]
│   ├── P2. Operation-backed autosave and crash/restart durability               [missing]
│   ├── P3. Immutable pre-migration backup with integrity/access/retention       [blocked]
│   ├── P4. Portable package with state/source/code/schema/license manifest      [missing]
│   ├── P5. Conflict-aware import/version migration preserving newer state       [planned]
│   ├── P6. Reviewed old-D&D world/campaign/content migration                    [planned]
│   └── P7. Replay/rollback/list/download/restore/recovery drill                 [planned]
└── Q. Full acceptance                                                             [blocked]
    ├── Q1. Catalog/schema/source/license/category/reference validation           [planned]
    ├── Q2. Fresh world/campaign/character creation                              [planned]
    ├── Q3. DM and Player end-to-end campaign session                            [planned]
    ├── Q4. Determinism/replay/stale/no-change/rollback/secret canaries           [planned]
    ├── Q5. Backup/migration/export/import/restart/resume drill                   [planned]
    └── Q6. Source-complete coverage ledger and explicit exclusions              [planned]
```

## Critical cross-branch edges

| Consumer | Provider | Reason |
| --- | --- | --- |
| R4 Condition definitions/instances | G2, G4 | Definitions resolve through related endpoints; newly created effect instances record immutable operation provenance. |
| C3–C5 grants and choices | G3 | Grant/choice facets contain bounded arrays and nested references; they do not need campaign creation. |
| R5–R8, R14, S7, M3, D4 | G5 | Damage/healing/turn/cast/rest/recharge/downtime reactions need one deterministic event/timing seam. |
| W7, R5–R8, R16, S7, D4–D6 | G6 | Elapsed time and expiry come from one authoritative clock coordinate/revision. |
| W2–W5, W8–W11 | G7, G9 | Owner convergence precedes guarded live graph authoring; a public action must transact every mutation. |
| W3 and P6 live migration | P3 | The hierarchy/owner rewrite cannot begin before a restorable immutable backup exists. |
| A2–A5 and C7 | A1, W9 | Membership, permission, control, and requested projection bind to an authenticated exact campaign. |
| R13–R14 and S5–S7 | R9–R10 | Spatial/timing consumers require explicit encounter, turn, action, and movement state. |
| S8 | M1–M2 | Summons/transforms reuse the generic creature bootstrap/spawn owner; monsters do not depend on spellcasting. |
| M4 | S1–S7, M1 | Only spellcasting monsters depend on the cast/effect vertical; non-spell monsters remain independent. |
| R8 complete rest closure | C5, I8, S2–S3, R7 | The first rest vertical proves one delegate; complete closure waits for all class, spell, and magic-item reset providers. |
| C9 advancement | D2, C2–C5 | XP/milestones expose eligibility; one advancement transaction resolves choices and recalculates owned resources. |

## World and location plan

The confirmed target hierarchy is:

```text
world.thalorien                                                World root
└── location.thalorien.thalos                                 Region (continent)
    ├── location.thalorien.aldros                             Region (country)
    │   └── location.thalorien.crownmere                      Settlement
    ├── location.thalorien.evandos                            Region (country)
    ├── location.thalorien.minevros                           Region (country)
    ├── location.thalorien.valeros                            Region (country)
    │   └── location.thalorien.brackenford                    Settlement
    ├── location.thalorien.rhiannos                           Region (country)
    ├── location.thalorien.merceros                           Region (country)
    │   └── location.thalorien.merrowgate                     Settlement
    └── location.thalorien.waylos                             Region (country)
```

Preserve existing country IDs. First validate or create/select the owning world, initialize its
clock/reference state, and capture a restorable pre-migration package. Create Thalos only through
the accepted live-authoring action, then move all seven countries beneath it atomically. Materialize
other known places, including Elaris and Kharad Veyr, only from reviewed canonical facts. A
settlement may contain sites; a site may contain interiors. Actors/objects use containment slot
`presence`.

Connections remain separate from nesting:

- `dnd2024.game.core.world.location.connected-to` records reviewed undirected adjacency once;
- route entities own directed travel, world scope, availability, and duration/distance;
- on-foot/ground, aerial, water, and teleport paths remain distinct owners when their admission,
  traversal, interruption, or consequences differ;
- an explicit gateway/cross-level transition lets an occupant leave a site/settlement, cross a
  parent Region boundary, and enter another descendant; the current sibling-only walking rule is
  not silently stretched to nested travel;
- faction alliance/opposition/control and political borders do not reuse travel adjacency; and
- map anchors or art never imply reachability, distance, discovery, or travel.

| World authoring leaf | State | Exit gate |
| --- | --- | --- |
| Canonical owner convergence | `conflicting` | One qualified world/campaign owner set and one campaign-create transaction replace every parallel shape before a write. |
| Live graph authoring action | `missing` | Idempotently creates/corrects entities, components, containment, relationships; validates archetypes and rolls back fully. |
| World lifecycle | `blocked` | Authorized validate/create/select/archive owns initial references/clock; archive handles campaigns, locations, occupants, routes, media, and current pointers without orphans. |
| Country/continent migration | `blocked` | Thalos and seven preserved countries read back at exact depth; no duplicate/orphan Region remains. |
| Location CRUD/connection/gateway editor | `blocked` | DM creates/edits/archives locations, connections, routes, and cross-level gateways without SQL/raw JSON; archival has explicit child/occupant/reference behavior. |
| Location maps/media | `planned` | Verified generic visual owner selects bounded Player/DM variants; missing Player media leaks no DM key/URL/count and TypeScript location tables are gone. |
| NPC/faction/knowledge authoring | `blocked` | Narrative identity/motive/location is distinct from an optional executable creature/statblock link; Player bytes omit secret entities and relationships. |
| Campaign/session/quest authoring | `blocked` | Campaign creation needs only a selected world; chapter/arc/session/checkpoint and general objective/reward/consequence transitions are separate actions. |
| Chronology/visited places | `missing` | World history and campaign history are distinct explicit owners, not prose/event-log inference. |

## Character and content plan

1. Retain one tool identity per normalized SRD tool/category, rewrite references, correct Navigator's
   Tools and Thieves' Tools, then retire duplicate aliases. Tests reject two active choices for one
   normalized name/category.
2. Add a current ability-assignment owner (candidate
   `dnd2024.advancement.ability-assignment`) for fixed multiset and point budget. Seeded random
   generation remains a separate mechanic.
3. Add bounded generic path/list reference projection with depth/count/component limits, exact
   revisions, and fail-closed malformed/missing/duplicate targets.
4. Reuse species/background/class/progression/choice/grant schemas. Add only grant vocabulary in
   use: `feature`, `feat`, `proficiency`, `ability-score-increase`, `item`. Grant IDs follow
   `dnd2024.grant.<owner>.<subject>`.
5. Populate the accepted first vertical: Human Skillful/Versatile/Resourceful, Skilled, four
   backgrounds, Fighter levels 1–2, and exact MVP choices. Expand species/background/class/feat
   behavior in independent cited cohorts; this content authoring does not depend on campaign setup.
6. Rewrite `character.basic.create` only after the Fighter MVP grants resolve. One transaction owns
   identity, provenance, origins, class membership, Hit Dice, choices, entitlements, starting
   inventory, and currency. Campaign participation/control is a separate transaction because a
   character may be created before it joins a campaign.
7. Add full class-capable creation after spell entitlements/resources and every chosen class's
   level-1 providers exist. It extends the same transaction shape rather than creating a second
   creator.
8. Give advancement its own transaction: validate eligibility; resolve level/multiclass choices;
   apply grants; recompute HP, Hit Dice, class resources, and spell entitlements; then commit once.
   XP/milestone rewards expose eligibility but do not perform advancement. Respec is not core SRD
   acceptance and remains outside this graph unless separately sourced/confirmed.
9. Retire `character-content-definition.record`; source/version/archetype facets own definitions.
   Fix `character-level.read` to accept current `dnd2024.class.*` identities only.

## Mechanic repair inventory

### Active contracts still requiring current owners

| Family | Mechanics | Disposition |
| --- | --- | --- |
| Conditions | `conditions.write`, `d20-test.state-effects`, `turn-budget.spend` | Adapt to related active-effect entities. |
| Character | `character-abilities.resolve`, `character.basic.create`, `species-skillful.resolve`, `species-versatile-skilled.resolve` | Adapt after ability/grant projection/content. |
| Content recorder | `character-content-definition.record` | Retire; current facets supersede it. |
| Items | `item-activity.use`, `item.transfer` | Adapt to activity facets and definition-link/quantity/equipment/container. |
| Rest | `rest.begin`, `rest.progress`, `rest.interrupt` | Adapt to entity-addressable `dnd2024.exploration.rest`. |

### Useful archived mechanics to adapt

- `armor-equipment.read`, `zero-hit-points-policy.write`, `dying.on-damage`,
  `unarmed-strike.damage`;
- `encounter-space.read/write`, `encounter-position.write`,
  `encounter-participant-tactical-state.read`, `encounter-participant-movement-state.read`;
- `encounter-sides.write/relation`;
- `tactical-move.path/budget-input/execute`;
- `melee-reach.check`, `tactical-melee.admit/attack`.

### Archived mechanics intentionally superseded

- `abilities.record` → current ability owner/composed creation;
- `armor-class.write` → derived Armor Class;
- `background-ability-increases.resolve` and
  `character-ability-assignment-policy.validate` → assignment/choice/grant composition;
- `character-level.record` → class memberships plus derived total;
- `conditions.guard` → active-effect validation/consumers;
- `damage-mitigation.write` → `dnd2024.mechanic.creature.defenses.write`;
- `melee-reach.write` → activity range plus derived unarmed reach;
- `origin-languages.resolve` → choices/grants/language writer;
- `rest.clock-reconcile` → clock-derived activity-classified progress.

## ID and category ledger

| Capability | Existing/proposed owner | Decision |
| --- | --- | --- |
| World/location | Authored and runtime `dnd2024.game.core.world.root/location` | Preserve `world.thalorien` and country IDs. The installed D&D types are canonical; `dnd2024.world.root` is migration-only and not an alias. |
| Campaign | Authored and runtime `dnd2024.game.core.campaign.root` | The installed D&D type is canonical; `dnd2024.campaign.root` is migration-only and no public/live write may use it. |
| Location adjacency | Existing `dnd2024.game.core.world.location.connected-to` | No D&D duplicate or embedded connection list. |
| Condition definitions | Candidate `dnd2024.effect.condition.{blinded,charmed,deafened,frightened,grappled,incapacitated,invisible,paralyzed,petrified,poisoned,prone,restrained,stunned,unconscious,exhaustion}` | Immutable rule definitions use exact `dnd2024.ruleset.core.state.conditions`; active-effect instance entities remain the only applied-condition state. |
| Active-effect target | Candidate manifest-local kind `effect.active-for-target`, whose sole runtime-qualified identity is `dnd2024.effect.active-for-target` | The local key and qualified kind are one identity at two representation layers, never two peer registrations; active-effect entity → target, with no creature-attached list. |
| Rest graph | Existing `dnd2024.exploration.rest`; candidate runtime kinds `dnd2024.exploration.has-rest` and `dnd2024.exploration.rest.for-actor` | Use exact category `dnd2024.ruleset.core.gameplay.rest`; candidate mechanics are `dnd2024.mechanic.rest.finish` and `dnd2024.mechanic.rest.hit-die.spend`, each separately confirmed. |
| Battlefield | Candidate `dnd2024.combat.battlefield`, `dnd2024.archetype.combat-battlefield`, and qualified kinds `dnd2024.encounter.has-battlefield`, `dnd2024.encounter.has-side`, `dnd2024.encounter.side.hostile-to` | Candidate categories awaiting leaf confirmation are `dnd2024.ruleset.core.combat.movement` and `dnd2024.ruleset.core.combat.relationships`; reach/melee reuse `dnd2024.ruleset.core.gameplay.weapon-attacks`. |
| Character assignment/grants | Candidate `dnd2024.advancement.ability-assignment`; `dnd2024.grant.<owner>.<subject>` | Reuse exact `dnd2024.ruleset.character.creation.abilities`, `dnd2024.ruleset.character.creation.basic`, and `dnd2024.ruleset.character.advancement`; no `content.*.v1` aliases. |
| Zero HP | Candidate `dnd2024.creature.zero-hit-point-behavior` | Use exact `dnd2024.ruleset.core.gameplay.dying`; distinguish Death Saves/die-at-zero without inferring creature kind. |
| D&D events | Candidate `dnd2024.combat.initiative.rolled`, `dnd2024.combat.damage.taken`, `dnd2024.spellcasting.spell.cast`, `dnd2024.exploration.rest.completed` | Payload/timing contracts require separate confirmation. |
| Map visuals | Confirmed existing `dnd2024.game.core.world.map.visual` | Presentation only; no route/knowledge/geometry authority. TypeScript filename tables are not a second owner. |

## Tactical and rest ordering

Tactical combat first closes the encounter/Initiative/round/turn/end lifecycle, including Initiative
ties; action, Bonus Action, reaction, and movement refresh; late joins; defeat/flee/removal; encounter
end; and synchronization back to the world actor. Battlefield/side state, placement, and movement
can then proceed independently of Conditions and armor except where a named rule effect consumes
them. Reach/admission reuses verified weapons. Use 2.5-foot internal cells (`381/500` metre) so Tiny
through Gargantuan footprints are exact; a normal step moves two cells and spends 5 feet
(`381/250` metre). Cover, visibility, line of effect, ranged distance, opportunity attacks, forced
movement, teleportation, special speeds, jumping, mounts, Grapple/Shove, and multiattack remain
explicit separate leaves.

Rest first extends `dnd2024.exploration.rest` with type/status, immutable clock coordinates,
sleep/light/exertion minutes, interruptions, and start/end references. Begin/progress/interrupt then
consume authoritative time. `rest.finish`, Hit Dice spending, HP/Hit Die recovery, Exhaustion
reduction, and resource-reset delegates commit together. Rest JavaScript never knows every class,
spell-slot, or magic-item ID.

That first rest completion proves one reset delegate only. Full rest acceptance is a later closure
after all selected class/subclass resources, spell-slot/preparation resources, and magic-item charge
providers register their own reset behavior; the rest coordinator remains data-driven.

## Ordered leaves (topological groups, not a waterfall)

IDs below are the same nodes used by the master graph. A row depends only on the named direct
providers; siblings without an edge may be planned or implemented in parallel. A verified provider
does not reappear as work.

### Foundation and safety seams

| ID | Leaf | State | Direct providers | Exit gate |
| --- | --- | --- | --- | --- |
| G1 | Owner/ID/category/reference/empty-behavior ledger | `ready` | — | Deterministic machine ledger closes duplicate/retired/empty-owner inventory with no runtime effects. |
| G8 | Stable activation/source-profile/private-table context | `verified` | G1 | Restart/refresh retains the exact campaign binding and both local-DM-seat tests pass. |
| G2 | Related-endpoint and optional-component projection | `planned` | G1 | Exact revision, endpoint, optionality, limit, and fail-closed tests pass generically. |
| G3 | Bounded list/path reference projection | `planned` | G1 | Depth/count/component limits and malformed/missing/duplicate/stale reference tests pass. |
| G4 | Parent/child operation identity into JavaScript | `verified` | G1 | Host-issued immutable IDs reach JavaScript and typed effects; callers cannot forge future IDs. |
| G5 | Generic deterministic event/reaction/timing seam | `blocked` | G4 | Ordered event envelopes, windows, once-only reactions, replay, and rollback pass with no D&D vocabulary in C#. |
| G6 | Application/world clock bridge | `verified` | G1 | One coordinate/revision drives elapsed-time consumers and rejects stale or backward updates. |
| G7 | Canonical world/campaign owner convergence decision | `verified` | G1 | `dnd2024.game.core.*` is the sole runtime owner; prototype migration inputs have explicit dispositions. |
| G7N | D&D application namespace containment | `implemented` | G7 | Every current D&D application identity/reference begins `dnd2024.`; generic catalogs remain outside the application; no compatibility aliases or live writes occur. Full feature acceptance waits on the separately recorded D&D suite blockers. |
| P1 | Full-state capture/restore owner | `missing` | G1, G4 | A complete authorized domain snapshot restores exactly; evidence checkpoints remain evidence-only. |
| P3 | Immutable pre-migration backup | `blocked` | G7N, P1 | Integrity/access/retention/list/download/restore proof exists before any live rewrite. |
| G9 | Authorized live application-ECS authoring | `verified` | G4, G7N, G8 | Private-operator protocol authorization, bounded root scope, exact revisions, typed atomic effects, replay/conflict, and rollback acceptance pass. |

### World, campaign, access, and their paired UI

| ID | Leaf | State | Direct providers | Exit gate |
| --- | --- | --- | --- | --- |
| W2 | World validate/create/select/archive | `blocked` | G7, G9 | World lifecycle owns initial clock/reference state and guarded archival consequences. |
| W9 | Campaign validate/create/select | `blocked` | G7, G9, W2 | Campaign binds one selected world/source profile; country migration is not a prerequisite. |
| A1 | Private-link authentication/expiry/revocation | `planned` | G8, W9 | Valid links authenticate one campaign; expired/revoked/wrong-campaign links fail without state change. |
| A2 | Invitation/join/leave/membership | `blocked` | A1, W9 | Membership is server-owned and survives reconnect; removal revokes subsequent access. |
| A3 | DM/Player permission and character control | `blocked` | A2 | Role and actor-control edges are independent, auditable, and non-elevating. |
| A4 | Requested view and DM Player-preview | `blocked` | A3 | Preview returns the exact Player projection without changing permission or creating a third seat. |
| A5 | Audience-safe information envelopes | `blocked` | A4 | Player bytes omit secret records, IDs, counts, relationships, text, and media keys. |
| W3 | Thalorien/Thalos/seven-country migration | `blocked` | G9, P3, W2 | Confirmed IDs read at exact depth with no duplicate/orphan Region. |
| W4 | Location lifecycle, connections, and gateways | `blocked` | G9, W2 | CRUD/archive/reference policy and nested cross-level travel topology pass without raw SQL/JSON. |
| W5 | Distinct ground/aerial/water/teleport traversal | `blocked` | G6, W4 | Each route/gate validates admission, time, interruption, arrival, replay, and consequences. |
| W6a | Map asset registry/write/projection/rendering | `blocked` | A5, G9, W4, W6 | Verified component selects Player/DM assets; missing Player variant leaks nothing; filename tables are removed. |
| W7 | Calendar/environment/weather/light/hazards | `blocked` | G6, W4, W5 | Authoritative time/environment transitions drive travel and hazard inputs deterministically. |
| W8 | Narrative NPC/faction/knowledge authoring | `blocked` | A5, G9, W4 | Narrative NPCs may link to creatures without becoming duplicate statblocks; secret projections fail closed. |
| W10 | Chapter/arc/session/recap/checkpoint lifecycle | `blocked` | A3, G5, G6, W9 | Session actions are distinct, replayable, and checkpoint evidence is not restore authority. |
| W11 | Quest/objective/reward/consequence/history/current selectors | `blocked` | W8, W9, W10 | General objective counts/transitions and world versus campaign chronology are explicit. |
| A6a | World/location/map authoring UI | `blocked` | A5, W3, W4, W6a | DM performs the vertical without raw IDs/JSON; Player sees only safe projections. |
| A6b | Campaign/session/NPC/quest authoring UI | `blocked` | A5, W8–W11 | Each backend action has a usable optimistic/stale/error/reconnect UI state. |

### Characters, rules, and items

| ID | Leaf | State | Direct providers | Exit gate |
| --- | --- | --- | --- | --- |
| C1 | Tool identity/category correction | `conflicting` | G1 | One normalized SRD identity/category survives; references rewrite atomically; aliases are retired deliberately. |
| C2 | Ability assignment/background increases | `planned` | G3 | Fixed multiset and point budget derive legal assignments; seeded generation stays separate. |
| C3 | Bounded grant/choice projection | `blocked` | G3 | Current grant vocabulary resolves exact active definitions and fails closed at every bound. |
| C4 | First species/background/language/feat/equipment grants | `blocked` | C1–C3 | Accepted Human/background/Skilled cohort grants executable entitlements with no duplicate identities. |
| C5 | Fighter progression/features/resources | `blocked` | C3–C4 | Levels 1–2 resolve features, proficiencies, Hit Dice, choices, and resources from current definitions. |
| C6 | Atomic Fighter MVP character creation | `blocked` | G4, C1–C5, I1 | Actor/origin/class/choices/inventory/currency commit once or not at all; no campaign is required. |
| C7 | Campaign participation/player control | `blocked` | A3, C6, W9 | Existing character joins one campaign and receives only explicitly authorized controllers. |
| R1 | D20/check/save/proficiency/Initiative closure | `planned` | G1 | Current owners cover edge cases and no active contract requests retired state. |
| R2 | Level/AC/HP/Temporary HP/healing/defense closure | `planned` | G1, R1 | Derived AC/level stay derived; damage/healing/Temporary HP and defenses compose without duplicate totals. |
| R4 | Condition/Exhaustion definitions and instance lifecycle basics | `blocked` | G2, G4 | Definitions stay immutable; apply/clear/stack/immunity/multiple-source behavior uses active-effect instances. |
| R5 | Timed/source-linked effect lifecycle | `blocked` | G5, G6, R4 | Expiry, repeat saves, turn boundaries, suppression, and source loss are deterministic. |
| R9 | Encounter/Initiative/round/turn/end lifecycle | `blocked` | G5, G6, R1 | Ties, joins, refresh, defeat/flee/remove, end/cleanup, and world synchronization pass. |
| R10 | Standard action repertoire and per-turn economy | `blocked` | R9 | Action, Bonus Action, reaction, movement, Dash, Disengage, Dodge, Help, Hide, Ready, Search, Study, and Influence are executable. |
| R7 | Rest episode finish/Hit Dice/HP/one reset delegate | `blocked` | G4–G6, R2, R4 | Start/interruption/finish/recovery commit atomically for one data-driven reset provider. |
| R6 | Zero HP/dying/Death Saves/death/recovery | `blocked` | G5, G6, R2, R4, R9 | Damage through instant death, saves, stability, healing, and stable recovery is event-driven. |
| I2 | Transfer/stack/split/consume/activity/currency | `blocked` | G2, G4, I1 | Quantity/custody/currency conservation and stale/no-change paths pass. |
| I3 | Draw/stow/don/doff/hands/armor/shield | `blocked` | I2, R2, R10 | Timing and AC/Strength/Stealth consequences derive from equipped state. |
| R13 | Battlefield/sides/placement/movement/range/cover/visibility | `blocked` | G4, R9, R10 | Topology, footprints, hostility, budgets, reach/range, cover, line of effect, replay, and rollback pass. |
| R12 | Unarmed/Grapple/Shove/improvised/multiattack | `blocked` | R3, R10, R13 | Each special attack has explicit admission, outcome, and action-cost behavior. |
| I4 | Ammo/loading/light/heavy/hands/reach/thrown/mastery | `blocked` | I2–I3, R3, R10, R13 | Every property modifies declared activities/state without embedding a second attack system. |
| R14 | Reactions/opportunity attacks/Ready/interrupt ordering | `blocked` | G5, R9–R10, R13 | Windows, eligibility, reservation, trigger, decline, expiration, and once-only effects pass. |
| R15 | Heroic Inspiration reroll/overflow transfer | `blocked` | G4, R1 | Grant, cap/overflow, consume-before-reroll, transfer, replay, and no-change failures pass. |
| R16 | Exploration/travel/jump/fall/suffocation/hazard/chase | `blocked` | R1–R2, R6, R10, W5, W7 | Time, checks, damage, movement, and consequences compose without UI authority. |
| R17 | Conversation/Influence/Search/Study/Help/Hide/social contests | `blocked` | R1, R10, W8 | Attitude/knowledge/action/check changes persist with correct audience and failure behavior. |
| I5 | Containers/shops/merchants/services/prices | `blocked` | I2, W4, W8 | Capacity, custody, purchase/sale, price, and stock transitions conserve items/currency. |
| I7 | Mounts/vehicles/crew/passengers/cargo/travel | `blocked` | I2, R13, W5 | Control, capacity, movement, damage, crew/passenger, and arrival state compose. |

### Spells, monsters, rewards, downtime, and content cohorts

| ID | Leaf | State | Direct providers | Exit gate |
| --- | --- | --- | --- | --- |
| S1 | Spell profile/activity fidelity | `planned` | G1 | Rule-bearing spell records have exact activities/targets/components/duration metadata or an explicit exclusion. |
| S2 | Lists/grants/known/prepared/casting sources | `blocked` | C3–C5, G3, S1 | Entitlements and preparation changes resolve from class/feature definitions. |
| S3 | Ability/attack/save DC/slots/Pact/upcast/cantrip/multiclass | `blocked` | C5, S2 | Resources and scaling derive from exact casting sources and levels. |
| S4 | Cast transaction and components/material spend | `blocked` | G4, I2, R9–R10, S2–S3 | Timing, focus/free hand, costly/consumed material, slot, and action spend commit once. |
| S5 | Target/range/area/cover/line-of-effect | `blocked` | R13, S4 | Legal target/area membership and spatial admission are authoritative and boundary-tested. |
| S6 | Attack/save/damage/heal/condition/movement outcomes | `blocked` | R2, R4, R12–R13, S5 | Typed outcomes delegate to current owners and never accept caller-derived results. |
| S7 | Duration/repeat save/concentration/ritual/reaction | `blocked` | G5–G6, R5, R14, S6 | Ticks, interruption, concentration termination, repeats, rituals, and reactions replay exactly. |
| M1 | Creature/statblock bootstrap | `blocked` | G3, R1–R2 | Definition produces complete derived creature state without campaign/spell dependency. |
| M2 | Generic spawn/control/location/participation | `blocked` | G4, G9, M1, R9, W4 | Creature placement/control joins world/encounter atomically and rolls back fully. |
| M3 | Non-spell monster action repertoire | `blocked` | M1, R3, R6, R10, R12–R13 | One non-caster resolves actions/recharge/multiattack without spellcasting. |
| S8 | Summon/create/transform creature outcomes | `blocked` | M1–M2, S7 | Spell-created/transformed actors reuse bootstrap/spawn and own cleanup/duration. |
| M4 | Spellcasting-monster adapter | `blocked` | M1, S1–S7 | Only caster monsters bind statblock spell sources to the canonical cast transaction. |
| M5 | Encounter builder/sides/placement/difficulty/reinforcements | `blocked` | M2–M3, R9, R13, W11 | One non-spell encounter can be prepared and run; caster support is optional. |
| D1 | Reward/consequence transaction | `blocked` | G4, W11 | Declared recipients/sources commit rewards and quest/world consequences exactly once. |
| D2 | XP/milestone/advancement eligibility | `blocked` | C5, D1 | Awards update owned progress and expose eligibility without silently leveling. |
| D3 | Currency/item/treasure/Inspiration/quest rewards | `blocked` | D1, I2, R15, W11 | Mixed rewards conserve resources and preserve audience/provenance. |
| M6 | Defeat/flee/death/cleanup/world synchronization | `blocked` | D1, M2–M3, M5, R6 | Encounter cleanup and consequences commit once; non-casters stay independent of M4 spellcasting. |
| C9 | Atomic advancement/full class recalculation | `blocked` | C5, D2, S2–S3 | Eligibility, choices, grants, HP, Hit Dice, resources, and spells commit once or not at all. |
| C8 | Spell/class-capable full character creation | `blocked` | C5–C6, S2–S4 | The canonical creator supports every selected class without a second transaction shape. |
| R8 | Complete rest reset-provider coverage | `blocked` | C5, I8, R7, S2–S3 | Every selected class/spell/magic-item reset provider participates data-first. |
| I8 | Magic-item lifecycle | `blocked` | I2, R4–R5, R7, S7 | Attunement, charges, curses, identification, activation, and resets use current owners. |
| D4 | Downtime activity lifecycle | `blocked` | G5–G6, I2, W9 | Time/cost/check/interruption/completion/consequence is one replayable transaction family. |
| D5 | Crafting | `blocked` | D4, I2, I6 | Recipes validate tools/materials/time and conserve ingredients/output. |
| D6 | Training/services/lifestyle | `blocked` | D4, I5 | Costs, providers, time, interruption, completion, and consequences are explicit. |
| X1 | Character content cohorts | `blocked` | C4–C5, C9 | Each species/background/class/subclass/feat record is executable or explicitly excluded. |
| X2 | Spell cohorts | `blocked` | S1–S8 | Every rule-bearing spell is executable; presentation-only is not used to mask missing rules. |
| X3 | Non-spell monster cohorts | `blocked` | M1–M3, M5–M6 | Non-casters are executable independently of spellcasting. |
| X4 | Spellcasting-monster cohorts | `blocked` | M4, X2–X3 | Caster monsters reuse exact spell owners and cite both statblock and spell evidence. |
| X5 | Magic-item cohorts | `blocked` | I8 | Every rule-bearing magic item is executable or explicitly excluded. |
| A7 | Character/inventory/spellbook/advancement UI | `blocked` | A5, C6–C9, I2, S2 | Each accepted character vertical works without raw IDs/JSON. |
| A8a | Noncombat decisions/actions UI | `blocked` | A5, R16–R17, W11 | Player decisions, dice, results, consequences, stale/reconnect behavior are transparent. |
| A8b | Combat/spell/monster UI | `blocked` | A5, M5–M6, R9–R14, S7 | The table runs one encounter for DM and Player with no secret leakage or client authority. |

### Persistence adapters, migrations, and final acceptance

| ID | Leaf | State | Direct providers | Exit gate |
| --- | --- | --- | --- | --- |
| P2 | Autosave and crash/restart durability | `blocked` | G4, P1 | Committed operations survive process restart; incomplete work does not. |
| P4 | Portable package envelope/manifest | `blocked` | P1 | Package lists/downloads exact state, source, code, schema, license, and integrity metadata. |
| P4W | World/campaign package adapter | `blocked` | P4, W2–W11, W6a | Export/import round-trip preserves topology, media, knowledge, campaign/session/quest state, and clocks. |
| P4C | Character/rule/content package adapter | `blocked` | C8–C9, I8, M6, P4, S8 | Export/import round-trip preserves actors, inventories, effects, spells, creatures, and provenance. |
| P5 | Conflict-aware import/version migration core | `blocked` | P4 | Dry run, version mapping, newer-state preservation, authorization, rollback, and audit pass. |
| P6W | Old world/campaign migration | `blocked` | G7, P3, P4W, P5, W2–W11, W6a | One reviewed old package migrates once with no duplicate/orphan/secret loss. |
| P6C | Old character/content migration | `blocked` | P3, P4C, P5, X1–X5 | Retired IDs map to accepted owners; unknown/excluded content is reported, never guessed. |
| C10 | Character package adapter/guarded correction | `blocked` | C9, G4, P4–P5 | Authorized corrections and character round-trips preserve provenance and reject stale changes. |
| P7 | Replay/rollback/list/download/restore/recovery drill | `blocked` | P2, P5, P6C, P6W | Backup through crash/import/migration/restore/resume is reproducible and access-controlled. |
| Q1–Q6 | Full campaign acceptance | `blocked` | A6a–A8b, C10, D3–D6, I7–I8, P7, R8, X1–X5 | Fresh setup through export/resume passes catalog, unit, integration, browser, security, determinism, rollback, and source-coverage gates. |

## Acceptance matrix

- **Positive:** intended DM/player action succeeds from authoritative state.
- **Negative/no-change:** malformed, missing, duplicate, stale, wrong-world/campaign/seat, unauthorized,
  and out-of-order input returns no partial effects.
- **Boundary:** levels, distances, counts, durations, footprints, ranges, recursion, references, and
  content sizes are explicit.
- **Determinism/replay:** equal projection/input/seed/code is equal; one operation commits once.
- **Rollback:** a forced mid-transaction failure leaves components, relationships, containment,
  events, and operation state unchanged.
- **Audience:** secret text/IDs/counts/media/relationship existence are absent from Player bytes.
- **Source/content:** exact citation, current category, unique ID, schema/reference/license/profile pass.
- **Compatibility:** current behavior remains; retired authorities do not return; optional/homebrew
  behavior is source-isolated.
- **Product:** keyboard/mobile/accessibility and loading/empty/denied/stale/disconnected states pass.

## Confirmation gates

The user confirmed on 2026-08-30 that missing useful D&D features should receive current-style IDs,
countries are Region locations, and categories/identities must not duplicate. That confirms this
graph's direction, `location.thalorien.thalos`, nested Region containment, and
`dnd2024.game.core.world.map.visual`. Planning creates no runtime authority.

Implementation documents still require exact confirmation for candidate schema/ID meanings,
generic public projection/event/timing contracts, duplicate-content retirement/migration, live ECS
authoring and live mutations, canonical world/campaign convergence, NPC/chronology/current-scene
owners, public hosting or cloud sync,
compatibility/homebrew sources, and completed feature/full-campaign acceptance.

## Completed prerequisite and next gate

G1 is accepted in the [owner-ledger receipt](adoption/evidence/DND2024-COMPLETE-CAMPAIGN-G1-OWNER-LEDGER-RECEIPT.md)
and its [receipt](adoption/evidence/DND2024-COMPLETE-CAMPAIGN-G1-OWNER-LEDGER-RECEIPT.md). It records
the complete deterministic input fingerprint, 13 active mechanics whose contracts retain retired
owners, the 14 duplicate tool identity groups, two category anomalies, and the conflicting qualified
world/campaign owner shapes.

G7 is verified: the live D&D owner is `dnd2024.game.core.*`, and prototype roots are migration-only
inputs. G7N is the active identity cutover: current authored D&D application records and references
must use explicit `dnd2024.*` identities. G9/G8 and the immutable backup gate still prevent live
world authoring, Thalos creation, or country migration.

## Planning receipt

- Runtime artifacts created by this graph: **none**.
- Live database/campaign/world/source profile/deployment/browser state changed: **none**.
- G1 added only catalog-integrity evidence and a focused regression test; it changed no runtime
  owner or live state.
- The weapon-activity implementation completed before the planning pivot remains implemented
  evidence; this graph does not authorize further runtime work.
- Future work proceeds through one implementation document and one reviewable leaf at a time.

# Feature 35 dependency plan — monsters, stat blocks, and encounter assembly

Status: **Planned; Slice 1 is an immutable, source-cited monster-identity catalog and is the next
and only authorised implementation pass.**
Last updated: 2026-08-21

## Execution rule

This is planning only. It creates no runtime procedure, component, entity, mechanic, fixture,
migration, action, event, subscription, campaign state, or encounter creature. A later
implementation pass re-reads the current source registry, Feature 2/6/12/15–17/20–21/23–24/
32–34 contracts, zero-HP policy, encounter-order/turn contracts, and catalog conventions;
confirms every permanent ID; validates a disposable catalog import; records a receipt; and stops
after one accepted slice.

## Target capability

A GM can select a source-cited monster stat block and, in later slices, create a distinct creature
for an encounter whose canonical base state and declared capabilities come from that stat block
through their existing rule owners; CR can inform an encounter diagnostic without directly awarding
XP or deciding campaign outcomes.

### Included

- Versioned immutable monster identity, later stat-block profile, and a small source-cited initial
  content set.
- Static creature details, CR, printed combat statistics, senses/languages/gear declarations, and
  named trait/action/bonus-action/reaction/legendary-action declarations separated from an actor.
- Later monster-specific readers for CR/PB, printed Initiative, skill/save modifiers, and attack
  declarations where the character-only readers cannot represent a published stat block.
- Later controlled actor bootstrap that composes with the owners of ability scores, HP, Armor
  Class, Size, Speed, zero-HP policy, conditions, mitigation, senses, inventory, and encounter
  membership.
- Later declared-action admission and encounter composition/diagnostic paths, each consuming
  trusted tactical, turn, visibility, and consequence results.

### Excluded

- The full SRD bestiary, homebrew-stat-block editor, natural-language rule scripts, or a generic
  “do whatever this trait says” interpreter.
- A second creature, character, NPC, class, proficiency, HP, Armor Class, condition, inventory,
  position, map, turn, encounter-side, or death-state model.
- Automatic NPC creation, player-character creation, campaign participation, GM/player authority,
  campaign encounter persistence, quest placement, loot/recoverable-gear decision, or narration.
- Item creation/custody, player XP award, character advancement, monster harvesting, treasure
  hoards, summon control, polymorph replacement, spell casting, recharge timing, and resurrection.
- Treating a monster label, creature type, CR, alignment, size, or stat-block existence as the
  Feature-17 zero-HP/death policy.

## Official source basis

The fixed source is `source.dnd2024.srd-5.2.1`, *System Reference Document 5.2.1* (Wizards of the
Coast LLC, 2025-05-01, CC-BY-4.0): [Monsters > Stat Block Overview, Monster Statistics, and
Running a Monster, PDF pp. 250–253](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf),
[Rules Glossary > Challenge Rating, PDF p. 177](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf),
and [Gameplay Toolbox > Encounter Difficulty, PDF pp. 200–202](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf).

- A stat block distinguishes immutable general details, defensive/offensive statistics, senses,
  languages, CR, traits, and action families. Creature types themselves have no rules of their
  own; a type must not be used as a hidden rules switch.
- A monster's Proficiency Bonus is determined by CR, but the printed stat block reflects the
  complete exceptional aptitude for its saves, skills, and other statistics. Published bonuses
  therefore cannot be recreated by silently applying the player-character class/level formula.
- An attack declaration specifies its attack kind, bonus, reach/range, and hit/miss consequences;
  multiattack and special abilities are separate declarations, not permission to execute arbitrary
  content text.
- CR summarizes a monster's threat to four player characters. Encounter difficulty is guidance,
  not a persistent player reward, hostile-side decision, or a campaign resolution.

## Planning inventory and overlap result

| Inquiry | Repository evidence and decision |
| --- | --- |
| Creature fundamentals | `dnd2024.abilities`, `dnd2024.hit-points`, `dnd2024.armor-class`, `dnd2024.creature-size`, and `dnd2024.speed` already own actor facts and normal writers. A profile declares source data; a bootstrap must compose with those writers rather than copy their components. |
| Character-only calculations | The saving-throw resolver requires `dnd2024.character-level` and character save proficiency. Weapon attack similarly derives character proficiency/level facts. They cannot be treated as a monster-stat-block resolver. |
| Final Armor Class | Feature 6 owns current final AC; Feature 24 deliberately reserves natural armor for Feature 35 and requires one later AC-reader migration. Feature 35 may carry printed source AC, but it must not invent a natural-armor formula or competing final-AC state. |
| Zero HP and death | Feature 17 owns `dnd2024.zero-hit-points-policy` because the SRD branch is policy, not intrinsic identity. Feature 35 supplies a profile fact/authorised bootstrap input only after Feature 17 Slice 1; superseding that policy requires its own explicit migration. |
| Conditions, damage, healing, and mitigation | Features 13 and 15–17 retain all state/lifecycle/effect ownership. Traits and attacks declare only a named consequence family; they cannot write HP, condition, resistance, temporary HP, healing, or death state. |
| Tactical combat | Features 5/11–12 own Initiative/order/turn allowance; Feature 20 owns placement/distance/reach; Feature 21 owns sides, cover, and physical line; Feature 34 owns perception. A stat block is not an encounter, and an encounter membership record is not a map/side/turn. |
| Items and gear | Feature 23 owns immutable item definitions, instances, custody, and equipped state. A stat block may name source gear definitions; it never creates or makes them recoverable. |
| Senses, spells, rest, and effects | Feature 34 owns sense semantics; Features 31–32 own spell identity/casting/effect lifecycle; Feature 33 owns rest/recovery timing. Monster source declarations wait for their accepted grant/execution interfaces. |
| Existing monster owner | Searches find creature fixtures and generic creature records, but no monster identity, stat-block, CR, monster action, monster trait, or encounter-builder owner. Existing fixtures are not a bestiary model. |
| Campaign reward/advancement | Feature 36 owns character XP state and Campaign C14 authorises advancement. Monster CR/XP may be read as source facts but cannot award XP, mark defeat, or level a character. |

## Recursive dependency analysis

~~~text
Feature 35: monsters, stat blocks, and encounter assembly                         [blocked parent]
├─ official source registry and stat-block vocabulary                              [implemented basis]
├─ creature base-state owners (abilities, HP, AC, Size, Speed)                    [implemented: 2, 6, 20, 23]
├─ conditions, damage, mitigation, healing, zero-HP policy                        [mixed: 13, 15–17]
├─ initiative/order and turn budget                                                [implemented: 5, 11–12]
├─ physical inventory/equipment                                                    [implemented: 23]
├─ map/placement, encounter sides, cover/physical line                            [blocked: 20 Slice 2; 21 Slices 2–3]
├─ observation/senses                                                              [blocked: 34]
├─ spell/effect/rest lifecycle                                                     [blocked: 31–33]
├─ immutable monster identity                                                      [missing Slice 1 leaf]
├─ immutable stat-block profile and declared capability catalog                   [blocked: identity]
├─ CR/PB and printed-stat readers                                                  [blocked: profile]
├─ monster-profile reference and actor bootstrap                                   [blocked: profile + staged composition + 17]
├─ monster save/check/initiative and attack evidence                               [blocked: actor + typed action/tactical seams]
├─ declared trait/action/usage/recharge execution                                  [blocked: effect, action, clock, consequence owners]
├─ encounter composition and CR diagnostic                                         [blocked: actor bootstrap + sides + party scope]
└─ source bestiary expansion                                                       [blocked: accepted vertical families]
~~~

The only independent leaf is a closed, immutable monster identity catalog. It supplies a
source-cited reference for later profiles without declaring an ability score, creating a creature,
or making a monster available in play.

## Dependency and ownership decisions

1. **A stat block is immutable content; a monster is an actor.** A versioned
   `content.dnd2024.monster.<key>.vN` entity holds source content. A later runtime actor has one
   profile-reference component with exact content key/version/provenance; it holds no copied
   abilities, HP, AC, speed, senses, actions, or CR. Absence means the actor is not a supported
   monster instance, never an untyped default monster.
2. **Identity precedes detail.** The first component, proposed as
   `dnd2024.monster-identity`, is deliberately small: stable monster key/version, display name,
   Size, creature type(s)/tags, alignment token, Challenge Rating token, and source reference.
   A later profile owns all remaining published facts. The identity is neither actor state nor a
   character-content definition; Feature 26's character-only identity remains closed.
3. **Profile declarations are structural interfaces, not executable prose.** A future
   `dnd2024.monster-stat-block-profile` declares source-backed abilities, printed HP/AC/Speed/
   Initiative facts, fixed bonus facts, senses/languages/gear references, and canonical named
   trait/action entry references. A capability entry declares its activation family, bounded
   usage/recharge class, source locator, and named resolution/consequence family. It contains no
   JavaScript, arbitrary target, result, dice roll, damage total, state effect, or copied source
   prose.
4. **Published bonuses remain source facts where they are exceptional.** F35 owns a future
   effect-free monster statistics reader. It derives CR-band PB from F35's immutable challenge
   table, reports the printed Initiative/skill/save/attack bonuses declared by the matched profile,
   and may expose an audit comparison to ability-plus-PB only when the profile explicitly declares
   that simple basis. It does not make a character level, replace Feature 3's D20 convention, or
   infer unlisted proficiency.
5. **Actor bootstrap is a coordinated, all-or-nothing consumer.** It obtains every base component
   through each existing writer and then records the exact profile reference. It must pass an
   explicit Feature-17 zero-HP policy through that owner, and must create each source-declared
   sense, item, spell, or trait state only through its accepted owner. No partial actor, “mostly
   initialized” component bundle, or caller-supplied final stat is valid.
6. **Natural armor is a future F35 base-AC source, not an exception to AC ownership.** The source
   profile may name a printed AC and its declared basis. Feature 24/F35 must ratify a shared
   alternative-base interface before an actor derives natural armor. Until that migration, no
   profile or bootstrap writes a formula or treats a manual AC as an enduring second truth.
7. **Monster actions are admitted and resolved by family.** F35 decides whether a profile declares
   an action and its source-specific activation/usage limit. Features 12, 20–21, 34, 3–4, 8–9,
   13, and 15–17 retain budget, spatial/sight, D20, weapon, damage, condition, and death
   consequences. An unusual trait opens a named future family; it never falls through to a generic
   “execute trait text” path.
8. **Encounter building is assembly plus diagnostic, not campaign adjudication.** A later root
   validates a bounded set of independently bootstrapped actors, the accepted encounter side/map
   state, and party scope. Its CR summary is a read-only guidance result. It creates no map,
   determines no hostility, rolls no Initiative, grants no XP, and does not decide that a monster
   is defeated.

## Confirmation boundaries

| Decision | Required confirmation before implementation |
| --- | --- |
| Identity vocabulary | Exact entity/component/procedure IDs, identity key syntax, creature-type/tag/alignment/CR vocabularies, source-locator shape, immutable revision rule, and initial source fixtures. |
| Stat-block profile | Component/entry IDs; separation of identity/profile/action entry; HP expression, AC basis, Initiative, bonus, speed, language, gear, and sense declaration shapes; closed canonical ordering; no-prose/script constraint. |
| CR and printed statistics | Exact CR table, fraction representation, XP/PB treatment, source version, diagnostic result shape, and policy for absent versus explicitly unlisted stat entries. |
| Actor reference/bootstrap | Entity lifecycle, profile-reference shape, owner-child/effect composition, identity/version validation, exact source-to-writer mapping, correction/replacement policy, atomic rollback, and no partial actor. |
| Zero-HP handoff | Feature 17 Slice 1 writer semantics, profile-to-policy mapping, independent policy override rule, and explicit migration if F35 ever owns richer replacement data. |
| Natural AC | Feature-24 alternative-base selector, printed-AC migration, profile source basis, Feature-8 attack-reader migration, and no coexistence of manual/derived truths. |
| Action families | Which initial trait/action entries are supported, action/reaction/legendary/usage/recharge state owners, action-cost/tactical inputs, D20 seed/audit path, consequence-child bindings, and source-event lifecycle. |
| Encounter diagnostic | Party membership/level source, side/map/roster lifecycle, CR aggregation rules, trusted-host authority, and separation from XP/rewards/advancement. |

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| ---: | --- | --- | --- |
| 1 | Immutable monster identity catalog | Permanent vocabulary and one small source fixture set confirmed. | Source-cited monster identities read deterministically with zero actor, stat, combat, encounter, or campaign effects. |
| 2 | Immutable stat-block profile and declared capability entries | Slice 1 and full static-field vocabulary confirmed. | One profile references one identity and exposes only closed source declarations; no actor/action/effect exists. |
| 3 | CR/PB and printed-stat diagnostic readers | Slice 2 and CR-table/bonus semantics confirmed. | Effect-free reader reports a profile's CR band/PB and declared printed statistics without a character-level inference or random call. |
| 4 | Monster profile reference and atomic base-actor bootstrap | Slices 1–3, staged composition, Feature 17 Slice 1, and each base-state owner confirmed. | One actor is fully composed from one profile or nothing changes; all components remain owned by their current writers. |
| 5 | Natural-AC and source-grant integration | Slice 4, Feature 24 AC migration, Feature 34 senses, Feature 23 gear, and Feature 31–32 spell seams. | A supported profile contributes only ratified source grants/alternative AC through their dedicated owners. |
| 6 | Monster check/save/Initiative and basic attack evidence | Slice 4, typed statistic rules, Initiative context, and attack/tactical composition confirmed. | One declared simple action returns auditable effect-free resolution evidence from profile and actor facts only. |
| 7 | Trait/action usage, consequences, and special action families | Slice 6 plus each action/effect/clock/consequence owner. | Each accepted family spends/limits/resolves/applies exactly once or rolls back atomically; unsupported entries fail explicitly. |
| 8 | Encounter assembly and CR diagnostic | Slices 4–7, Feature 20 map/placement, Feature 21 sides, and party-scope authority. | A bounded encounter validates supported participants and returns CR guidance without creating a campaign reward or tactical duplicate. |
| 9 | Bestiary expansion | Slices 1–8 and source-family review. | Every additional stat block reuses accepted declarations/families and has fixture, routing, and source evidence. |

## Slice 1 — immutable monster identity catalog

### Runtime artifacts

- A confirmed immutable `dnd2024.monster-identity` component/schema and governing static-content
  procedure, attached only to a versioned ruleset content entity.
- A small confirmed fixture set beginning with one uncomplicated SRD monster identity, proposed as
  `content.dnd2024.monster.goblin-warrior.v1`, with its exact page/entry locator confirmed before
  authoring.
- Focused catalog validation/tests only. No profile details, actor, ability, HP, AC, Size, Speed,
  zero-HP policy, action, trait, item, spell, D20 roll, event, encounter, XP, or campaign effect.

### Governing contracts and source locator

Immediately before implementation, re-read `procedure.system.create-feature`, the source-registry
and static-content/definition conventions, Feature 17's zero-HP-policy boundary, Feature 24's
natural-AC boundary, and Feature 26's character-content identity boundary. Use
`source.dnd2024.srd-5.2.1`, *Monsters > Stat Block Overview*, plus the exact initial stat-block
entry. Confirm IDs and source pages at the permanent-ID boundary; the proposed Goblin Warrior
fixture is a planning candidate, not an authorisation to guess data.

### Data/input contract and required state

The component is closed and immutable. It declares:

- a normalized stable monster key and matching entity/version;
- display name;
- one canonical Size token;
- ordered unique creature-type/tag tokens;
- one alignment token;
- one closed CR token (including the confirmed fractional vocabulary); and
- one fixed source reference plus exact stat-block locator.

It holds no ability score/modifier, HP/hit dice, AC, Initiative, Speed, proficiency, skill/save/
attack bonus, sense, language, gear, trait, action, reaction, spell, XP value, profile reference,
actor id, zero-HP policy, condition, map/position, encounter, party, campaign, source prose, or
code. Missing identity is invalid; an empty tag list is allowed only when the confirmed source
syntax permits it. Canonical ordering and case are part of validity. Corrections require a
successor identity version, never mutation of an identity a later profile or receipt may cite.

### Recording behavior, result, and effects

Catalog authoring validates the closed identity and source locator, then readback returns exact
entity ID/key/version/source facts. The result has zero effects and no player-facing match phrase.
Reading identity cannot create a creature, establish a monster type on an actor, determine the
zero-HP rule, provide an Initiative modifier, calculate PB/XP, or make an encounter.

### Invariants, failure behavior, and non-goals

- Entity key/version and component key/version agree; the source reference belongs to the active
  SRD source registry and has an exact monster-stat-block locator.
- Wrong source/key/version/size/type/tag/alignment/CR/locator, duplicates, noncanonical ordering,
  extra data, or an in-place rewrite rejects unchanged.
- Rejection/readback leaves every creature, actor component, item, encounter, campaign, event,
  and audit state byte-identical; it makes no random call.
- Slice 1 neither imports a complete stat block nor selects, instantiates, equips, positions,
  allies, damages, kills, rewards for, or otherwise makes a monster playable.

### Slice 1 implementation sequence

1. Re-read the governing contracts and inspect catalog identity/component conventions. Search
   `monster`, `stat block`, `bestiary`, `creature type`, `challenge rating`, `CR`, and the proposed
   ID before creating any permanent vocabulary.
2. Confirm exact component/procedure/entity IDs, CR/creature-type/tag/alignment tokens, source
   locator, fixture scope, successor-version policy, and whether a compatible ruleset-content
   owner already exists. Stop for this semantic boundary.
3. Author schema, procedure, fixture, manifest entry, and focused validation tests together. Do
   not create profile fields early or reuse character-content identity.
4. Prove valid readback and rejection of every malformed/duplicate/stale/extra/mutable case, with
   byte-identical world-state isolation and deterministic replay.
5. Run focused tests, `roleplay validate catalog`, the full suite, and `git diff --check`; record a
   receipt and stop. Do not begin Slice 2.

### Slice 1 acceptance matrix

| Case | Exact assertion |
| --- | --- |
| Source identity | Each confirmed fixture returns exact ID/key/version, name, Size, creature type/tags, alignment, CR, source entity, and page/entry locator. |
| Closed shape | Missing/null/wrong-case/wrong-type/unknown Size/type/tag/alignment/CR, duplicate tag, bad order, malformed locator, entity/component mismatch, or extra field rejects unchanged. |
| Version discipline | Duplicate identity/version and in-place correction reject; a reviewed successor version can coexist without changing prior identity readback. |
| Separation | Identity exposes no ability/HP/AC/Speed/action/XP/zero-HP/actor/encounter fields and creates no component/entity/action/event effect. |
| Isolation and determinism | Equivalent reads are byte-identical, make no random call, select no player phrase, and leave creatures, items, encounters, campaigns, events, and audits byte-identical. |
| Compatibility | Existing character-content, item, creature-size, source-registry, and catalog-fixture tests retain their exact behavior; no generic “monster” route shadows an existing intent. |
| Repository | Focused tests, disposable catalog validation, full suite, diff check, and catalog query-back pass; no persistent import occurs. |

### Slice 1 exit gate

Slice 1 is verified only when the immutable source-cited identity catalog has closed versioned
data, rejection/immutability/isolation evidence, catalog validation, repository checks, and a
receipt. Stop before detailed stat blocks, CR arithmetic, actor bootstrap, or encounter assembly.

## Later owner and consumer map

~~~text
immutable monster identity
└─ stat-block profile / declared entries ───────────────> Feature 35
   ├─ CR/PB and printed-stat reader ────────────────────> Feature 35 diagnostics
   ├─ profile reference ─────────────────────────────────> monster actor bootstrap
   │  ├─ abilities / HP / final AC / Size / Speed ───────> Features 2, 6, 20, 23
   │  ├─ zero-HP policy / conditions / death ───────────> Features 13, 15–17
   │  ├─ inventory / gear ──────────────────────────────> Feature 23
   │  ├─ senses / can-see ──────────────────────────────> Feature 34
   │  └─ spell resource/effect declarations ────────────> Features 31–33
   ├─ simple attack evidence ───────────────────────────> Features 3–4, 8–9, 12, 20–21, 34
   ├─ trait/action/usage/recharge family ───────────────> Feature 35 plus each named effect/clock owner
   └─ encounter member / CR diagnostic ─────────────────> Features 5, 11–12, 20–21; party/campaign authority
~~~

## Plan-quality audit

- One capability—source-backed monster content that later becomes independently bootstrapped
  encounter actors—with explicit non-goals: yes.
- Identity, immutable profile, runtime actor reference, canonical state, derived statistics,
  transient action context, and downstream effects have distinct owners: yes.
- The graph expands all missing parents to one independent Slice-1 leaf: yes.
- Slice 1 has closed data, source, versioning, failure/isolation behavior, an implementation
  sequence, acceptance matrix, and all-or-nothing exit gate: yes.
- No runtime game artifact was created during this planning pass: yes.

## Plan-change rule

Revise before implementation if a compatible immutable ruleset-content identity owner already
exists, Feature 17 changes policy ownership, Feature 24 selects an incompatible natural-AC
interface, Feature 20/21 changes encounter membership/sides, or a source stat block needs a
declaration family not yet owned. Do not use monster identity as death policy, character level,
generic creature rules, action script, item inventory, map position, hostile-side flag, XP award,
or campaign outcome; do not accept caller-supplied printed totals, effects, targets, or stat-block
text as a substitute for the immutable profile.

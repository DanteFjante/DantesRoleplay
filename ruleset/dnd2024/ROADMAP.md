# D&D 2024 ruleset development roadmap

Status: **Features 1–16 verified; Feature 17 Slice 1 verified; Feature 23 accepted; Feature 28 Slice 1 verified; the remaining rows are planned backlog**
Last updated: 2026-08-21

## Purpose and authority

This roadmap is the numbered index for the D&D 2024 ruleset. Features 1–10 establish one
reproducible vertical session; Features 11–38 identify the remaining capabilities needed for
complete SRD play. It is not a promise to implement all of D&D and it does not authorize bundling
several features into one pass.

`procedure.system.create-feature` is the governing workflow. For each feature, create or review
its recursive dependency plan, implement exactly one lowest unimplemented slice, meet that
slice's exit gate, record evidence, and stop. Catalog files are the canonical development source
for rule contracts, component definitions, entities, and mechanics; dry-run, import, and verify
them into the live database. Repository planning documents hold plans and evidence, not copied
runtime payloads.

`TERRA-FEATURE-PLANNING-GUIDE.md` is the reusable planning playbook for expanding future roadmap
rows to this quality bar. It requires a planning-only pass, live ownership/dependency evidence,
complete slice specifications, a plan-quality audit, and a stop before implementation.

The official rule source is SRD 5.2.1, represented by the existing live source entity
`source.dnd2024.srd-5.2.1`.

## Verified foundation

- Feature 1: ability scores and seeded fixed-DC ability checks.
- Feature 2: character level, derived Proficiency Bonus, the 18 skill IDs, character skill
  proficiency state, and proficient named-skill checks.
- Shared deterministic dice mechanic exists, but it is not a substitute for D20 Test rules.
- Repository regression: the suite passes. A pinned number here drifts within a week — this line
  has already said 213 and 304 while the suite was neither. Read the last run, not this file.

Features 3–10 now supply the D&D implementations for Advantage/Disadvantage, saving throws,
Initiative, weapon attacks, damage, and Hit Point loss. Generic dice and threshold mechanics
remain examples, not substitutes for those rules.

## Minimum test-session dependency graph

```text
small D&D 2024 test session
├─ exploration checks                                      [implemented: Features 1–2]
│  ├─ abilities and modifiers                              [implemented]
│  ├─ skill proficiency and level bonus                    [implemented]
│  └─ Advantage/Disadvantage on checks                     [Feature 3 verified]
├─ defensive D20 Tests                                     [Feature 4 verified]
│  ├─ shared Advantage/Disadvantage convention             [Feature 3]
│  ├─ saving-throw proficiency state                       [Feature 4 Slice 1 verified]
│  └─ saving-throw resolution                              [Feature 4 Slice 2 verified]
├─ combat entry                                             [Feature 5 verified]
│  ├─ closed action-input transport                        [system Slice 0 verified]
│  ├─ Dexterity-based Initiative roll                      [Feature 5 Slice 1 verified]
│  └─ deterministic arbitrary-roster order and tie policy [Feature 5 Slice 2 verified]
├─ combatant durability                                     [Feature 6 verified]
│  ├─ Armor Class state                                    [Feature 6 Slice 1 verified]
│  └─ current/max Hit Points state                         [Feature 6 Slice 2 verified]
├─ weapon attacks                                           [Features 7–8 verified]
│  ├─ weapon profile and proficiency state                 [Feature 7 Slices 1–2 verified]
│  ├─ attack roll vs Armor Class                            [Feature 8 Slice 1 verified]
│  └─ natural 20/1 and Critical Hit classification         [Feature 8 Slice 1 verified]
├─ damage and consequences                                  [Feature 9 verified]
│  ├─ seeded damage dice and critical extra dice           [Feature 9 Slice 1 verified]
│  └─ validated Hit Point application                      [Feature 9 Slice 2 verified]
└─ vertical acceptance session                              [Feature 10 verified]
   ├─ one player character and one simple opponent         [fixtures verified, not new rules]
   ├─ exploration check, Initiative, attack, damage, save  [all parents verified]
   └─ exact replay and final-state audit                    [Feature 10 verified]
```

## Ordered features and boundaries

| Feature | Capability | Depends on | Status | Deliberate non-goals |
| --- | --- | --- | --- | --- |
| 1 | Ability scores and seeded fixed-DC ability checks | Core mechanics | Verified | Saving throws, Initiative, attacks, damage |
| 2 | Character level, Proficiency Bonus, skill IDs, and proficient named-skill checks | Feature 1 | Verified | Classes, advancement, character creation |
| 3 | Advantage/Disadvantage for ability checks, plus one reusable D20 Test input/result convention | Features 1–2 | Verified | Heroic Inspiration, rerolls, persistent conditions, automatic circumstance discovery |
| 4 | Six saving-throw proficiencies and fixed-DC saving throws | Feature 3 and character level | Verified | Spell effects, death saves, monster CR, legendary resistance |
| 5 | Initiative rolls and deterministic encounter ordering | Feature 3 and abilities | Verified | Full turn economy, surprise state beyond its supplied roll circumstance, ready/delay |
| 6 | Authoritative Armor Class and Hit Point state | Source registry | Verified | Armor-building formulas, classes, equipment loadouts, temporary HP, resistance |
| 7 | Minimal weapon profiles and character weapon proficiency | Source registry and level | Verified | Complete equipment catalog, mastery, ammunition, range/cover, class grants |
| 8 | Weapon attack rolls against Armor Class | Features 3, 6, 7 | Verified | Multiattack, opportunity attacks, spell attacks, damage application |
| 9 | Weapon damage and transactional Hit Point loss | Features 6–8 and deterministic dice | Verified | Resistance, immunity, vulnerability, healing, unconsciousness, death saves |
| 10 | One reproducible vertical test session | Features 1–9 | Verified | Campaign management, character builder, complete combat engine |
| 11 | Turn and round lifecycle: active participant, turn advance, round counter, and encounter end | Feature 5 | **Verified** — [plan](feature-11/FEATURE-11-DEPENDENCY-PLAN.md) | Simultaneous turns, Initiative rerolls each round, delay, ready |
| 12 | Action economy: Action, Bonus Action, Reaction, interaction, and Move are spent and restored | Feature 11 | **Verified** — [plan](feature-12/FEATURE-12-DEPENDENCY-PLAN.md), [receipt](feature-12/FEATURE-12-IMPLEMENTATION-RECEIPT.md) | Legendary/lair actions, multiattack routines, and which rule costs which resource |
| 13 | SRD conditions: apply, list, clear, and enforce their effects on checks, saves, and attacks | Feature 12 | **Verified** — [plan](feature-13/FEATURE-13-DEPENDENCY-PLAN.md), [receipts](feature-13/) | Homebrew conditions, species-based immunity, non-SRD stacking, Exhaustion (Feature 14), and every positional condition effect |
| 14 | Exhaustion levels and their D20 Test and speed effects | Feature 13; E1 | **Verified** — [plan and receipts](feature-14/) | Recovery pacing (Feature 33) and death at level 6 (Feature 17) |
| 15 | Damage types with resistance, immunity, and vulnerability in SRD order | Features 9, 13; E1 | **Verified** — [plan and receipts](feature-15/); confirmed weapon damage now applies mitigation and records overkill | Damage transfer, shared damage, absorption, half-damage-on-a-save |
| 16 | Temporary Hit Points and healing, including no-stacking rules | Features 6, 15; E1 | **Verified** — [plan and receipts](feature-16/); buffers absorb mitigated damage before Hit Points and healing clamps without affecting them | Timed regeneration, healing over time, and every source of healing |
| 17 | Dying: zero HP, unconsciousness, death saves, stabilization, and massive-damage death | Features 13–16; E1 | **Slices 1–3 verified in scope** — [plan and receipts](feature-17/); zero-HP policy, bounded terminal death state, and condition-list guard are ready; dropping-to-zero reaction is next | Resurrection, lingering injuries, and the 1d4-hour stable recovery (needs a clock) |
| 18 | Concentration: one effect at a time, damage-triggered save, and ending conditions | Features 4, 13, 15–17, 32; E1 and confirmed reaction composition | **Planned in full** — [plan](feature-18/FEATURE-18-DEPENDENCY-PLAN.md); runtime slices are blocked by the effect-source and composition boundaries | Metamagic and feature-specific exceptions |
| 19 | Reactions in play: opportunity attacks and triggered abilities | Features 8, 12, 20–21, 34; E1 and confirmed reaction composition/choice protocol | **Planned in full** — [plan](feature-19/FEATURE-19-DEPENDENCY-PLAN.md); runtime slices are blocked by Feature 20 spatial timing, Feature 21 geometry, Feature 34 sight, and dynamic reaction composition | Counterspell timing puzzles and held actions |
| 20 | Position and movement: speed, distance, difficult terrain, and reach as attack preconditions | Features 11–12, 23; E1 and confirmed derived-input composition | **Slices 1–4 verified** — bounded maps, collision-safe placement, base-reach evidence, tactical melee admission, and atomic closed-path movement are installed; [Slice 4 receipt](feature-20/FEATURE-20-SLICE-4-MOVEMENT-RECEIPT.md). Difficult terrain and SRD pass-through are Slice 5. | Rendered grid, pathfinding, flanking, elevation/3D terrain |
| 21 | Cover and ranged combat: half/three-quarters cover, long-range Disadvantage, and ranged attacks in close combat | Features 6–8, 13, 20, 34; confirmed trusted attack-context composition | **Slice 1 verified** — static Shortbow 80/320 range data; cover, sides, sight, position, and tactical resolution remain blocked — [plan](feature-21/FEATURE-21-DEPENDENCY-PLAN.md), [receipt](feature-21/FEATURE-21-SLICE-1-RECEIPT.md) | Arbitrary/dynamic geometry and projectile physics |
| 22 | Unarmed and improvised combat: unarmed strike, grapple, shove, improvised weapons, and two-weapon fighting | Features 4, 8, 12–13, 15, 20, 23, 25; confirmed condition/spend composition | **Slice 1 verified** — effect-free Strength/PB Unarmed Strike Damage evidence; tactical reach, Action spending, HP, Grapple, Shove, improvised weapons, and two-weapon fighting remain blocked — [plan](feature-22/FEATURE-22-DEPENDENCY-PLAN.md), [receipt](feature-22/FEATURE-22-SLICE-1-RECEIPT.md) | Wrestling subsystems and called shots |
| 23 | Equipment and inventory: item entities, containment, currency, and supported carrying rules | Feature 7 | **Accepted** — bounded physical inventory, carrying, equipment, currency, and fixed item activities are implemented; [plan](feature-23/FEATURE-23-DEPENDENCY-PLAN.md), [receipts](feature-23/) | Shopping economy, crafting, item durability |
| 24 | Armor, shields, armor training, and Armor Class derived from equipped items | Features 3, 6, 12, 20, 23; confirmed Armor Class migration | **Slice 1 verified** — source-backed mundane armor and Shield definitions; equipped aggregation, training, AC, effects, Speed, and timing remain blocked — [plan](feature-24/FEATURE-24-DEPENDENCY-PLAN.md), [receipt](feature-24/FEATURE-24-SLICE-1-RECEIPT.md) | Every natural-armor formula and magical stacking exception |
| 25 | Weapon properties and mastery: Finesse, Versatile, Thrown, Loading, Ammunition, and 2024 mastery | Features 4, 8–9, 12–13, 20–23; Feature 21 range schema; trusted property composition | **Slice 1 verified** — static Dagger, Shortbow, and Battleaxe properties/mastery facts; mastery permission and all behavior remain blocked — [plan](feature-25/FEATURE-25-DEPENDENCY-PLAN.md), [receipt](feature-25/FEATURE-25-SLICE-1-RECEIPT.md) | Weapon content beyond the SRD catalog |
| 26 | SRD species traits and their mechanical grants | Features 2, 15–16, 20, 23, 28, 30–34; confirmed origin assembly | **Slice 1 verified** — nine static, immutable SRD species profiles; selection and every trait consequence remain blocked — [plan](feature-26/FEATURE-26-DEPENDENCY-PLAN.md), [receipt](feature-26/FEATURE-26-SLICE-1-RECEIPT.md) | Non-SRD species and custom lineages |
| 27 | Classes and levels: features, hit dice, subclasses, proficiency grants, and multiclassing rules | Feature 2; CH4; C14; CH9; Feature 33 for recovery | **Slice 1 verified** — immutable Fighter 1–2 progression and effect-free entitlement reader; actor membership, HP, feature behavior, and level-up remain blocked — [plan](feature-27/FEATURE-27-DEPENDENCY-PLAN.md), [receipt](feature-27/FEATURE-27-SLICE-1-RECEIPT.md) | Non-SRD subclasses and homebrew progression |
| 28 | Character-origin foundation: language/tool proficiency state, then backgrounds, feats, and ability-score improvements | Feature 2 for Slice 1; Feature 27 for later grant resolution | Slice 1 verified — [plan](feature-28/FEATURE-28-DEPENDENCY-PLAN.md), [receipt](feature-28/FEATURE-28-SLICE-1-RECEIPT.md) | Non-SRD feats; language/tool checks, crafting, and feature effects |
| 29 | Attunement and magic items: SRD item set, attunement slots, and item-granted effects | Features 16–17, 20, 23–25, 27, 31–33; confirmed rest/effect composition | **Slice 1 verified** — static Potion of Healing, Boots of Elvenkind, and Amulet of Health profiles; instances, attunement, and effects remain blocked — [plan](feature-29/FEATURE-29-DEPENDENCY-PLAN.md), [receipt](feature-29/FEATURE-29-SLICE-1-RECEIPT.md) | Artifacts, sentient items, curses, and item creation |
| 30 | Guided character creation producing a legal playable sheet | Character CH0–CH6; Campaign C15; Features 23–28 and each selected ruleset owner | **Planned in full** — [plan](feature-30/FEATURE-30-DEPENDENCY-PLAN.md); the next actual candidate is CH5 Slice 0 staged-composition proof | Visual character-builder UX, drafts, and a duplicate create API |
| 31 | Spellcasting resources: slots, known/prepared spells, spellcasting ability, save DC, and attack bonus | Features 1–2, 12, 27, 32–33; Character CH10; confirmed resource composition | **Slice 1 verified** — static Fire Bolt, Cure Wounds, and Dancing Lights identities; casting profile/resource work remains blocked on a caster source/class seam — [plan](feature-31/FEATURE-31-DEPENDENCY-PLAN.md), [receipt](feature-31/FEATURE-31-SLICE-1-RECEIPT.md) | Spell-point variants, multiclass aggregation, and non-SRD spells |
| 32 | Spell resolution: spell attacks, saves, targeting, areas, duration, and effects | Features 3–4, 9, 12–13, 15–21, 31, 33–34; confirmed effect/clock composition | **Slice 1 verified** — static Fire Bolt, Cure Wounds, and Dancing Lights resolution profiles; effects, casting, and consequences remain blocked — [plan](feature-32/FEATURE-32-DEPENDENCY-PLAN.md), [receipt](feature-32/FEATURE-32-SLICE-1-RECEIPT.md) | Every SRD spell as initial content and generic spell scripts |
| 33 | Rests: short/long rest, Hit Dice, resource recovery, and expiry | Features 14, 16, 27, 31; core world clock; confirmed event/recovery composition | **Planned in full** — [plan](feature-33/FEATURE-33-DEPENDENCY-PLAN.md); Slice 1 (immutable standard-rest policy) is next | Gritty-realism and other optional rest variants |
| 34 | Vision/light, hiding, passive Perception, and encounter surprise | Features 3, 5, 12–13, 20–21, 23, 26, 35; confirmed observation composition | **Planned in full** — [plan](feature-34/FEATURE-34-DEPENDENCY-PLAN.md); Slice 1 (immutable observation policy) is next | Dynamic-lighting geometry |
| 35 | Monsters and stat blocks: creature data, CR, traits, actions, and encounter building | Features 2, 5–6, 12–17, 20–21, 23–24, 31–34; confirmed actor/bootstrap composition | **Planned in full** — [plan](feature-35/FEATURE-35-DEPENDENCY-PLAN.md); Slice 1 (immutable, source-cited monster identities) is next | Full SRD bestiary import as part of the capability |
| 36 | Advancement: XP or milestone and level-up through existing class features | Feature 27; Campaign C14; Character CH9 | **Slice 1 implemented:** explicit XP state and effect-free next-level eligibility; campaign awards and level-up remain blocked on C14, Feature 27, and CH9 — [plan](feature-36/FEATURE-36-DEPENDENCY-PLAN.md) | Automatic optimisation and respec tooling |
| 37 | D&D travel pace and time integration: apply an authorized pace policy and elapsed-time consequences to the existing world routes, itinerary, and clock | Features 20, 32, 33; core world travel/time; E1 | **Planned; blocked on source registration, generic route-distance ownership, and Features 32–33** — [plan](feature-37/FEATURE-37-DEPENDENCY-PLAN.md) | Rebuilding routes, maps, clocks, conveyances, weather simulation, or hex-crawl generation |
| 38 | Social interaction: attitude, Influence checks, and non-trivial persuasion | Features 2–3, 12–13; core world clock; confirmed social/campaign authority | **Planned in full** — [plan](feature-38/FEATURE-38-DEPENDENCY-PLAN.md); Slice 1 (immutable, source-cited social-interaction policy) is next | NPC personality simulation |

Feature numbers express dependency order, not permission to begin them. Features 1–10 are verified.
Feature 5's file-first catalog import gate exercises the composition runtime and encounter-order
matrix. Feature 6 records final Armor Class and bounded current/maximum Hit Point state through
the catalog; both slices are verified in feature-06/FEATURE-6-DEPENDENCY-PLAN.md. Feature 7 now
provides canonical Dagger, Shortbow, and Battleaxe profile data plus authoritative Simple/Martial
weapon-category proficiency state. Feature 8 resolves effect-free attacks against final AC,
including natural-20/1 classification, without damage or persistence. Feature 9 now provides
effect-free confirmed-hit damage evidence and a composed, transactional target Hit Point
application parent. Feature 10's catalog-owned baseline fixtures and two-database deterministic
vertical-session harness are verified. The first reproducible D&D 2024 session is complete.

Features 12–17 have full recursive dependency plans, and Feature 23's plan is accepted, under `ruleset/dnd2024/feature-NN/`,
written to the `TERRA-FEATURE-PLANNING-GUIDE.md` bar and stopping before implementation. The
following findings change this table rather than only those files:

- **Feature 15 depends on Feature 13**, not on Feature 9 alone. Petrified grants Resistance to all
  damage, so a mitigation resolver blind to conditions would be wrong on the day it shipped.
- **Two hidden dependencies exist that no row here names**, both discovered by expanding Feature 17:
  a minimal `dnd2024.creature-kind` marker (nothing distinguishes a player character from a monster,
  and the dying rules branch on exactly that), and a condition-integrity guard (a subscribed
  mechanic may not declare children, so a reaction cannot reuse Feature 13's condition writer).
- **Features 14, 15 and 16 each register an event a later feature subscribes to.** A feature that
  produces a fact a later feature must react to declares an event for it in the pass that produces
  the fact; retrofitting one means revising a verified mechanic and re-running its exit gate.
- **Feature 22 has four additional explicit seams:** Feature 20 owns tactical reach and the future
  forced-push transition; a narrow manipulation-capacity/encounter-grapple reader is needed for
  free hands; Feature 25 owns Light and weapon-hand facts; and a distinct per-turn Attack-action
  ledger is required because Feature 12 intentionally records only whether an Action was spent.
- **Feature 24 cannot update final AC as an equipment side effect.** Feature 6's manual
  dnd2024.armor-class and Feature 8's direct consumer need one reviewed derived-reader migration,
  while Feature 20 owns the heavy-armor effective-Speed consequence and a later clock owner owns
  minute-based don/doff completion.
- **Feature 25 and Feature 21 share one range schema.** Normal/long and Thrown ranges both extend
  Feature 7's weapon profile; the static vocabulary must be confirmed once before either tactical
  property behavior is implemented. Mastery permission, temporary effects, hands, ammunition,
  action limits, movement, and saves remain distinct downstream seams.
- **Feature 26 separates species catalog identity from selected character state.** The existing
  immutable character-content identity may receive source facts, but Feature 30 must assemble a
  selected species atomically through the existing Size, Speed, and proficiency owners. Humanoid
  source data is not the player-character/monster marker Feature 17 still needs.
- **Feature 29 separates magic catalog data, physical instances, attunement, and effects.** A
  profile’s attunement/activation declaration does not make an item held or an actor attuned.
  Feature 23 retains custody, Feature 33 owns the Short-Rest/time handoff, and each item effect
  must compose with its specific HP, combat, condition, movement, senses, or spell owner.
- **Feature 30 is an acceptance layer, not another character model.** CH5 owns the one atomic
  create transaction and CH6 owns discovery/play handoff. The next technical prerequisite is
  CH5’s generic staged-composition proof, so child planners can validate one reserved actor and
  return ordered effects without creating partial state or duplicating validation.
- **Feature 31 separates spell identity, class profile, actor resource state, and spell casting.**
  A catalog spell does not grant a slot or cast permission; derived DC/attack statistics remain
  readers over abilities, level, and source profile. Feature 32 alone owns player-facing casting
  and Feature 33 alone owns recovery/preparation timing.
- **Feature 32 supplies the active spell-effect protocol, not a generic effect shortcut.** It must
  identify the exact source, creator, subjects, and end lifecycle an effect needs, allowing Feature
  18 to hold one concentration reference. Action/slot spend, target geometry, D20/save, damage,
  healing, conditions, and time all remain their existing owners.
- **Feature 33 owns rest timing and completion, while the world owns time itself.** The existing
  root clock is the only elapsed-time coordinate, but fixed-role subscriptions cannot yet fan an
  accepted clock replacement out to arbitrary active rests. Full Long-Rest recovery also requires
  a Feature-16-owned full-HP transition and named reversal owners for reduced ability scores and
  HP maximum; no rest may directly rewrite those facts.
- **Feature 34 owns perceptual outcomes, not a second tactical map.** Feature 20 must first
  establish placement and Feature 21 must establish physical cover/line and enemy scope. Existing
  child composition cannot yet pass a derived Hide or surprise result into the Action, condition,
  or Initiative owner, so a source-cited observation policy is the only safe first slice.
- **Feature 35 separates immutable stat blocks from live encounter creatures.** A monster profile
  may declare source facts, CR, and named capabilities, but actor bootstrap must compose through
  existing ability, HP, AC, Size, Speed, zero-HP, item, sense, turn, tactical, and consequence
  owners. Character-only level/proficiency calculations cannot be reused to recreate published
  monster bonuses; a source-cited identity catalog is the only safe first slice.
- **Feature 38 separates social rules from NPC storytelling.** Attitude is a directional
  target-to-player-character fact, while willingness remains a trusted GM judgement. The existing
  ability-check child cannot receive the target-derived social DC/circumstances, so static source
  policy is the only safe first slice; live Influence also needs social authority, an approved
  Feature-3 composition seam, action timing, and the root-clock cooldown model.

`ROADMAP-COMPLETE-PLAY.md` remains the supporting rationale for Features 11–38. This table is the
single numbered feature index used to track progress.

## Platform prerequisites

These are engine capabilities rather than D&D features. They stay separately numbered because
they unblock more than one feature.

| Dependency | Capability | Status | Required before |
| --- | --- | --- | --- |
| E1 | Event guards, subscriptions, deterministic event chains, and notifications | Verified | Features 13, 17–19, 29, 37 |
| E2 | Intent selection that ranks explicit phrases above incidental tokens | Verified | Broad mechanic growth; exact phrase collisions still remain an authoring concern |
| E3 | Hierarchical catalog navigation | Planned | Large content families, especially Features 31–35 |
| E4 | Local intent routing | Planned | A low-context GM experience; depends on E2 and E3 |
| E5 | Exact numeric fidelity across the sandbox boundary | Planned | Any feature requiring exact 64-bit values |
| E6 | Typed dependent mechanic composition | **Slices 1–2 accepted** — [plan](../../platform/e6/E6-DEPENDENCY-PLAN.md), [receipts](../../platform/e6/); consumer adoption is separately gated | Features 20, 32, 34, 38 and any trusted child-result handoff |
| E7 | Atomic staged composition and virtual projections | **Planned** — [plan](../../platform/e7/E7-DEPENDENCY-PLAN.md); depends on E6 | Character CH5 and Feature 35 actor bootstrap |
| E8 | Dynamic event role binding and bounded indexed fan-out | **Planned** — [plan](../../platform/e8/E8-DEPENDENCY-PLAN.md) | Features 17–18, 32–33 and other dynamic event consumers |
| E9 | Trusted principal context and authorization hook | **Planned but blocked** — [plan](../../platform/e9/E9-DEPENDENCY-PLAN.md) | Feature 38, campaign GM authority, and Character CH14 |

**Selection guardrail.** Feature 5 exposed a token-ranking collision, which is now corrected:
authored match phrases outrank incidental name and description tokens. Exact phrase collisions
remain an authoring risk, so every new mechanic still requires routing tests against adjacent
rules and a near-duplicate review before activation.

**Enabling-feature rule.** E6–E9 are implementation features, not a license for a consuming
feature to modify the platform. Their source plans, shared [roadmap](../../platform/PLATFORM-ENABLING-FEATURES-ROADMAP.md),
and the ordinary feature workflow govern them. E6 Slice 1 awaits repository acceptance; do not
start E6 Slice 2 until its full-suite baseline is restored. E9 remains blocked on a human
identity/authorization decision.

## Global quality gates

Every feature plan must require all of the following:

1. Query live dependencies and governing procedures immediately before writing.
2. Search mechanics and intent phrases before choosing IDs or match text.
3. Define closed input, authoritative state, derived values, result shape, effects, failure
   behavior, source locators, non-goals, and state-restoration obligations.
4. Dry-run every supported write and commit the identical payload.
5. Run the mechanic through `commit(kind: "action")`; parse and assert the returned data rather
   than treating `ok: true` as sufficient.
6. Test boundary, malformed, missing-state, deterministic replay, routing, and zero/unexpected
   effect cases proportionately to the slice.
7. Query every committed artifact and changed entity back. Temporary fixtures must be created and
   deleted through dry-run-first audited effects.
8. Run the full repository suite and `git diff --check`.
9. Record operation IDs and objective results without copying live payloads into the repository.
10. Mark only the current slice complete and stop for review.

## When the first test run is honest

The system is ready for the Feature 10 vertical session only when Features 3–9 have met their own
exit gates. A narrated workaround, caller-supplied attack total, manually chosen damage result, or
generic threshold roll does not satisfy a missing D&D mechanic.

# D&D 2024 ruleset development roadmap

Status: **Features 1–13 verified; Feature 23 accepted; Feature 28 Slice 1 verified; Features 14–17 have full dependency plans; the remaining rows are planned backlog**
Last updated: 2026-08-20

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
| 14 | Exhaustion levels and their D20 Test and speed effects | Feature 13; E1 | **Revised plan** — Slice 0 first repairs ordinary-action event propagation, then 3 rules slices | Recovery pacing (Feature 33) and death at level 6 (Feature 17) |
| 15 | Damage types with resistance, immunity, and vulnerability in SRD order | Features 9, 13; E1 | **Planned in full** (4 slices) | Damage transfer, shared damage, absorption, half-damage-on-a-save |
| 16 | Temporary Hit Points and healing, including no-stacking rules | Features 6, 15; E1 | **Planned in full** (3 slices) | Timed regeneration, healing over time, and every source of healing |
| 17 | Dying: zero HP, unconsciousness, death saves, stabilization, and massive-damage death | Features 13–16; E1 | **Planned in full** (7 slices) | Resurrection, lingering injuries, and the 1d4-hour stable recovery (needs a clock) |
| 18 | Concentration: one effect at a time, damage-triggered save, and ending conditions | Features 4, 13, 15–17, 32; E1 and confirmed reaction composition | **Planned in full** — [plan](feature-18/FEATURE-18-DEPENDENCY-PLAN.md); runtime slices are blocked by the effect-source and composition boundaries | Metamagic and feature-specific exceptions |
| 19 | Reactions in play: opportunity attacks and triggered abilities | Features 8, 12, 20–21, 34; E1 and confirmed reaction composition/choice protocol | **Planned in full** — [plan](feature-19/FEATURE-19-DEPENDENCY-PLAN.md); runtime slices are blocked by Feature 20 spatial timing, Feature 21 geometry, Feature 34 sight, and dynamic reaction composition | Counterspell timing puzzles and held actions |
| 20 | Position and movement: speed, distance, difficult terrain, and reach as attack preconditions | Features 11–12, 23; E1 and confirmed derived-input composition | **Slice 1 verified** — [plan](feature-20/FEATURE-20-DEPENDENCY-PLAN.md), [receipt](feature-20/FEATURE-20-SLICE-1-RECEIPT.md); Slice 2 map/placement/reach ids need confirmation | Rendered grid, pathfinding, flanking, elevation/3D terrain |
| 21 | Cover and ranged combat: half/three-quarters cover, long-range Disadvantage, and ranged attacks in close combat | Features 6–8, 13, 20, 34; confirmed trusted attack-context composition | **Planned in full** — [plan](feature-21/FEATURE-21-DEPENDENCY-PLAN.md); Slice 1 (ranged profile range data) is next | Arbitrary/dynamic geometry and projectile physics |
| 22 | Unarmed and improvised combat: unarmed strike, grapple, shove, improvised weapons, and two-weapon fighting | Features 4, 8, 12–13, 15, 20, 23, 25; confirmed condition/spend composition | **Planned in full** — [plan](feature-22/FEATURE-22-DEPENDENCY-PLAN.md); Slice 1 (effect-free Unarmed Strike Damage resolver) is next | Wrestling subsystems and called shots |
| 23 | Equipment and inventory: item entities, containment, currency, and supported carrying rules | Feature 7 | **Accepted** — bounded physical inventory, carrying, equipment, currency, and fixed item activities are implemented; [plan](feature-23/FEATURE-23-DEPENDENCY-PLAN.md), [receipts](feature-23/) | Shopping economy, crafting, item durability |
| 24 | Armor, shields, and Armor Class derived from worn equipment | Features 6, 23 | Planned | Every natural-armor formula and magical stacking exception |
| 25 | Weapon properties and mastery: finesse, versatile, thrown, loading, ammunition, and 2024 mastery | Features 8, 23 | Planned | Weapon content beyond the SRD catalog |
| 26 | SRD species traits and their mechanical grants | Feature 2 | Planned | Non-SRD species and custom lineages |
| 27 | Classes and levels: features, hit dice, subclasses, proficiency grants, and multiclassing rules | Features 2, 26 | Planned | Non-SRD subclasses and homebrew progression |
| 28 | Character-origin foundation: language/tool proficiency state, then backgrounds, feats, and ability-score improvements | Feature 2 for Slice 1; Feature 27 for later grant resolution | Slice 1 verified — [plan](feature-28/FEATURE-28-DEPENDENCY-PLAN.md), [receipt](feature-28/FEATURE-28-SLICE-1-RECEIPT.md) | Non-SRD feats; language/tool checks, crafting, and feature effects |
| 29 | Attunement and magic items: SRD item set, attunement slots, and item-granted effects | Features 23; E1 | Planned | Artifacts, sentient items, and item creation |
| 30 | Guided character creation producing a legal playable sheet | Features 23–28 | Planned | Visual character-builder UX |
| 31 | Spellcasting resources: slots, known/prepared spells, spellcasting ability, save DC, and attack bonus | Feature 27 | Planned | Spell-point variants and non-SRD spells |
| 32 | Spell resolution: spell attacks, saves, targeting, areas, duration, and effects | Features 18, 20, 31 | Planned | Every SRD spell as initial content |
| 33 | Rests: short/long rest, Hit Dice, resource recovery, and expiry | Features 14, 27 | Planned | Gritty-realism and other optional rest variants |
| 34 | Vision/light, hiding, passive Perception, and encounter surprise | Features 13, 20 | Planned | Dynamic-lighting geometry |
| 35 | Monsters and stat blocks: creature data, CR, traits, actions, and encounter building | Features 6–9, 27 | Planned | Full SRD bestiary import as part of the capability |
| 36 | Advancement: XP or milestone and level-up through existing class features | Feature 27; Campaign C14; Character CH9 | **Slice 1 implemented:** explicit XP state and effect-free next-level eligibility; campaign awards and level-up remain blocked on C14, Feature 27, and CH9 — [plan](feature-36/FEATURE-36-DEPENDENCY-PLAN.md) | Automatic optimisation and respec tooling |
| 37 | D&D travel pace and time integration: apply an authorized pace policy and elapsed-time consequences to the existing world routes, itinerary, and clock | Features 20, 32, 33; core world travel/time; E1 | **Planned; blocked on source registration, generic route-distance ownership, and Features 32–33** — [plan](feature-37/FEATURE-37-DEPENDENCY-PLAN.md) | Rebuilding routes, maps, clocks, conveyances, weather simulation, or hex-crawl generation |
| 38 | Social interaction: attitude, influence checks, and non-trivial persuasion | Feature 3 | Planned | NPC personality simulation |

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

**Selection guardrail.** Feature 5 exposed a token-ranking collision, which is now corrected:
authored match phrases outrank incidental name and description tokens. Exact phrase collisions
remain an authoring risk, so every new mechanic still requires routing tests against adjacent
rules and a near-duplicate review before activation.

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

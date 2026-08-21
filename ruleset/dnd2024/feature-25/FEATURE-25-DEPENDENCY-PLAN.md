# Feature 25 dependency plan — weapon properties and mastery

Status: **Slice 1 verified; the consolidated normal/Thrown range vocabulary and static property/mastery facts are available. Slice 2 remains blocked on confirmed mastery-grant semantics.**
Last updated: 2026-08-21

## Execution rule

Slice 1 was implemented as a revision of Feature 7's single static profile owner. It updated the
closed profile schema, administrative writer and governing procedure, and the three canonical
fixtures; it created no new component, mechanic, event, subscription, fixture, migration, or
game state. Its evidence is recorded in `FEATURE-25-SLICE-1-RECEIPT.md`. All later slices remain
prospective and require their stated confirmation boundaries.

## Target capability

The game can determine a canonical weapon's 2024 properties and mastery property from immutable
profile data, then later apply only the consequences for which the required equipment, tactical,
turn, damage, and duration state is available.

### Included

- Static properties: Ammunition, Finesse, Heavy, Light, Loading, Reach, Thrown, Two-Handed, and
  Versatile.
- One static mastery property per weapon: Cleave, Graze, Nick, Push, Sap, Slow, Topple, or Vex.
- Held weapon/item eligibility, ammunition use, property-specific attack context, alternate
  Versatile damage, and mastery eligibility as distinct later readers/actions.
- The official SRD weapon table as the eventual canonical profile catalog.

### Excluded

- Improvised weapons and the Light extra attack parent (Feature 22), cover/ranged geometry
  (Feature 21), armor/Shield use (Feature 24), class grants/Extra Attack/Fighting Style
  (Feature 27), and magical weapons/ammunition (Feature 29).
- Weapon price/shopping/crafting/durability, disarm, weapon breakage, siege weapons, firearms
  beyond the SRD table, and a player equipment UI.
- A universal hand-slot inventory model. Feature 22's future manipulation-capacity seam provides
  capacity; this feature supplies property-defined use requirements.

## Official source basis

The registered source is source.dnd2024.srd-5.2.1: System Reference Document 5.2.1
(Wizards of the Coast LLC, 2025-05-01, CC-BY-4.0), [Equipment > Weapons and Properties, PDF
pp. 88–90](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf) and
[Equipment > Mastery Properties, PDF p. 89](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf).

- Ammunition requires a matching piece for each ranged attack, expends it, and needs a free hand to
  load a one-handed weapon. Loading limits one piece of ammunition per Action, Bonus Action, or
  Reaction used to fire it.
- Finesse chooses Strength or Dexterity for both attack and damage. Heavy creates attack
  Disadvantage below Strength 13 for melee or Dexterity 13 for ranged. Reach adds five feet.
- Thrown creates a ranged attack, permits drawing the weapon as part of that attack, and retains a
  melee weapon's ability modifier. Two-Handed requires two hands; Versatile changes melee damage
  when used in two hands.
- Mastery is usable only when a feature unlocks the selected weapon's mastery. Cleave/Nick use
  limited extra attacks; Graze uses a miss; Push/Topple move or condition a target; Sap/Slow/Vex
  create temporary target/next-attack consequences.

## Planning inventory and overlap result

| Inquiry | Repository evidence and conclusion |
| --- | --- |
| Static profile owner | Feature 7 owns dnd2024.weapon-profile, category, kind, attack abilities, base damage, and source. Properties belong on this existing immutable profile, not a weapon-instance component. |
| Range seam | Feature 21 already plans normal/long range as a weapon-profile migration for ranged weapons. Thrown range must extend that same static owner with a mode distinction, never a second component or caller range. |
| Attack/damage | Feature 8 owns D20 attack arithmetic and Feature 9 owns weapon damage/HP application. Finesse/Heavy/Versatile must revise or compose with those owners; no property may copy their dice, PB, or HP logic. |
| Equipment | Feature 23 owns item instances, custody, quantities, and held state. It supplies a weapon profile reference seam but has no property, hand, ammunition, or attack meaning. |
| Hands/Light | Feature 22 plans manipulation capacity, Attack-action history, and the player-facing Light extra attack. Feature 25 exposes property facts and hand-use requirements only. |
| Tactical state | Feature 20 owns reach, position, paths, forced movement, and effective Speed. Reach/Push/Slow/Cleave require its final tactical contracts. |
| Conditions/saves | Feature 13 owns Prone and its writer; Feature 4 owns the Topple Constitution save. Feature 25 consumes both only after their composition boundary is confirmed. |
| Timed effects | No durable temporary effect with source/target/expiry exists for Sap, Slow, or Vex. They cannot be stored as ad hoc conditions or caller circumstances. |
| Mastery grants | No mastery state exists. Feature 25 owns a closed learned-mastery record/reader; Feature 27 later supplies class-feature grants and changes. |

## Recursive dependency analysis

~~~text
Feature 25: weapon properties and mastery
├─ official table and rule definitions                         [implemented source basis]
├─ canonical weapon-profile owner                              [implemented: Feature 7]
├─ item custody / quantity / held state                        [implemented: Feature 23]
├─ attack and weapon damage owners                             [implemented: Features 8–9]
├─ ranged normal/long profile range                            [implemented: Feature 21 Slice 1]
├─ static property/mastery data migration                      [implemented: Slice 1]
├─ mastery eligibility state                                   [blocked: confirmed grant semantics]
├─ property-use / hand-capacity reader                         [blocked: Feature 22 capacity]
├─ Finesse/Heavy attack-context consumption                    [blocked: trusted attack context]
├─ Versatile chosen-grip damage                                [blocked: capacity + damage pipeline]
├─ ammunition consumption / Loading action ledger              [blocked: inventory + Feature 22 ledger]
├─ weapon Reach / Thrown tactical readers                      [blocked: Feature 20 + Feature 21]
├─ transient Sap/Slow/Vex effect lifecycle                     [missing duration state]
├─ Push/Topple/Cleave tactical resolution                      [blocked: Feature 20 + Feature 4/13]
└─ complete mastery actions                                    [blocked parent]
~~~

The lowest prerequisite is Feature 21 Slice 1's static range migration. Once its exact profile
shape is confirmed, Feature 25's static property/mastery migration becomes an independent,
effect-free catalog slice.

## Dependency and ownership decisions

1. Static properties and one mastery id extend weapon-profile. The closed profile uses canonical
   ordered tags plus structured fields only where a value is required: ammunition type/range,
   thrown range, and Versatile alternate damage. It does not store held state, attack choice,
   remaining ammunition, target, result, or mastery permission.
2. Range is one model. Normal/long range applies to a ranged weapon's normal mode; Thrown supplies
   a separate declared ranged mode on a melee or ranged weapon. Feature 21's range reader consumes
   those facts; neither feature stores distance on a creature or accepts it from a caller.
3. Feature 8 stays the attack arithmetic owner. Finesse becomes a validated ability-choice
   eligibility fact and Heavy yields one derived Disadvantage circumstance. A parent supplies
   trusted property evidence only after the composition contract proves it cannot be forged.
4. Feature 9 stays the base-damage owner. Versatile passes a validated two-hand damage expression
   only through a confirmed selected-grip context; it does not add a second damage roll or permit
   callers to submit damage dice.
5. Feature 23 owns physical ammunition and custody. An accepted ammunition attack consumes exactly
   one compatible stack unit atomically with its attack parent. Recovery after a fight waits for the
   clock and encounter-completion owners.
6. Feature 22 owns creature manipulation capacity and the turn attack ledger. Feature 25's reader
   reports what a property requires; it does not assume humanoid hands, invent slots, or decide the
   Light extra-attack schedule.
7. Mastery permission is creature state, not property data. A source-cited closed known-weapon
   list references canonical profile ids. It is the authorization seam Feature 27 later grants;
   it neither chooses class options nor silently gives every proficient creature mastery.
8. Temporary mastery outcomes require one general source/target/expiry effect owner. Sap, Slow,
   and Vex must not become new Feature-13 condition names. Push/Topple/Cleave also wait for
   Feature 20 movement/reach and Feature 4/13 save/condition boundaries.

## Confirmation boundary

| Decision | Required confirmation |
| --- | --- |
| Unified range schema | Feature-7 profile fields for ranged and Thrown modes, Feature-21 migration order, and source locators. |
| Property shape | Canonical tag order, structured ammunition/thrown/Versatile fields, property compatibility matrix, and full-table migration. |
| Weapon/item link | Immutable profile id/reference, physical instance eligibility, held/direct-custody semantics, and equipment draw/stow boundary. |
| Hand use | Feature-22 capacity result, two-hand/Versatile/free-hand rules, Shield interaction, and non-humanoid policy. |
| Trusted contexts | Exact Finesse/Heavy/grip/property evidence route into Features 8–9 without public forged modifiers. |
| Ammunition | Compatible definition identity, atomic stack consumption, loading/attack-action ledger, and post-fight clocked recovery. |
| Mastery state | Component/writer ids, profile-id vocabulary, missing semantics, source provenance, and Feature-27 grants. |
| Temporary effects | Generic source/target/duration identity and consumption order for Sap, Slow, and Vex. |
| Tactical masteries | Feature-20 push/reach/position contract, Feature-4 Topple save, Feature-13 Prone writer, and action-choice protocol. |

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 0 | Consolidated normal/Thrown range decision | **Verified in Slice 1.** | One static range vocabulary has an accepted owner; no rival field/model remains. |
| 1 | Static property/mastery profile migration | **Verified 2026-08-21.** | Canonical profiles record exact property/mastery facts with no behavior change. |
| 2 | Learned mastery state and reader | Slice 1 and grant semantics confirmed. | Closed state reports whether a creature may use a named profile mastery; it grants nothing by inference. |
| 3 | Property-use and hand-capacity reader | Slice 1 and Feature 22 capacity. | Effect-free reader reports legal hand/grip/load/held eligibility for one physical weapon use. |
| 4 | Finesse, Heavy, Versatile consumption | Slice 3 and trusted attack/damage context. | Exact ability eligibility, Heavy Disadvantage, and two-hand damage flow reach Features 8–9 without duplicated math. |
| 5 | Ammunition, Loading, and Thrown | Slices 3–4, inventory atomicity, range reader, turn ledger. | Compatible ammunition is consumed once; Loading limits the right action; Thrown moves/draws through validated equipment rules. |
| 6 | Reach and Light handoff | Slice 3 and Feature 20/22 tactical contracts. | Weapon reach extends only relevant attacks; Light facts safely enable Feature 22's distinct-weapon parent. |
| 7 | Mastery temporary-effect foundation | Slice 2 and general duration-effect owner. | Sap/Slow/Vex state has exact source, target, expiry, replacement, and read semantics. |
| 8 | Mastery resolution families | Slices 4–7 and tactical/save/condition contracts. | Graze, Nick, Push, Topple, Cleave, Sap, Slow, and Vex each apply only their authorized consequence and limits. |

## Slice 1 — static property/mastery profile migration

### Runtime artifacts

- Revision of Feature 7 weapon-profile schema, writer, governing procedure, and profile fixtures.
- A focused fresh-import migration test.
- No item instance, equipment state, Action/Bonus Action, ammunition consumption, hand capacity,
  attack/damage result, temporary effect, condition, movement, or mastery grant.

### Data and behavior

After Slice 0 confirms the shared range vocabulary, the profile has canonical property tags and
exact structured values for Ammunition, Thrown, and Versatile. It has exactly one mastery enum.
The migration must retain existing category/kind/ability/base-damage/source facts exactly and
populate the initial Dagger, Shortbow, and Battleaxe profiles from the SRD table:

- Dagger: Finesse, Light, Thrown 20/60, Nick.
- Shortbow: Ammunition (Arrow, 80/320), Two-Handed, Vex.
- Battleaxe: Versatile 1d10, Topple.

This is static data only. Existing Feature-8 attack and Feature-9 damage outcomes remain
byte-identical for the same profile selection, input, and seed.

### Acceptance matrix and exit gate

| Case | Exact assertion |
| --- | --- |
| Source facts | Dagger, Shortbow, and Battleaxe record the listed tags/structured fields/mastery exactly. |
| Closed compatibility | Invalid/duplicate/out-of-order tags, missing required structured field, inappropriate kind/range, wrong Versatile damage type/size, null mastery, or extra fields reject unchanged. |
| Migration | Existing profiles migrate once; category/kind/ability/base damage/source remain byte-identical; all catalog fixtures validate. |
| Non-behavior | No Action, custody, equipment, ammunition, attack, damage, AC, position, condition, or temporary-effect result changes. |
| Routing | Administrative property/profile phrases remain with Feature 7; no player-facing property or mastery action becomes selectable. |

Stop after catalog validation, focused/full repository checks, diff check, and a receipt pass. Do
not implement an attack parent, consume ammunition, set a hand state, or apply a mastery effect.

## Later-slice invariants

- A property can never be inferred from a weapon's name, category, damage, item mass, or held state.
- A caller cannot provide property tags, range, hand count, selected grip, ammunition id, target
  movement, mastery id, DC, circumstance, die, damage, or expiry as a substitute for authoritative
  profile/state facts.
- Loading's limit attaches to each Action, Bonus Action, or Reaction used to fire; it is not a
  general once-per-turn flag.
- Cleave and Nick have their stated once-per-turn limits; mastery permission is checked for every
  use and proficiency alone never grants it.
- Graze uses the attack's chosen ability modifier and no other damage increases. Push and Slow do
  not spend target movement. Topple's DC uses the attack's actual selected ability modifier/PB.
- Sap/Slow/Vex expire through one duration owner and cannot be represented by permanent AC/Speed,
  a Feature-13 condition, or an unscoped caller circumstance.

## Plan-quality audit

- One capability, official source, profile/equipment/attack/damage/tactical owner searches,
  recursive graph, closed static data, behavior boundaries, and one current prerequisite: yes.
- Static data is the first Feature-25 implementation slice after Feature 21 confirms range.
- No runtime artifact is created by this planning pass.

## Plan-change rule

Stop and revise if Feature 21 selects a different range representation, Feature 22 owns a
conflicting hand model, Feature 27 already defines mastery state, or the platform cannot route
trusted property contexts to Features 8–9. Do not create a second range component, copy static
properties to instances, treat proficiency as mastery, store temporary mastery results as
conditions, or bypass physical ammunition/turn/tactical owners.

# Feature 8 dependency plan — Weapon attack rolls against Armor Class

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Complete — Slice 1 verified through file-first catalog import**
Last updated: 2026-08-19

## Execution rule

The completed slice was authored in `catalog/`, reviewed there, imported with `roleplay import
catalog`, and checked with `roleplay verify catalog`. Its runtime authority is the imported
contract and mechanic; this plan retains the boundary and reproducible verification evidence.

## Target capability

Given a creature, a target creature, and a canonical weapon profile, resolve one seeded weapon
attack roll against the target's authoritative Armor Class. The result explains the chosen die,
ability modifier, conditional Proficiency Bonus, total, hit/miss, and natural-20 Critical Hit
classification without dealing damage or mutating either creature.

### Included

- One effect-free, deterministic seeded weapon-attack resolver.
- D20 normal, Advantage, Disadvantage, non-stacking, and cancellation behavior using the existing
  Feature 3 `rollCircumstances` convention.
- A selected permitted ability, level-derived Proficiency Bonus only when the actor has the
  selected weapon profile's category proficiency, comparison with target final AC, and natural
 20/natural 1 precedence.
- A structured result suitable for a later Feature 9 damage resolver to consume as evidence, not
  as mutable attack state.

### Excluded

- Weapon profiles and category proficiency recording (Feature 7); Feature 8 consumes but never
  creates, changes, or defaults either state.
- Weapon ownership, holding/equipping, ammunition, range, cover, reach, thrown attacks, loading,
  Heavy, Light, mastery, magical bonuses, class features, spell attacks, unarmed strikes,
  opportunity attacks, multiattack, actions/turn economy, or target legality.
- Damage dice, damage modifiers, Critical Hit extra dice, damage type interaction, Hit Point
  application, healing, conditions, and all other effects (Feature 9 and later).
- A generic attack component, persisted attack result/history, direct database writes, or changes
  to the unrelated legacy `homer` `stats.armorClass` data.

## Official source basis

`source.dnd2024.srd-5.2.1` identifies the official SRD 5.2.1, published 2025-05-01 under
CC-BY-4.0.

- *Playing the Game > D20 Tests*, PDF pages 6–7: roll one d20 (or two and select high/low for
  Advantage/Disadvantage), add the relevant ability modifier and applicable Proficiency Bonus,
  then compare the total to the target number; both Advantage and Disadvantage cancel and never
  stack.
- *Playing the Game > D20 Tests > Attack Rolls*, PDF page 7: an attack hits when it equals or
  exceeds the target's Armor Class; weapon melee attacks normally use Strength, ranged weapon
  attacks use Dexterity, and a permitted exception such as Finesse can use another listed ability.
- *Playing the Game > D20 Tests > Attack Rolls > Proficiency Bonus*, PDF page 7 and
  *Equipment > Weapons > Weapon Proficiency*, PDF page 89: add Proficiency Bonus only for a
  weapon with which the attacker is proficient.
- *Playing the Game > D20 Tests > Attack Rolls > Rolling 20 or 1*, PDF page 7: a natural 20 hits
  regardless of modifiers/AC and is a Critical Hit; a natural 1 misses regardless of modifiers/AC.
  The later critical-damage consequence is explicitly deferred to Feature 9.

## Verified and pending dependencies

| Dependency | Evidence / required state |
| --- | --- |
| File-first workflow and source registry | Implemented; catalog files are authoritative development source and `source.dnd2024.srd-5.2.1` exists. |
| Ability scores and level-derived Proficiency Bonus | Features 1–2 verified. The resolver derives modifier from `dnd2024.abilities` and PB from `dnd2024.character-level`; it accepts neither as input. |
| D20 circumstance policy | Feature 3 verified in ability checks: seeded validation-first rolls, one die normally/mixed, two dice for Advantage/Disadvantage, and high/low selection. |
| Final Armor Class | Feature 6 verified: target `dnd2024.armor-class` is the only AC source. No legacy `stats.armorClass` may be read. |
| Canonical weapon profile | Feature 7 Slice 1 verified: `dnd2024.weapon-profile` entities supply category/kind, allowed attack abilities, and source facts. |
| Weapon-category proficiency | Feature 7 Slice 2 verified: `dnd2024.weapon-proficiencies` supplies actor categories; absent/corrupt is invalid, while `[]` means known nonproficient. |
| Damage/consequences | Deliberately absent; Feature 9 owns damage rolls and Hit Point changes. |

## Recursive dependency analysis

```text
Feature 8: one weapon attack roll against final Armor Class
├─ file-first catalog workflow, source registry, effects model            [implemented]
├─ actor ability scores and character level / proficiency-bonus derivation [implemented]
├─ D20 circumstance and seeded-replay convention                           [implemented]
├─ target authoritative Armor Class                                         [implemented: Feature 6]
├─ canonical weapon profile                                                 [verified: Feature 7 Slice 1]
├─ actor Simple/Martial proficiency state                                   [verified: Feature 7 Slice 2]
└─ attack resolution                                                        [verified: Feature 8 Slice 1]
   ├─ closed role/input and state validation
   ├─ select die, ability modifier, and conditional proficiency bonus
   ├─ natural 20/1 precedence and AC comparison
   └─ explanatory, zero-effect result envelope
```

Every internal Feature 8 leaf was delivered by the single resolver slice. It consumes Feature 7's
verified canonical facts rather than substituting caller-supplied weapon facts or proficiency.

## Data ownership and boundary decisions

1. `dnd2024.weapon-profile` remains Feature 7's static entity component. Feature 8 reads only its
   `category` and `attackAbilities`; it must not duplicate profile facts, infer a profile by name,
   or accept a caller-supplied category/ability list.
2. `dnd2024.weapon-proficiencies` remains Feature 7's actor component. Feature 8 derives one
   Boolean from `profile.category in subject.categories`; it does not write, grant, or treat
   missing state as empty.
3. `dnd2024.abilities` and `dnd2024.character-level` remain actor state. The resolver derives the
   selected ability modifier and level-band Proficiency Bonus. No `abilityModifier`, `level`, or
   `proficiencyBonus` input is allowed.
4. `dnd2024.armor-class` remains target state owned by Feature 6. The resolver reads its final AC;
   no input `ac`, `targetNumber`, calculation mode, armor, or shield data is permitted.
5. The selected weapon profile is the required `weapon` role. That establishes canonical facts but
   not physical possession; ownership/equipping is a future model and must not be faked here.
6. The attack outcome is transient action result data. No `dnd2024.attack` component or effect is
   created: Feature 9 may receive a caller-confirmed hit/critical result or define a composed
   action later, but Feature 8 never changes HP.
7. `rollCircumstances` retains the Feature 3 closed convention. It expresses only Advantage and
   Disadvantage causes; caller-provided bonuses, penalties, rerolls, expanded critical ranges, or
   automatic outcomes remain future feature-owned facts.

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Weapon attack resolver | Feature 7 Slices 1–2 are imported, verified, and reviewed | Catalog-authored contract and resolver read all canonical inputs, pass every correctness/negative/routing/replay assertion below, return zero effects, import from a fresh database, and leave no fixtures. |

## Slice 1 — Resolve a weapon attack against AC

### Runtime artifacts

- New `procedure.mechanic.dnd2024.weapon-attack` contract under
  `ruleset.dnd2024.core.gameplay.weapon-attacks`.
- New active `mechanic.dnd2024.weapon-attack` under the same category and scope
  `dnd2024-srd-5.2.1`.
- No component definition, writer, entity, or database schema change.

### Governing contracts and source locator

Re-read `procedure.system.create-feature`, `procedure.action.run`, the Feature 3 ability-check
contract/mechanic, Feature 7's completed profile/proficiency contracts, and Feature 6's
Armor-Class component immediately before authoring. Cite `source.dnd2024.srd-5.2.1` with the
heading-plus-page locator `Playing the Game > D20 Tests > Attack Rolls`, PDF pages 6–7; cite
`Equipment > Weapons > Weapon Proficiency`, PDF page 89 for category proficiency.

### Role, input, and required-state contract

The resolver requires exactly these roles:

- `subject`: attacker with valid `dnd2024.abilities`, `dnd2024.character-level`, and
  `dnd2024.weapon-proficiencies`.
- `target`: defender with valid `dnd2024.armor-class`.
- `weapon`: canonical entity with valid `dnd2024.weapon-profile`.

The input is a closed object:

```json
{
  "ability": "str|dex",
  "rollCircumstances": [
    { "kind": "advantage|disadvantage", "source": "nonempty human-readable reason" }
  ]
}
```

`rollCircumstances` may be omitted only if the existing Feature 3 convention permits its omitted
canonical form; otherwise it is required exactly as that contract specifies. The selected `ability`
must occur exactly once in the weapon profile's canonical `attackAbilities` list. The action seed
is transport metadata, never input data. Reject unknown roles, missing/invalid/corrupt required
components, noncanonical profile fields, an absent profile/proficiency/AC, any other ability,
null/scalar/array root, extra fields, direct AC/profile/category/level/PB/modifier/die/total/hit/
critical/damage/effect fields, or malformed circumstances before consuming randomness.

Known empty category proficiency is valid and means no PB; it is not an error. No target ability,
level, or HP state is required.

### Resolution and recording behavior

1. Validate the closed input, all three roles, and every required component before rolling.
2. Derive the ability modifier from subject ability score and level-band PB from character level
   using the established Feature 1–2 formulas.
3. Determine `proficient` solely from the selected profile's `category` and the subject's canonical
   category list. Set `proficiencyBonusApplied` to derived PB or `0` accordingly.
4. Resolve `rollCircumstances` exactly as Feature 3: any Advantage plus any Disadvantage cancels
   to normal and rolls one d20; otherwise roll one normally, or two and select high/low. Retain all
   rolled dice in order and state the selected die/mode.
5. Compute `total = selectedDie + abilityModifier + proficiencyBonusApplied`.
6. Apply precedence after the total is available for explanation: selected natural 20 means hit and
   `critical: true`; selected natural 1 means miss and `critical: false`; otherwise hit is
   `total >= targetArmorClass` and `critical: false`.
7. Return result data only. Apply `effects: []`; do not write an action, attack, damage, HP, or
   circumstance state.

### Result envelope

The result must identify `test: "weapon-attack"`, source locator, subject/target/weapon ids,
weapon category, selected ability, target Armor Class, `proficient`, derived and applied PB,
ability modifier, all d20 rolls, selected d20, resolved roll mode/circumstances, total, `hit`, and
`critical`. It must make the automatic natural-20/natural-1 reason explicit so a consumer never
mistakes an AC comparison for the cause. It contains no damage total, damage dice result, HP
delta, mutation request, or extra critical-damage value.

### Invariants, failure behavior, and non-goals

- Equivalent valid actions with the same seed and equivalent state return byte-identical result
  data and zero effects. Invalid actions consume no RNG and leave all role entities byte-stable.
- A natural 20 always hits/critical and a natural 1 always misses, irrespective of AC/modifiers;
  no feature may silently introduce expanded critical ranges or a critical-damage roll.
- A category-proficient weapon adds the subject's PB once; a nonproficient weapon adds zero. PB is
  never added twice and is never inferred from the target or an input field.
- Finesse selection is limited to the profile's stored list. The seed fixture must prove Dagger
  permits Strength and Dexterity, Shortbow rejects Strength, and Battleaxe rejects Dexterity.
- This slice does not decide whether an attack is legal in space/time, whether a weapon is held,
  whether ammunition exists, whether damage happens, or whether a hit has a consequence.

### Slice 1 implementation sequence

Feature 7 passed its full exit gate before authoring. The contract and source were added in
`catalog/`, with a fresh-database integration test that imports completed Feature 6–7 state. The
identical catalog dry-run, import, and verification are recorded below; disposable test fixtures
exist only in the temporary test database. The full suite and `git diff --check` then passed.

### Slice 1 acceptance matrix

- Fresh-import gate finds exactly one attack contract/mechanic plus existing Feature 6/7 input
  artifacts; it does not create an attack component, action state, or damage writer.
- A proficient Simple Dagger attacker at levels 4/5/16/17 proves PB application of +2/+3/+5/+6;
  identical nonproficient state proves the exact PB delta and unchanged ability/AC arithmetic.
- Dagger succeeds with each permitted Strength/Dexterity selection and exposes the selected
  modifier; Shortbow rejects Strength and Battleaxe rejects Dexterity before a roll. Profile
  category, kind, damage, and source are unchanged by every action.
- Normal results prove `total == AC` hits and adjacent below/above totals miss/hit. A target's
  authoritative `dnd2024.armor-class` is used even if unrelated `stats.armorClass` exists.
- Fixed seeds prove normal, Advantage, Disadvantage, duplicate-cause non-stacking, mixed
  cancellation, high/low selection, tie handling, and exact seeded replay using the Feature 3
  convention.
- A selected natural 20 hits and is critical against an otherwise impossible AC; a selected natural
  1 misses against an otherwise automatic-hit AC. Both include the normal explanatory total but
  include no damage result/effect/HP change.
- Valid actions report `effects: []`; compare before/after bytes for subject, target, and weapon.
  Repeated seeded actions are idempotent with respect to state.
- Reject missing/wrong roles; null/scalar/array/extra input; unknown ability; invalid/extra/unknown
  circumstance fields; caller-supplied derived values/outcomes/damage; absent/corrupt abilities,
  level, profile, proficiency, or AC; invalid profile ability/category; and noncanonical
  proficiency state. Assert no RNG use, no effects, and byte-stable state for every case.
- Intent-routing tests distinguish weapon attack language from Feature 3 ability checks, Feature 4
  saving throws, Feature 5 Initiative, Feature 6 AC/HP writers, Feature 7 profile/proficiency
  writers, generic dice, and future damage mechanics.

### Slice 1 exit gate

All matrix rows have objective structured result/effect/state evidence; the resolver and its
contract import/read back from a fresh database; catalog verification is clean; no disposable
fixture remains; full repository tests and diff check pass; and the plan records operation ids or
equivalent reproducible evidence. Stop after that gate. Feature 9 remains a separately planned
owner of damage and HP consequences.

## Completion evidence — 2026-08-19

- Added catalog-authored `procedure.mechanic.dnd2024.weapon-attack` and active
  `mechanic.dnd2024.weapon-attack`; no component, entity, writer, or schema was introduced.
- `CatalogFeature8Tests` imports a complete catalog copy into a fresh database and proves result
  shape/routing, zero effects and exact unchanged subject/target/weapon state; derived PB bands
  +2/+3/+5/+6; proficient versus known-empty delta; Dagger Strength/Dexterity acceptance;
  Shortbow Strength and Battleaxe Dexterity rejection; final-AC equality/adjacency; normal,
  Advantage, Disadvantage, cancellation, high/low selection and replay; natural 1/20 precedence;
  malformed/derived input and corrupt-AC rejection.
- Catalog dry-run reported exactly **2 new** records (`mechanic.dnd2024.weapon-attack` and
  `procedure.mechanic.dnd2024.weapon-attack`) and **65 unchanged**. Import created **2** and
  updated **0**. `roleplay verify catalog` then reported **67 unchanged**.
- Focused Feature 8 integration tests passed **2/2**. Full repository tests passed **299/299**.
  `git diff --check` passed with only existing line-ending conversion warnings.

## Plan-quality audit

The completed plan used concrete official SRD heading/page locators, assigns every input and
derived value an owner, defines a closed action contract and natural-roll precedence, prohibits
persisted attack/damage state, and has a fresh-import plus adversarial acceptance matrix. The
catalog artifacts and database import are now independently recorded above.

## Plan-change rule

Stop and revise if Feature 7's final profile/proficiency contract differs from the stated fields,
an existing attack owner appears, target selection needs an unmodelled spatial/encounter rule, or
a requested weapon needs a property that affects the attack roll. Do not bypass a missing
dependency with caller-supplied weapon facts, AC, PB, profile/proficiency defaults, a generic
attack component, or a C# rules helper.

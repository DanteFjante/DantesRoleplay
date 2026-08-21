# Feature 7 dependency plan — Minimal weapon profiles and weapon proficiency

Status: **Complete — Slices 1–2 verified**
Last updated: 2026-08-21

## Execution rule

Runtime content is authored in `catalog/`, reviewed there, imported with `roleplay import catalog`,
and checked with `roleplay verify catalog`. Both Feature 7 slices were implemented through that
path. Stop here; Feature 8 is a separate planned feature and requires its own implementation pass.

## Target capability

A GM can identify a small, source-cited set of weapons and record whether a creature is proficient
with Simple and/or Martial weapons, giving later attack and damage rules authoritative inputs
without inventing weapon facts or proficiency on each roll.

### Included

- A reusable weapon-profile component on canonical ruleset weapon-profile entities.
- A deliberately small SRD profile seed set: Dagger, Shortbow, and Battleaxe, extended with the
  source-cited Greatsword, Flail, and Javelin profiles needed by the accepted first-build fixture.
- Closed, source-cited record/correction paths for weapon profiles and weapon-category proficiency.
- Category membership (`simple`, `martial`), profile category/kind, permitted attack abilities,
  and one weapon damage expression/type as stored facts for later consumers.

### Excluded

- Attack rolls, Armor Class comparison, hit/miss, natural 20/1, Advantage/Disadvantage, and
  Proficiency Bonus application (Feature 8).
- Damage rolls, damage application, critical damage, resistance, immunity, vulnerability, healing,
  and Hit Point changes (Feature 9 and later).
- A complete equipment catalogue, physical ownership/equipping, carrying capacity, cost, weight,
  ammunition, range/cover, loading, reach, thrown use, Light extra attacks, mastery, improvised
  weapons, magical weapons, class/background grants, or individual weapon proficiency exceptions.
- Armor, shields, class identity, and database schema changes.

## Official source basis

`source.dnd2024.srd-5.2.1` identifies the official SRD 5.2.1, published 2025-05-01 under
CC-BY-4.0.

- *Equipment > Weapons*, PDF page 89: every weapon is Simple or Martial and Melee or Ranged; the
  table supplies its damage and damage type.
- *Equipment > Weapons > Weapon Proficiency*, PDF page 89: anyone may wield a weapon, but adding
  Proficiency Bonus to its attack roll requires proficiency; player-character features can grant
  it, while a monster is proficient with weapons in its stat block.
- *Equipment > Weapons > Properties > Finesse*, PDF page 89: a Finesse weapon permits Strength or
  Dexterity for both attack and damage. Feature 7 stores the permitted ability set as a profile
  fact; Feature 8 will select and apply one to an attack roll.
- *Equipment > Weapons table*, PDF page 91: Dagger, Shortbow, and Battleaxe provide the small
  profile seed set and cover Simple/Martial plus Melee/Ranged cases. The same table supplies the
  static Greatsword, Flail, and Javelin extension used by the first-build fixture.

## Verified existing dependencies

| Dependency | Evidence |
| --- | --- |
| File-first feature workflow | `procedure.system.create-feature` requires catalog authoring, dry-run/import, verify, and committing catalog, manifest, and database together. |
| Source attribution | Catalog entity `source.dnd2024.srd-5.2.1` supplies official URLs, CC-BY attribution, and heading-plus-page locators. |
| State model | `procedure.world.model` and `procedure.world.change` provide reusable components, entities, containment, and atomic effects. |
| Safe administrative writers | Feature 6's AC/HP catalog writers prove closed record/correct input and one `component.add` or `component.set` effect work through actions. |
| Later attack prerequisites | Features 1–3 supply abilities, derived level-based Proficiency Bonus, and the shared D20 circumstance convention; Feature 6 supplies final AC. |
| Ownership search | Current catalog components, procedures, and mechanics contain no weapon, equipment, weapon-proficiency, or attack-profile owner. `homer`'s unrelated legacy `stats.armorClass` is not a D&D weapon rule. |
| Import guard | `roleplay verify catalog` reports 56 matching catalog/database records after Feature 6. |

## Recursive dependency analysis

```text
Feature 7: minimal weapon facts and proficiency state
├─ source registry, component/effect model, file-first import       [implemented]
├─ final AC and future D20 conventions                               [implemented dependencies]
├─ minimal canonical weapon profiles                                 [verified: Slice 1]
│  ├─ dnd2024.weapon-profile definition and safe writer             [verified]
│  └─ Dagger, Shortbow, Battleaxe seed plus Greatsword, Flail,
│     and Javelin extension entities                                 [verified]
├─ actor weapon-category proficiency state                           [verified: Slice 2]
│  ├─ dnd2024.weapon-proficiencies definition and safe writer       [verified]
│  └─ canonical Simple/Martial membership                            [verified]
└─ weapon attack roll against AC                                     [next parent: Feature 8]
   ├─ actor abilities/level, weapon profile, proficiency, target AC [implemented after Slices 1–2]
   └─ attack resolution and D20 policy                               [Feature 8]
```

## Dependency and ownership decisions

1. A canonical SRD weapon profile is an entity because it is a named rules object that can be
   selected by id and read by later mechanics. It carries `dnd2024.weapon-profile`; it is not a
   character inventory item or a copied object in an actor component.
2. `dnd2024.weapon-profile` owns only static profile facts: Simple/Martial category, Melee/Ranged
   kind, the canonical allowed attack-ability list, and its base damage dice/type. It does not own
   reach, range, ammunition, properties, mastery, whether it is held, a chosen ability, an attack
   total, or damage dealt.
3. `dnd2024.weapon-proficiencies` belongs on a creature and owns a complete canonical subset of
   `simple` and `martial`. Missing state means unknown and must fail a later attack; `[]` means the
   creature is known to have neither category. It does not store a Proficiency Bonus, individual
   weapon ids, class source, or an attack result.
4. The allowed attack-ability list is a static profile fact, not caller input and not an actor
   modifier. It is `[str,dex]` for Dagger, `[dex]` for Shortbow, and `[str]` for Battleaxe. Feature
   8 will validate the caller's one chosen ability against this list and derive the modifier.
5. Base damage dice/type belong in a profile so Feature 9 can roll a selected weapon's actual
   damage without a caller-supplied die. Feature 7 never rolls or applies it.
6. The profile seed is intentionally small. More SRD weapons are additive data in a later reviewed
   catalogue expansion; they must use this owner, not create per-weapon component definitions or
   mechanics.
7. Physical possession/equipping requires a future ownership model (containment or relationship)
   and is not mocked with a caller-provided profile or a copied actor profile.

### Starting-equipment extension

On 2026-08-21, the confirmed Human Soldier Fighter fixture added the source-cited static profiles
`weapon.dnd2024.greatsword`, `weapon.dnd2024.flail`, and `weapon.dnd2024.javelin`. They use the
existing closed profile component: Greatsword is Martial/Melee, 2d6 Slashing, Heavy/Two-Handed,
and Graze; Flail is Martial/Melee, 1d8 Bludgeoning, and Sap; Javelin is Simple/Melee, 1d6 Piercing,
Thrown 30/120, and Slow. This is a catalog-data extension only. It grants no weapon to an actor
and enables no mastery, range, property, or attack behavior beyond existing owners.

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Minimal weapon profiles | Plan reviewed | **Verified 2026-08-19** — file-authored profile definition, contract, writer, and three canonical profile entities import/read back through the fresh-database gate and the live catalog. |
| 2 | Weapon-category proficiency | Slice 1 reviewed and verified | **Verified 2026-08-19** — file-authored proficiency definition, contract, and writer pass empty/membership, correction, state, routing, replay, cleanup, and catalog verification checks. |

## Slice 1 — Minimal canonical weapon profiles

### Runtime artifacts

- New `dnd2024.weapon-profile` component definition.
- New `procedure.mechanic.dnd2024.weapon-profile` and active
  `mechanic.dnd2024.weapon-profile.write` in `ruleset.dnd2024.core.data.weapon-profile`, scope
  `dnd2024-srd-5.2.1`.
- New canonical entities `weapon.dnd2024.dagger`, `weapon.dnd2024.shortbow`, and
  `weapon.dnd2024.battleaxe`, each carrying exactly one profile component.

### Governing contracts and source locator

Re-read `procedure.system.create-feature`, `procedure.world.model`, `procedure.world.change`,
`procedure.mechanic.write`, and `procedure.action.run` immediately before authoring. Use
`source.dnd2024.srd-5.2.1`, `Equipment > Weapons`, PDF pages 89–91.

### Data/input contract and required state

Profile data is a closed object with `category`, `kind`, `attackAbilities`, `damage`, and fixed
`sourceRef`. `category` is `simple|martial`; `kind` is `melee|ranged`; `attackAbilities` is a
nonempty duplicate-free canonical array using only `str` and `dex` (canonical order `str`, then
`dex`); and `damage` is a closed `{count, faces, type}` object using positive safe-integer count,
one of 4/6/8/10/12 faces, and `bludgeoning|piercing|slashing`. The fixed source locator is
`Equipment > Weapons`.

The writer requires role `weapon`. Its input is exactly the profile facts without `sourceRef`; it
has `record|correct` mode. Record requires no profile and proposes `component.add`; correct
requires a valid existing profile and proposes `component.set`. Missing/corrupt profile state is
never defaulted or silently repaired. The canonical entities must match: Dagger (Simple/Melee,
`[str,dex]`, 1d4 Piercing), Shortbow (Simple/Ranged, `[dex]`, 1d6 Piercing), and Battleaxe
(Martial/Melee, `[str]`, 1d8 Slashing).

### Resolution/recording behavior

Validate the complete closed input and existing state before an effect. Canonicalize/validate
ability order rather than accepting reordered or duplicated arrays. Return profile facts plus
previous profile/null and one effect. Use no randomness. Do not infer a profile from a name or
accept a caller-selected attack modifier, range, property, outcome, or damage result.

### Result and effects

Return mode, category, kind, attackAbilities, damage, previous profile/null, and fixed source
reference. Apply exactly one `component.add` or `component.set` on `weapon`; no effects on a
creature and no profile entity creation from an action. The three canonical entities are catalog
world fixtures, imported with their component state.

### Invariants, failure behavior, and non-goals

Profiles remain static source data; no caller can change a source locator, add unknown damage
types/abilities, or create an incomplete profile. This slice does not establish ownership,
equipping, range, ammunition, mastery, attack ability choice, attack roll, damage, or HP change.

### Slice 1 implementation sequence

Search all candidate ids and intent phrases; author component/schema, contract, writer markdown/
source, and the three entities in `catalog/`; add an import-from-fresh-database test. Run catalog
dry-run, inspect exactly the expected records, import identical files, verify catalog, read back
each artifact, exercise record/correct and negative actions, remove or restore disposable fixtures
through ordinary effects, run full tests and `git diff --check`, record evidence, and stop.

### Slice 1 acceptance matrix

- Import a copied catalog into a fresh database and assert all three profile entities and the
  component/procedure/mechanic exist.
- Read each canonical profile and assert category/kind/ability list/damage tuple/source exactly.
- Record/correct a disposable profile; assert one add then one set and exact returned/attached
  state. Replaying equivalent actions on equivalent fixtures yields byte-identical profile data.
- Reject missing/null/array/scalar input, unknown/wrong-case enums, zero/fractional/unsafe dice,
  empty/reordered/duplicated/unknown abilities, extra/source/property/range fields, duplicate
  record, absent correction, and corrupt existing state; assert no effect and unchanged bytes.
- Prove player phrases route to the profile writer rather than AC, HP, ability, saving-throw, or
  Initiative mechanics; prove profile writes do not mutate a creature.

### Slice 1 exit gate

All profile artifacts and the three canonical entities are imported/read back, every acceptance
assertion passes, no fixture remains, catalog verification is clean, the full suite and diff check
pass, and the plan records concise evidence. Stop for review.

### Slice 1 implementation evidence — 2026-08-19

- Catalog-authored `dnd2024.weapon-profile`, `procedure.mechanic.dnd2024.weapon-profile`, and
  `mechanic.dnd2024.weapon-profile.write` were added with canonical Dagger, Shortbow, and Battleaxe
  entities.
- Catalog dry run reported exactly six new records and 56 unchanged records. The identical import
  created six records; `roleplay verify catalog` then reported 62 unchanged records.
- `CatalogFeature7Tests` imports a copied catalog into a fresh database and verifies canonical
  profiles, record/correct add/set behavior, routing, replay, malformed input, absent correction,
  and corrupt-state rejection without mutation.
- Full repository verification passed: 296/296 tests, with `git diff --check` clean.

Slice 1 is complete. Stop here for review; Slice 2 is the next and only authorized Feature 7 pass.

## Slice 2 — Weapon-category proficiency state

### Runtime artifacts

- New `dnd2024.weapon-proficiencies` component definition.
- New `procedure.mechanic.dnd2024.weapon-proficiencies` and active
  `mechanic.dnd2024.weapon-proficiencies.write` in
  `ruleset.dnd2024.core.data.weapon-proficiencies`, scope `dnd2024-srd-5.2.1`.

### Governing contracts and source locator

Re-read the Slice 1 artifacts plus `procedure.system.create-feature`, `procedure.world.model`,
`procedure.world.change`, `procedure.mechanic.write`, and `procedure.action.run` before authoring.
Use `source.dnd2024.srd-5.2.1`, `Equipment > Weapons > Weapon Proficiency`, PDF page 89.

### Data/input contract and required state

Data is a closed object containing `categories` and fixed `sourceRef`. `categories` is a
duplicate-free canonical subset of `[simple,martial]`; absent is invalid/unknown and `[]` is known
no category proficiency. Input is exactly `{"mode":"record"|"correct","categories":[...]}`.
The writer requires role `subject`, rejects caller source, level, Proficiency Bonus, weapon id,
attack, modifier, total, result, class, damage, or effects, and validates existing state before a
correction.

### Resolution/recording behavior

Validate and canonicalize category order (`simple`, then `martial`). Record requires absence and
returns one `component.add`; correct requires valid presence and returns one `component.set`. It
returns canonical categories, previous categories/null, and fixed source reference. It never
derives or applies Proficiency Bonus, and uses no randomness.

### Result and effects

Exactly one component effect on `subject`; result data has mode, canonical categories, previous
categories/null, and source. No profile, AC, HP, level, or equipment state changes.

### Invariants, failure behavior, and non-goals

This is category membership only. No class/background grant, individual weapon exception, weapon
mastery, temporary proficiency, attack, or damage behavior may be folded in. Later Feature 8 must
derive level-based Proficiency Bonus and compare a selected profile's category to this state; it
must reject absent/corrupt state rather than treating it as nonproficiency.

### Slice 2 implementation sequence

Repeat file-first search/author/dry-run/import/verify/readback flow. Use disposable actors for
empty, singleton, both-category, absent, and corrupt states; restore/remove them through ordinary
effects. Run the full suite and diff check, record evidence, and stop.

### Slice 2 acceptance matrix

- Fresh-catalog import creates the component, contract, and writer while preserving Slice 1 profile
  entities.
- Record empty, Simple, Martial, and both categories; assert canonical order, exact source, one
  effect, and byte-identical replay on equivalent fixtures.
- Correct Simple to both; assert previous/current values and that profile, AC, HP, and level bytes
  are untouched.
- Reject missing/null/array/scalar root, wrong-case/unknown/duplicate/reordered categories,
  source/class/weapon/proficiencyBonus/attack/damage/effect extras, duplicate record, absent
  correction, and corrupt state; assert no effects and unchanged state.
- Assert routing selects this writer—not the existing skill/save proficiency, profile, AC, or HP
  writers—and that a future attack fixture sees missing proficiency as a failure, not empty state.

### Slice 2 exit gate

All artifacts are imported/read back; the profile and proficiency integration gate passes; every
matrix row has objective result/effect/state evidence; catalog verification is clean; full tests
and diff check pass. Feature 7 is then complete, but Feature 8 remains separately planned.

### Slice 2 implementation evidence — 2026-08-19

- Catalog-authored `dnd2024.weapon-proficiencies`,
  `procedure.mechanic.dnd2024.weapon-proficiencies`, and
  `mechanic.dnd2024.weapon-proficiencies.write` were added as the single owner of Simple/Martial
  category membership.
- Catalog dry run reported exactly three new records and 62 unchanged records. The identical import
  created three records; `roleplay verify catalog` then reported 65 unchanged records.
- `CatalogFeature7Tests` imports a copied catalog into a fresh database and verifies explicit-empty,
  Simple, Martial, and both-category state; correction; routing; replay; protected AC/HP/level
  state; malformed input; absent correction; and corrupt-state rejection without mutation.
- Full repository verification passed: 297/297 tests, with `git diff --check` clean.

Feature 7 is complete. Stop here for review; Feature 8 weapon-attack resolution remains separately
planned.

## Plan-quality audit

The target and non-goals are explicit; the official source and PDF locators are concrete; catalog
search establishes no current owner; state versus derived/transient values are separated; both
leaves have safe writers, closed contracts, source ownership, testable effects, and full
acceptance matrices. Both slices are verified; Feature 8 remains a separate next assignment.

## Plan-change rule

Stop and revise if an existing weapon/equipment owner appears, a requested weapon needs an
unmodelled property, a profile field duplicates a future attack result, or physical ownership is
needed before Feature 8. Do not bypass that dependency with caller-supplied weapon facts,
individual per-weapon components/mechanics, copied profiles on actors, a generic damage roll, or a
C# rules helper.

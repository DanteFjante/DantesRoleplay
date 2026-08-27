# D&D 2024 CC-MVP-C1 implementation - all-class level-1 creation models

Status: **accepted**
Feature/slice: **D&D 2024 character creation / CC-MVP-C1**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency leaf: [basic character-creation MVP](DND2024-CHARACTER-CREATION-MVP-PLAN.md)
Ruleset alignment: **dnd2024-owned**
Source ID and locators: `source.dnd2024.srd-5.2.1`; class Core Traits and level-1 feature
tables for Barbarian (PDF p. 28), Bard (pp. 31-32), Cleric (p. 36), Druid (p. 41), Fighter
(pp. 47-48), Monk (pp. 49-50), Paladin (p. 53), Ranger (pp. 57-58), Rogue (pp. 61-62),
Sorcerer (pp. 64-65), Warlock (pp. 70-71), and Wizard (pp. 77-78)
Outcome: model every SRD class at level 1 and allow the accepted basic creator to create any of
those classes without granting mechanics that do not yet have state/effect owners.
Exclusions: class levels 2-20, subclasses, spell selection/casting, class-resource behavior, armor
or equipment application, tool selection, restricted-martial weapon resolution, multiclassing,
background choice, UI discovery, migration, and a new public protocol kind.
Allowed areas: D&D application class content/components/mechanics/procedures, the existing D&D
acceptance-test harness, this slice's roadmap/dependency/evidence documents, and no unrelated
cleanup.
Stop point: all twelve level-1 class models validate, basic creation selects each class, and all
unsupported grants remain explicit no-behavior pending entitlements in the same atomic transaction.

## Confirmed decisions

The user's 2026-08-27 instruction to prefer as many correctly schematized models as possible over
partially implemented mechanics confirms:

- permanent component ID `dnd2024.class-creation-profile`;
- permanent class IDs `content.dnd2024.class.<class-key>.v1` for all twelve SRD class keys;
- permanent level-1 feature identity IDs under `content.dnd2024.feature.<class-key>.*.v1`;
- widening `dnd2024.character-creation-record` from a Fighter-only template to one of twelve exact
  Soldier/class level-1 templates; and
- absent mechanics grant no approximate behavior. Their source-declared entitlements remain
  queryable and pending until a later mechanic consumes them.

“Magician” is implemented as the SRD Wizard. The existing Fighter IDs remain unchanged.

## D&D 5e 2024 alignment

| Rule concern | SRD 5.2.1 meaning | Existing owner | Implementation consequence |
| --- | --- | --- | --- |
| Class core traits | Each class declares primary abilities, Hit Die, saves, skill choices, weapon proficiencies, tools, and armor training | new immutable class-creation profile plus existing progression owner | Store all traits source-bound; apply only state with an accepted exact owner |
| Level-1 HP | Maximum equals the class Hit Die maximum plus Constitution modifier | `dnd2024.class-progression`, HP component | Reuse progression child and derive from its Hit Die for every class |
| Skills | Class supplies a bounded choice from its list; Bard chooses any three | profile options and fixed legal default choices | Apply deterministic non-Soldier-duplicate choices; caller cannot invent skills |
| Weapons | Most classes receive categories; Monk/Rogue receive a restricted Martial subset | weapon proficiency category component cannot express property-qualified Martial access | Apply exact full categories only; record restricted Martial access pending rather than overgranting |
| Armor/tools | Core tables grant armor/tool traits, but no accepted character state owner applies them | profile declarations | Keep declarations queryable and record each grant/choice pending |
| Spellcasting | Seven classes declare level-1 spellcasting/Pact Magic quantities and ability | profile spellcasting declaration; no spell-state/execution root | Record spellcasting and selections pending; create no slots, spells, focuses, or casting behavior |
| Class features | Every class table lists level-1 feature identities | class progression and content-definition owners | Create identity records and progression entitlements; behavior remains unimplemented and pending |
| Baseline AC | Generic unarmored AC is 10 + Dexterity; Barbarian/Monk alternatives are class features | armor-class owner; feature behavior absent | Preserve generic baseline and record each alternative-defense feature pending |

## External implementation reference

Reviewed Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9` at
`packs/_source/classes24/bard/bard.yml` and `packs/_source/classes24/wizard/wizard.yml`. It keeps
class traits, progression grants, spellcasting progression, Hit Points advancement, and selectable
traits as declarative class data consumed by a generic advancement workflow. This slice adopts that
separation only. No Foundry code, data, assets, IDs, or runtime dependency are copied.

## Prerequisite evidence

- The accepted CC-MVP receipt proves actor creation, participation, replay, rollback, and pending
  entitlement semantics.
- The accepted class-progression reader validates source identity, Hit Die pairs, ordered level
  rows, and feature identifiers without granting feature behavior.
- Existing skill, save, weapon-category, HP, AC, ability, species, and level schemas own applied
  state. Missing spell, armor-training, tool-choice, and property-qualified weapon owners remain
  explicit boundaries.

## Runtime artifacts

- New `dnd2024.class-creation-profile` definition/schema for source-bound Core Traits and level-1
  spellcasting quantities.
- Eleven new class entities plus the revised Fighter entity, for twelve total active level-1 class
  models.
- Source-bound content-definition identities for every level-1 feature referenced by new class
  progression rows.
- Revised `dnd2024.character-creation-record`, basic creator mechanic, and governing procedure.
- No C# rules, migration, database bootstrap copy, public kind, or additional transaction owner.

## Authoritative state and closed input

The existing roles remain exactly `world`, `policy`, `background`, `species`, and `class`. The
`class` role now requires content definition, class progression, and class creation profile. It may
bind exactly one active SRD class entity whose three source references and class keys agree.

The request shape is unchanged. Class selection occurs through the trusted role binding, not a
duplicate caller field. Soldier, Standard Array, species selection, and level 1 remain fixed.

## Behavior, result, and typed effects

1. Validate the profile as closed, source-bound, internally consistent, and matched to the class
   content/progression child.
2. Resolve legal Standard Array/Soldier abilities and species as before.
3. Derive HP from the selected class Hit Die and Constitution; derive baseline unequipped AC.
4. Apply the class saves, deterministic class skill choices unioned with Soldier skills, and only
   complete weapon categories declared by the profile.
5. Record profile-declared armor, tool, restricted Martial, spellcasting, and every level-1 feature
   as sorted no-behavior pending entitlements.
6. Commit the same actor/component/participation effect bundle through the generic application
   transaction. Replay and rollback ownership do not change.

## Failure, replay, and rollback contract

Unknown classes, mismatched class keys, malformed/extra profile fields, invalid skill choices,
source drift, invalid Hit Die pairs, unsupported levels, or inconsistent spell quantities fail
before effects. Existing collision, exact replay, and injected late rollback behavior remain
unchanged.

## Implementation sequence

1. Add the class profile schema and all twelve source-bound level-1 class/profile/progression models.
2. Add every referenced level-1 feature identity.
3. Generalize the creation record and JavaScript root without adding a second mechanic.
4. Extend focused fixtures/tests across all classes and failure/transaction boundaries.
5. Validate the catalog, run focused/D&D/full tests, and write one completion receipt.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Coverage | exactly twelve active SRD class entities have valid matching profiles/progression |
| Bard/Wizard | correct Hit Dice, saves, skills, simple weapons, and spell declarations; spell behavior remains absent/pending |
| Martial classes | correct Hit Dice, saves, skill counts, and complete weapon categories |
| Monk/Rogue | simple weapons apply; restricted Martial eligibility is pending and never overgranted |
| Spellcasters | correct ability/cantrip/prepared/slot/spellbook quantities are declared and no spell state is fabricated |
| HP | each class uses its own Hit Die plus Constitution modifier, minimum 1 |
| Pending visibility | armor, tools, spells, restricted weapons, and all unimplemented features are durable and sorted |
| Closed/source failure | malformed, mismatched, inactive, or drifted profile/class data creates nothing |
| Transactions | replay, fresh readback, collision, and injected late rollback remain green |
| Compatibility | existing Fighter result and all prior D&D tests remain green |

## Verification commands

- focused basic-creation and class-model tests;
- complete `Dnd2024AbilityCheckTests`;
- `roleplay validate catalog` against a disposable database; and
- full solution tests. No protocol walk is required because registration does not change.

## Completion receipt and exit gate

Delivered by [the all-class completion receipt](evidence/DND2024-CHARACTER-CREATION-ALL-CLASS-RECEIPT.md).
The slice stops without implementing spells, class features, equipment, level-up, subclasses, or a
new user interface.

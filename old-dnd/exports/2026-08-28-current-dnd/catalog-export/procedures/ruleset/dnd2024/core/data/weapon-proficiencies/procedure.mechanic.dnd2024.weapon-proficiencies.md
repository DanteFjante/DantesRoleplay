---
id: procedure.mechanic.dnd2024.weapon-proficiencies
category: ruleset.dnd2024.core.data.weapon-proficiencies
name: Record weapon-category proficiencies
governs: commit(kind: "component") introducing weapon-category proficiency storage; commit(kind: "mechanic") validating weapon-category proficiency records; commit(kind: "action") recording or correcting weapon-category proficiency
status: active
createdBy: "import"
changeNote: "Imported from the catalog."
---

## Description
Defines a creature's complete known D&D 2024 Simple/Martial weapon-category proficiency state and its closed administrative writer. It records category membership only; later attack resolution derives Proficiency Bonus and compares this state to a canonical weapon profile.

## Instructions
Source and scope

- Rule source: `source.dnd2024.srd-5.2.1`, locator `Equipment > Weapons > Weapon Proficiency`, PDF page 89 in System Reference Document 5.2.1.
- Anyone may wield a weapon, but adding Proficiency Bonus to its attack roll requires proficiency. This feature records the relevant category state only.
- Class/background grants, individual weapon exceptions, mastery, temporary proficiency, equipment ownership, Proficiency Bonus, attack rolls, damage, and Hit Point changes are out of scope.

Creation order and data

1. Create this contract.
2. Declare `dnd2024.weapon-proficiencies` as a closed object containing categories and sourceRef.
3. Fix `sourceRef` to `{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Equipment > Weapons > Weapon Proficiency"}`.
4. Create active `mechanic.dnd2024.weapon-proficiencies.write` in scope `dnd2024-srd-5.2.1`. It declares role `subject` and may inspect that component when present.
5. Use that mechanic for every normal write. It applies exactly one `component.add` for record or one `component.set` for correct; callers never author source references, a class grant, a weapon id, an attack result, or effects directly.

Action input and result

- Input is exactly `{"mode":"record"|"correct","categories":["simple"|"martial"...]}`. Categories are a duplicate-free canonical subset ordered simple then martial; `[]` means known proficiency with neither category.
- It rejects missing, non-array, unknown, wrong-case, duplicated, reordered, source, class, weapon, Proficiency Bonus, attack, damage, and effect fields before proposing effects.
- `record` requires absence. `correct` requires a valid existing component. A corrupt existing record is rejected, never silently repaired.
- The result reports mode, canonical categories, previous categories/null, and fixed source attribution. It uses no dice and changes no other component.

Deterministic verification

- Record empty, Simple, Martial, and both categories; correct Simple to both; query each resulting component and prove its exact two-field shape.
- Reject duplicate record, correction while absent, malformed roots, every wrong input category/order/extra-key shape, and corrupt stored component data without state change.
- Confirm intent routing selects this writer rather than the skill/save, weapon-profile, Armor Class, or Hit Point writers. Prove existing profile, Armor Class, Hit Point, and level bytes remain unchanged.
- Run catalog dry-run, import, catalog verify, the fresh-database catalog test, the full repository suite, and `git diff --check`.

## Constraints
- `dnd2024.weapon-proficiencies` contains exactly canonical categories and the fixed sourceRef; missing state is distinct from explicit empty state.
- The writer accepts only its closed mode/categories input and produces exactly one component effect on subject.
- It grants nothing and owns no Proficiency Bonus, weapon id, class provenance, attack, damage, or equipment state. Feature 8 must fail closed on absent or corrupt state rather than treating it as empty.


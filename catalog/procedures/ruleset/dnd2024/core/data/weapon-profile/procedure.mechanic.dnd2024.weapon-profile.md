---
id: procedure.mechanic.dnd2024.weapon-profile
category: ruleset.dnd2024.core.data.weapon-profile
name: Record canonical weapon profiles
governs: commit(kind: "component") introducing weapon-profile storage; commit(kind: "mechanic") validating weapon-profile records; commit(kind: "action") recording or correcting a weapon profile
status: active
---

## Description
Defines the reusable canonical D&D 2024 weapon profile and its closed administrative writer. A profile is source data on a weapon entity, never a copied actor object or an attack result.

## Instructions
Source and scope

- Rule source: `source.dnd2024.srd-5.2.1`, locator `Equipment > Weapons`, PDF pages 89–91 in System Reference Document 5.2.1.
- Every profile is either Simple or Martial, Melee or Ranged. Dagger, Shortbow, and Battleaxe are the deliberately small canonical seed set.
- Equipment ownership, equipping, ammunition consumption, mastery permission, attack rolls, Proficiency Bonus, damage rolls, and Hit Point changes are out of scope. Ranged normal/long and Thrown range are static profile facts only.

Creation order and data

1. Create this contract.
2. Declare `dnd2024.weapon-profile` as a closed object containing category, kind, attackAbilities, damage, canonical propertyTags, exactly one mastery, and sourceRef. A Ranged profile additionally requires `rangeFeet: { normal, long }`; `ammunition` declares its ammunition type, `thrown` declares `thrownRangeFeet`, and `versatile` declares a matching-type greater alternate damage expression. Every range has positive five-foot values with normal no greater than long.
3. Fix `sourceRef` to `{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Equipment > Weapons"}`.
4. Create active `mechanic.dnd2024.weapon-profile.write` in scope `dnd2024-srd-5.2.1`. It declares role `weapon` and may inspect that component when present.
5. Import canonical entities `weapon.dnd2024.dagger`, `weapon.dnd2024.shortbow`, and `weapon.dnd2024.battleaxe`, each with exactly one profile component. They declare Dagger Finesse/Light/Thrown 20/60 and Nick; Shortbow Ammunition (Arrow) / Two-Handed and Vex at 80/320; and Battleaxe Versatile 1d10 and Topple.
6. Use the writer for every normal profile write. It applies exactly one `component.add` for record or one `component.set` for correct; callers never author source references, physical properties, attack outcomes, or effects directly.

Action input and result

- Input is the same closed profile facts except fixed `sourceRef`: ordered `propertyTags`, one `mastery`, Ranged `rangeFeet`, and only the structured fields required by its tags (`ammunitionType`, `thrownRangeFeet`, or `versatileDamage`).
- Attack abilities are nonempty, duplicate-free, and ordered `str` then `dex`; callers cannot provide another ability.
- `record` requires absence. `correct` requires a valid existing component. A corrupt existing record is rejected, never silently repaired.
- The result reports mode, profile facts, prior profile/null, and fixed source attribution. It uses no dice and changes no other entity.

Deterministic verification

- Import a fresh catalog and read Dagger (Simple/Melee, str+dex, 1d4 Piercing), Shortbow (Simple/Ranged, dex, 1d6 Piercing, 80/320), and Battleaxe (Martial/Melee, str, 1d8 Slashing) back exactly.
- Record and correct a disposable profile; assert one add then one set, exact source/profile bytes, and identical replay on equivalent fixtures.
- Reject malformed roots, invalid/reordered/duplicate abilities, invalid damage, invalid/missing ranged range, range on a Melee profile, extra source/property/attack fields, duplicate record, absent correction, and corrupt existing state without changes.
- Confirm intent routing selects this writer rather than ability, saving-throw, Initiative, Armor Class, or Hit Point rules. Run catalog dry-run, import, verify, the fresh-database catalog test, the full repository suite, and `git diff --check`.

## Constraints
- `dnd2024.weapon-profile` owns only static category, kind, allowed attack abilities, base damage, properties, normal/long Ranged range, Thrown range, mastery identity, and fixed source attribution.
- The writer accepts only its closed input and produces exactly one component effect on role `weapon`.
- No profile is equipped, no attack ability is selected, no die is rolled, and no creature, Armor Class, Hit Point, or proficiency state is changed.

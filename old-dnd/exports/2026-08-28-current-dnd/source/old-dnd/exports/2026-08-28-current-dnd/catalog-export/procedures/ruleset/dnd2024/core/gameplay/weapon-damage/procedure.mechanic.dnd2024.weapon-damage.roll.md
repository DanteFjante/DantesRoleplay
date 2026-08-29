---
id: procedure.mechanic.dnd2024.weapon-damage.roll
category: ruleset.dnd2024.core.gameplay.weapon-damage
name: Roll confirmed weapon damage
governs: commit(kind: "mechanic") validating D&D weapon damage resolution; commit(kind: "action") resolving seeded damage for a confirmed weapon hit
status: active
createdBy: "import"
changeNote: "Imported from the catalog."
---

## Description
Defines one effect-free D&D 2024 base weapon-damage resolver. A caller invokes it only after confirming a Feature 8 hit, then it reads canonical weapon facts and a selected ability, reports normal or critical damage, and never changes Hit Points.

## Instructions
Source and scope

- Rule source: `source.dnd2024.srd-5.2.1`, locators `Playing the Game > Damage and Healing > Damage Rolls` and `Playing the Game > Damage and Healing > Critical Hits` (PDF page 16), plus `Equipment > Weapons` (PDF page 89).
- Roll the profile's base damage dice, add the same ability modifier selected for the attack, and clamp a negative result to zero. A Critical Hit doubles only the attack's base damage dice and adds the modifier once.
- Feature 8 owns whether the attack hit and whether it was critical. Because it stores no result, this action's closed `critical` Boolean is GM/caller confirmation after an observed hit; it must not accept attack/damage/Hit Point facts that the resolver can derive.

Required state and input

1. Require subject `dnd2024.abilities` and weapon `dnd2024.weapon-profile`; validate every closed field and fixed source reference before consuming randomness.
2. Input is exactly `{"ability":"str"|"dex","critical":true|false}`. The ability must occur in the profile's canonical list.
3. Base count must support at most 100 actual dice even when critical. This safety bound is a resolver capacity, not a new profile fact; current catalog weapons remain well within it.
4. Derive the ability modifier and all dice/count/type data. Reject input `hit`, AC, profile, modifier, roll, total, damage, target, Hit Point, and effect fields.

Result and verification

- Return subject/weapon ids, ability, critical flag, base count/faces/type, actual dice count and ordered rolls, dice subtotal, ability modifier, nonnegative damage, and source. Return `effects: []`.
- Prove normal/critical multiplier and one-modifier behavior, zero clamp, profile ability restrictions, deterministic replay, malformed input/state rejection, intent routing, zero effects, and exact unchanged subject/weapon state.
- Run catalog dry-run/import/verify, fresh-database integration coverage, full tests, and `git diff --check`.

## Constraints
- This resolver owns only transient confirmed-hit damage evidence. It never re-rolls an attack, adds Proficiency Bonus, persists an outcome, changes a component, applies damage, or changes Hit Points.
- Resistance, Vulnerability, Immunity, temporary Hit Points, healing, conditions, unconsciousness, death, massive damage, extra damage sources, and target state are later owners.
- Feature 9 Slice 2 may consume this exact envelope through declared composition; it must not duplicate this dice or critical owner.


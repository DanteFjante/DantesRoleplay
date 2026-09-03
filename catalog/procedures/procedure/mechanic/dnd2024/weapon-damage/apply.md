---
id: procedure.mechanic.dnd2024.weapon-damage.apply
category: ruleset.dnd2024.core.gameplay.weapon-damage
name: Apply confirmed weapon damage to Hit Points
governs: commit(kind: "mechanic") validating composed D&D weapon damage application; commit(kind: "action") applying one confirmed weapon-damage result to authoritative Hit Points
status: active
createdBy: "import"
changeNote: "Imported from the catalog."
---

## Description
Defines the D&D 2024 parent that composes one confirmed weapon-damage child and atomically applies its verified result to the target's authoritative Hit Point state. It has no second dice owner and infers no zero-Hit-Point consequence.

## Matches

## Instructions
Source and ownership

- Rule source: `source.dnd2024.srd-5.2.1`, locators `Playing the Game > Damage and Healing > Hit Points`, `Damage Rolls`, and `Critical Hits` (PDF page 16).
- Declare exactly one `mechanic.dnd2024.weapon-damage.roll` child bound to parent subject/weapon roles with identical closed input. The child owns damage dice and critical doubling; this parent owns only the target Hit Point effect.
- Damage subtracts from current Hit Points and current cannot fall below zero. Maximum and Feature 6's fixed Hit Point source reference remain unchanged. Zero Hit Points has no condition, death, or other consequence in this slice.

Required state and input

1. Require subject `dnd2024.abilities`, weapon `dnd2024.weapon-profile`, and target `dnd2024.hit-points`; validate the target's complete closed Feature 6 state and source reference before proposing effects.
2. Input is exactly `{"ability":"str"|"dex","critical":true|false}` and is inherited by the child. Do not accept a target delta, HP pair, damage amount/type, dice, child result, or effects.
3. Require exactly one frozen child result with matching role ids/input facts, closed result fields, valid dice and arithmetic. Reject a malformed/mismatched result rather than rerolling or recomputing it.

Result and verification

- Return child mechanic/version/seed, roles, damage/critical/type, before/after current HP, unchanged maximum, and source. Return exactly one target `component.set` effect containing the full valid after-state, including zero-damage or already-zero cases.
- Prove normal/critical child consumption, overkill clamp, zero damage, atomic dry-run/apply, replay, target-only state change, absent/corrupt HP and input rejection, routing, and no mutation on failure.
- Run catalog dry-run/import/verify, fresh-database coverage, the full suite, and `git diff --check`.

## Constraints
- This parent never rolls, recalculates, or trusts caller-supplied damage; it consumes only its declared child evidence.
- It never changes subject/weapon state, target maximum/source reference, Armor Class, or another entity. No Resistance, Vulnerability, Immunity, temporary Hit Points, healing, condition, death, or damage history is created.
- Feature 10 may compose the verified attack and damage workflow later; do not revise this parent to introduce turns, range, or attack legality.

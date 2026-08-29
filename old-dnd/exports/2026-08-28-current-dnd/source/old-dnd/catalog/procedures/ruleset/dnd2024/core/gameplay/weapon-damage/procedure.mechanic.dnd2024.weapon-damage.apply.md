---
id: procedure.mechanic.dnd2024.weapon-damage.apply
category: ruleset.dnd2024.core.gameplay.weapon-damage
name: Apply confirmed weapon damage to Hit Points
governs: commit(kind: "mechanic") validating composed D&D weapon damage application; commit(kind: "action") applying one confirmed weapon-damage result to authoritative Hit Points
status: active
---

## Description
Defines the D&D 2024 parent that composes one confirmed weapon-damage child and one mitigation-profile child, applies the SRD mitigation order, spends a Temporary Hit Point buffer first, and atomically applies the remainder to authoritative Hit Points. It has no second dice owner and infers no zero-Hit-Point consequence.

## Instructions
Source and ownership

- Rule source: `source.dnd2024.srd-5.2.1`, locators `Playing the Game > Damage and Healing > Hit Points`, `Damage Rolls`, and `Critical Hits` (PDF page 16).
- Declare exactly one `mechanic.dnd2024.weapon-damage.roll` child bound to parent subject/weapon roles with identical closed input, and one `mechanic.dnd2024.damage.resolve` child bound `defender: target` with static `{}` input. The damage child owns dice and critical doubling; the mitigation child reports stored facts; this parent owns the arithmetic, target Hit Point effect, and event.
- Apply the `procedure.mechanic.dnd2024.damage-mitigation` order exactly once: Immunity makes the final amount zero; otherwise Resistance (including Petrified) halves and rounds down; then Vulnerability doubles. Fail before effects or events if doubling exceeds the safe integer.
- After mitigation, a present valid `dnd2024.temporary-hit-points` buffer absorbs first. Write it only when its amount changes: set while positive, remove when exhausted. Then subtract the remainder from current Hit Points, which cannot fall below zero. `overkill` is the amount beyond the buffer and current Hit Points. Maximum and Feature 6's fixed Hit Point source reference remain unchanged.

Required state and input

1. Require subject `dnd2024.abilities`, weapon `dnd2024.weapon-profile`, and target `dnd2024.hit-points`; declare target `dnd2024.temporary-hit-points`, `dnd2024.damage-mitigation`, and `dnd2024.conditions`. Validate complete present state before proposing effects.
2. Input is exactly `{"ability":"str"|"dex","critical":true|false}` and is inherited by the child. Do not accept a target delta, HP pair, damage amount/type, dice, child result, or effects.
3. Require exactly one frozen result from each declared child with matching role ids/input facts and their closed result fields. Validate dice arithmetic and the mitigation profile; reject malformed/mismatched evidence rather than rerolling or recomputing it.

Result and verification

- Return both child mechanic/version/seed records, roles, raw/final damage, mitigation breakdown, temporary before/after/absorbed values, damage/critical/type, before/after current HP, unchanged maximum, overkill, and source. A buffer effect precedes the always-present full Hit Point `component.set`; zero-damage leaves a buffer byte-identical.
- Declare exactly one schema-valid `dnd2024.damage.dealt` event on every successful application, naming the target and carrying the closed payload required by its event type. No failed application emits an event.
- Prove normal/critical child consumption, mitigation order, overkill clamp, zero damage, atomic dry-run/apply, replay, target-only state change, absent/corrupt HP and child evidence rejection, routing, event validity, and no mutation on failure.
- Run catalog dry-run/import/verify, fresh-database coverage, the full suite, and `git diff --check`.

## Constraints
- This parent never rolls, recalculates, or trusts caller-supplied damage; it consumes only its declared child evidence.
- It never changes subject/weapon state, target maximum/source reference, Armor Class, or another entity. It reads and spends but never grants Temporary Hit Points; it does not create Resistance, Vulnerability, Immunity, healing, condition, or death state. Its one damage event is history for later rules, not a condition or consequence.
- Feature 10 may compose the verified attack and damage workflow later; do not revise this parent to introduce turns, range, or attack legality.

---
id: procedure.mechanic.dnd2024.damage.resolve
category: ruleset.dnd2024.core.gameplay.damage
name: Read D&D 2024 damage mitigation
governs: commit(kind: "mechanic") authoring mechanic.dnd2024.damage.resolve; commit(kind: "action") reading creature damage mitigation
status: active
---

## Description

Owns the effect-free translation of stored damage-mitigation and condition state into a defender's
damage-mitigation profile. A damage cause consumes this profile; this resolver never receives an
amount or type and never changes Hit Points.

## Instructions

1. Require a `defender` role and accept exactly `{}`. Read optional `dnd2024.damage-mitigation` and
   `dnd2024.conditions` components directly. Missing state reports `false` in its corresponding
   known field and an empty/default branch; malformed or semantically invalid present state fails.
2. Return the canonical Immunity, Resistance, and Vulnerability lists, whether Petrified is stored,
   both known fields, and fixed `Playing the Game > Damage and Healing` provenance. Return no effects
   and consume no randomness.
3. A consumer applies one instance in this exact order: Immunity sets its final amount to zero;
   otherwise Resistance halves and rounds down when the type is stored resistant **or** the defender
   is Petrified; then Vulnerability doubles when the type is stored vulnerable. Each applies once.
4. The consumer reports `rawAmount`, `type`, `immune`, `resistanceApplied`,
   `vulnerabilityApplied`, ordered reasons, and `finalAmount`. Stored and Petrified resistance to
   the same type each appear as reasons but still halve only once. Reject an unsafe-integer overflow
   before proposing an effect or event.

## Constraints

- This resolver accepts no damage amount, damage type, condition, source, arithmetic result, Hit
  Point, event, or effects field.
- It does not write mitigation, conditions, damage, Hit Points, or an event.
- Later damage causes compose this resolver rather than duplicating its state validation or treating
  absent state as explicitly known-empty state.

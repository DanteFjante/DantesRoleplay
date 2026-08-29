---
id: procedure.mechanic.dnd2024.damage.resolve
category: ruleset.dnd2024.core.gameplay.damage
name: Read D&D 2024 damage mitigation
governs: mechanic.dnd2024.damage.resolve
status: active
---

## Description

Owns the effect-free translation of stored mitigation and the existing Condition state-effects
projection into a defender profile for later damage causes.

## Instructions

1. Accept exactly `{}` with a `defender`. Read optional `dnd2024.creature.defenses`; missing state is
   unknown, while present empty lists are known-empty.
2. Compose exactly one `mechanic.dnd2024.d20-test.state-effects` child with `subject` bound to the
   defender. Derive Petrified only from that validated child. Source:
   `source.dnd2024.srd-5.2.1`, `Rules Glossary > Petrified > Resist Damage` (PDF p. 186).
3. Return known flags, canonical memberships, Petrified, and exact provenance with no effects,
   events, notifications, randomness, damage input, or arithmetic.
4. A later consumer applies one damage instance in this order: Immunity prevents matching damage;
   otherwise Resistance halves and rounds down once when the type is stored resistant or the
   defender is Petrified; Vulnerability then doubles once. Source: `Playing the Game > Damage and
   Healing > Resistance and Vulnerability > No Stacking/Order of Application` and `Immunity`
   (PDF p. 17).

## Constraints

This resolver does not write mitigation or Conditions, receive or calculate damage, change Hit
Points, or emit an event. Consumers compose it instead of duplicating its stored-state or Condition
dependency.

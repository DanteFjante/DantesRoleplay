---
id: procedure.mechanic.dnd2024.damage-mitigation
category: ruleset.dnd2024.core.gameplay.damage
name: Record D&D 2024 damage mitigation
governs: dnd2024.damage-mitigation; mechanic.dnd2024.damage-mitigation.write
status: active
---

## Description

Owns a creature's complete known base Resistance, Immunity, and Vulnerability type memberships and
their closed administrative writer. Damage instances, arithmetic, Conditions, Hit Points, and
source grants are separate owners.

## Instructions

1. Source: `source.dnd2024.srd-5.2.1`, `Playing the Game > Damage and Healing > Resistance and
   Vulnerability > No Stacking/Order of Application` and `Immunity` (PDF p. 17).
2. Use the canonical order `acid`, `bludgeoning`, `cold`, `fire`, `force`, `lightning`, `necrotic`,
   `piercing`, `poison`, `psychic`, `radiant`, `slashing`, `thunder`.
3. Store three required duplicate-free lists and fixed source provenance. Missing state is unknown;
   present empty lists are known-empty. Preserve cross-list memberships.
4. Accept exactly complete `record` or `correct` input. `record` requires absence and proposes one
   `component.add`; `correct` requires valid present state and proposes one `component.set`.

## Constraints

The writer accepts no source, grant, Condition, damage amount/type, arithmetic result, Hit Points,
event, notification, or caller-selected effect. It consumes no randomness and changes no other
component.

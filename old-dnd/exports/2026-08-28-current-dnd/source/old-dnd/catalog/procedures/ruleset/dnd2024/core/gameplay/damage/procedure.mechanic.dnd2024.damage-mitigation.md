---
id: procedure.mechanic.dnd2024.damage-mitigation
category: ruleset.dnd2024.core.gameplay.damage
name: Record D&D 2024 damage mitigation
governs: commit(kind: "component") declaring damage-mitigation storage; commit(kind: "mechanic") validating damage-mitigation records; commit(kind: "action") recording or correcting creature damage mitigation
status: active
---

## Description

Owns a creature's complete known D&D 2024 Resistance, Immunity, and Vulnerability state and its
closed administrative writer. It records type membership only; later Feature 15 slices resolve the
arithmetic for a particular damage instance.

## Instructions

1. Use the canonical types and order in `procedure.mechanic.dnd2024.damage-types`: `acid`,
   `bludgeoning`, `cold`, `fire`, `force`, `lightning`, `necrotic`, `piercing`, `poison`,
   `psychic`, `radiant`, `slashing`, `thunder`.
2. Declare one closed `dnd2024.damage-mitigation` component with `resistances`, `immunities`,
   `vulnerabilities`, and fixed `sourceRef`
   `{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Playing the Game > Damage and Healing"}`.
3. `mechanic.dnd2024.damage-mitigation.write` accepts exactly
   `{"mode":"record"|"correct","resistances":[...],"immunities":[...],"vulnerabilities":[...]}`.
   Every list is required, duplicate-free, and stored in canonical order; empty means known absence
   of that kind of mitigation.
4. `record` requires absence and proposes one `component.add`. `correct` requires complete valid
   existing state and proposes one `component.set`; malformed state is rejected rather than repaired.
5. A type may occur in more than one list. This represents valid SRD state; the Feature 15 resolver
   determines its effect exactly once per damage instance.

## Constraints

- The writer accepts no source reference, damage amount/type, source grant, condition, Hit Point,
  event, arithmetic result, or effects field.
- It changes only the subject's complete mitigation component, consumes no randomness, and grants
  no class, spell, item, species, or condition effect.
- This slice does not resolve Resistance, Immunity, Vulnerability, Petrified, damage, or Hit Points.

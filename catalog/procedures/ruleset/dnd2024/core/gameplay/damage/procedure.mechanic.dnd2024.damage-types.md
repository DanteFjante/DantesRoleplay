---
id: procedure.mechanic.dnd2024.damage-types
category: ruleset.dnd2024.core.gameplay.damage
name: Define canonical D&D 2024 damage types
governs: commit(kind: "component") declaring damage-typed state; commit(kind: "mechanic") authoring D&D 2024 damage resolution
status: active
---

## Description

Owns the canonical D&D 2024 damage-type vocabulary used by every future damage-typed component,
resolver, and event. The vocabulary is a rules contract, not creature or item state.

## Instructions

1. Use SRD 5.2.1, `Playing the Game > Damage and Healing > Damage Types` and `Rules Glossary >
   Damage Types`. The complete canonical order is: `acid`, `bludgeoning`, `cold`, `fire`, `force`,
   `lightning`, `necrotic`, `piercing`, `poison`, `psychic`, `radiant`, `slashing`, `thunder`.
2. A new damage-typed schema enumerates exactly this vocabulary unless its established domain is a
   strict, documented subset. It never introduces an alias, case variant, count, or fourteenth type.
3. `dnd2024.weapon-profile.damage.type` remains the documented physical subset: `bludgeoning`,
   `piercing`, and `slashing`. Its existing schema and writer are unchanged until that owner is
   explicitly revised.
4. A mitigation component, damage resolver, or damage event must reuse these exact lower-case ids;
   it must not maintain an independently ordered list.

## Constraints

- This contract creates no component, mechanic, event type, fixture, or world state.
- No current weapon profile gains a non-physical damage type through this contract.
- Resistance, Immunity, Vulnerability, damage arithmetic, Hit Point changes, and damage events are
  separate Feature 15 slices and are not implied by declaring the vocabulary.

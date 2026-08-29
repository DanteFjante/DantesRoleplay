---
id: procedure.mechanic.dnd2024.armor-class
category: ruleset.dnd2024.core.data.armor-class
name: Derive selected Armor Class
governs: mechanic.dnd2024.armor-class.read; mechanic.dnd2024.armor-class.unarmored; dnd2024.creature.defenses; dnd2024.creature.defense-basis
status: active
---

## Description

Selects a source-owned defense basis and derives final Armor Class without storing the result.

## Instructions

Resolve the creature's `armorClassSource`, validate its declared calculation, and run that
calculation against authoritative creature state. The current active ordinary-unarmored basis is
`10 + Dexterity modifier`.

## Constraints

- Callers cannot supply final Armor Class, ability scores, modifiers, or a replacement source.
- Armor, Shields, class-specific unarmored formulas, cover, and temporary effects require their own
  declared defense sources or later modifier owners.

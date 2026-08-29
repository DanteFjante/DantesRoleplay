---
id: procedure.mechanic.dnd2024.dice
category: ruleset.dnd2024.core.gameplay.dice
name: Roll seeded D&D 2024 dice
governs: mechanic.dnd2024.dice
status: active
---

## Description

Provides a bounded seeded dice roll without changing state.

## Instructions

Supply optional count, sides, and modifier; use the seeded random helper once per die and return
individual rolls plus exact total.

## Constraints

Count is 1–100, sides 2–1,000,000, and modifier/total are safe integers. Never use `Math.random`.
This applies no D20-test, Advantage, damage, table, visibility, or world-state rule.

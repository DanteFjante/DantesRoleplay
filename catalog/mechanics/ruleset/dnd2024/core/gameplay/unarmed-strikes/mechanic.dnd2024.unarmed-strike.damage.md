---
id: mechanic.dnd2024.unarmed-strike.damage
category: ruleset.dnd2024.core.gameplay.unarmed-strikes
name: Resolve unarmed strike damage evidence
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Resolves one seeded D&D 2024 Unarmed Strike Damage option against final Armor Class. It derives
Strength, Proficiency Bonus, condition circumstances, hit/critical classification, and fixed
Bludgeoning damage evidence without spending an Action, checking reach, or changing Hit Points.

## Matches

unarmed strike damage
unarmed attack damage
resolve unarmed strike

## Requirements

```json
{"roles":{"subject":{"components":["dnd2024.abilities","dnd2024.character-level","dnd2024.conditions"],"description":"The creature making the diagnostic Strength-based Unarmed Strike."},"target":{"components":["dnd2024.armor-class","dnd2024.conditions"],"description":"The creature whose final Armor Class and derived condition circumstances inform the diagnostic attack."}},"children":{"attackerEffects":{"mechanicId":"mechanic.dnd2024.d20-test.state-effects","roleBindings":{"subject":"subject"},"inheritInput":false,"input":"{}"},"targetEffects":{"mechanicId":"mechanic.dnd2024.d20-test.state-effects","roleBindings":{"subject":"target"},"inheritInput":false,"input":"{}"}}}
```

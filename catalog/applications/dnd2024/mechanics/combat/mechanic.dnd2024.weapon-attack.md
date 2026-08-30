---
id: mechanic.dnd2024.weapon-attack
category: ruleset.dnd2024.core.gameplay.weapon-attacks
name: Resolve weapon attack
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Resolves a fixed weapon attack against authoritative target Armor Class without applying damage.
It verifies the selected active activity is a member of the weapon, uses that activity's permitted
ability choices, and accepts both complete category proficiency and canonical property-qualified
Martial proficiency.

## Matches

weapon attack
attack with weapon

## Requirements

```json
{"roles":{"subject":{"components":["dnd2024.creature.ability-scores","dnd2024.creature.proficiencies"],"description":"The attacker."},"weapon":{"components":["dnd2024.item.weapon","dnd2024.activity.membership"],"description":"The selected canonical weapon definition."},"activity":{"components":["dnd2024.core.version","dnd2024.activity.attack"],"description":"The selected active attack activity belonging to the weapon."},"target":{"components":["dnd2024.creature.defenses"],"description":"The target whose Armor Class is derived."}},"children":{"level":{"mechanicId":"mechanic.dnd2024.character-level.read","roleBindings":{"subject":"subject"},"inheritInput":false,"input":"{}"},"armorClass":{"mechanicId":"mechanic.dnd2024.armor-class.read","roleBindings":{"subject":"target"},"inheritInput":false,"input":"{}"}}}
```

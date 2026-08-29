---
id: mechanic.dnd2024.weapon-attack
category: ruleset.dnd2024.core.gameplay.weapon-attacks
name: Resolve weapon attack
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Resolves a fixed weapon attack against authoritative target Armor Class without applying damage.
It accepts legacy category-only and current expanded weapon-proficiency state. This mechanic still
uses complete category membership only; property-qualified Martial proficiency remains denied
until weapon profiles expose canonical properties.

## Matches

weapon attack
attack with weapon

## Requirements

```json
{"roles":{"subject":{"components":["dnd2024.creature.ability-scores","dnd2024.creature.proficiencies"],"description":"The attacker."},"weapon":{"components":["dnd2024.weapon-profile"],"description":"The selected static weapon profile."},"target":{"components":["dnd2024.creature.defenses"],"description":"The target whose Armor Class is derived."}},"children":{"level":{"mechanicId":"mechanic.dnd2024.character-level.read","roleBindings":{"subject":"subject"},"inheritInput":false,"input":"{}"},"armorClass":{"mechanicId":"mechanic.dnd2024.armor-class.read","roleBindings":{"subject":"target"},"inheritInput":false,"input":"{}"}}}
```

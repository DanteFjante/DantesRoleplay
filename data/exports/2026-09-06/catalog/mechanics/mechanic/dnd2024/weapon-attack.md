---
id: mechanic.dnd2024.weapon-attack
category: ruleset.dnd2024.core.gameplay.weapon-attacks
name: Resolve a weapon attack against Armor Class
scope: dnd2024-srd-5.2.1
status: deprecated
createdBy: "seed"
changeNote: "Re-seeded: the embedded catalog mechanic changed."
---

## Description
Resolves one seeded D&D 2024 weapon attack against a target's final Armor Class. It reads canonical weapon facts and category proficiency, explains hit/miss and natural 20/1 precedence, and applies no effects or damage.

## Matches
attack with weapon
weapon attack
make weapon attack
roll weapon attack
attack target with dagger

## Requirements
```json
{"roles":{"subject":{"components":["dnd2024.abilities","dnd2024.character-level","dnd2024.weapon-proficiencies"],"description":"The creature making the attack."},"target":{"components":["dnd2024.armor-class"],"description":"The creature whose final Armor Class is the target number."},"weapon":{"components":["dnd2024.weapon-profile"],"description":"The canonical weapon profile used for the attack."}}}
```

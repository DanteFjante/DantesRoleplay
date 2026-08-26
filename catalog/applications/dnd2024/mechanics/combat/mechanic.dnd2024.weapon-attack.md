---
id: mechanic.dnd2024.weapon-attack
category: ruleset.dnd2024.core.gameplay.weapon-attacks
name: Resolve weapon attack
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Resolves a fixed weapon attack against authoritative target Armor Class without applying damage.

## Matches

weapon attack
attack with weapon

## Requirements

```json
{"roles":{"subject":{"components":["dnd2024.abilities","dnd2024.character-level","dnd2024.weapon-proficiencies"],"description":"The attacker."},"weapon":{"components":["dnd2024.weapon-profile"],"description":"The selected static weapon profile."},"target":{"components":["dnd2024.armor-class"],"description":"The target."}}}
```

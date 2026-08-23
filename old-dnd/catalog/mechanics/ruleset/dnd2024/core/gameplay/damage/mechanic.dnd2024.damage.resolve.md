---
id: mechanic.dnd2024.damage.resolve
category: ruleset.dnd2024.core.gameplay.damage
name: Read damage mitigation profile
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Reads a defender's stored D&D 2024 mitigation and Petrified state as an effect-free profile for a
future damage cause. It neither takes a damage instance nor applies one.

## Matches

inspect damage mitigation
read damage mitigation profile

## Requirements

```json
{"roles":{"defender":{"components":["dnd2024.damage-mitigation","dnd2024.conditions"],"description":"The creature whose absent, known-empty, or valid mitigation and condition state is reported."}}}
```

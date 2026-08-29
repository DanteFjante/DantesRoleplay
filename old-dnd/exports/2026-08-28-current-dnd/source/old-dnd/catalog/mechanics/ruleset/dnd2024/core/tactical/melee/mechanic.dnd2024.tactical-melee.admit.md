---
id: mechanic.dnd2024.tactical-melee.admit
category: ruleset.dnd2024.core.tactical.melee
name: Admit a base-reach tactical melee attack
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Effect-free internal admission that proves map, roster, placement, Size, base reach, and melee
weapon kind before it returns only the closed input that Feature 8 accepts.

## Matches

admit tactical melee attack

## Requirements

```json
{"roles":{"attacker":{"components":["dnd2024.creature-size","dnd2024.encounter-position","dnd2024.melee-reach"]},"target":{"components":["dnd2024.creature-size","dnd2024.encounter-position"]},"weapon":{"components":["dnd2024.weapon-profile"]},"encounter":{"components":["dnd2024.encounter-space"],"includeContents":true}}}
```

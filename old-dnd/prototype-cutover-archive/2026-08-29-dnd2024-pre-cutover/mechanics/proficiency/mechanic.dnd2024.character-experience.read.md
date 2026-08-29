---
id: mechanic.dnd2024.character-experience.read
category: ruleset.dnd2024.core.advancement.experience
name: Read character experience eligibility
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Derives only whether explicit XP reaches the exact next total-character-level threshold.

## Matches

inspect character experience

## Requirements

```json
{"roles":{"subject":{"components":["dnd2024.character.experience"]}},"children":{"level":{"mechanicId":"mechanic.dnd2024.character-level.read","roleBindings":{"subject":"subject"},"inheritInput":false,"input":"{}"}}}
```

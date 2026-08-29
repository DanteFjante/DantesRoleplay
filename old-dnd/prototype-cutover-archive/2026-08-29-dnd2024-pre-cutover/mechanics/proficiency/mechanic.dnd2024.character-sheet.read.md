---
id: mechanic.dnd2024.character-sheet.read
category: ruleset.dnd2024.core.advancement.character-sheet
name: Derive core character-sheet numbers
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Derives the core numerical character-sheet view from authoritative ability, total-level, skill-
proficiency, and saving-throw-proficiency state. It stores no derived value.

## Matches

read character sheet numbers
derive character modifiers
inspect proficiency bonus and passive perception

## Requirements

```json
{"roles":{"subject":{"components":["dnd2024.creature.ability-scores","dnd2024.creature.proficiencies"],"description":"The character whose source-backed core sheet numbers are derived."}},"children":{"level":{"mechanicId":"mechanic.dnd2024.character-level.read","roleBindings":{"subject":"subject"},"inheritInput":false,"input":"{}"}}}
```

---
id: mechanic.dnd2024.armor-class.read
category: ruleset.dnd2024.core.data.armor-class
name: Derive Armor Class from direct equipment
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Derives one creature's D&D 2024 Armor Class from Dexterity and its valid direct armor/Shield
selection. It does not read or fall back to the legacy manually recorded Armor Class component.

## Matches

read derived armor class
inspect derived armor class

## Requirements

```json
{"roles":{"subject":{"components":["dnd2024.abilities","dnd2024.armor-training"],"description":"The creature whose Dexterity, optional Shield training, and direct equipment determine Armor Class."}},"children":{"equipment":{"mechanicId":"mechanic.dnd2024.armor-equipment.read","roleBindings":{"subject":"subject"},"inheritInput":false,"input":"{}"}}}
```

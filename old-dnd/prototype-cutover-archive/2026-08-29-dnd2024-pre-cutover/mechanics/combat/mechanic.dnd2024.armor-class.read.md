---
id: mechanic.dnd2024.armor-class.read
category: ruleset.dnd2024.combat.armor-class
name: Derive selected Armor Class
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Validates a creature's selected defense-basis definition and returns final Armor Class without
storing the derived number.

## Matches

read armor class
derive armor class

## Requirements

```json
{"roles":{"subject":{"components":["dnd2024.creature.defenses"],"componentReferences":[{"sourceComponentId":"dnd2024.creature.defenses","field":"armorClassSource","targetComponentIds":["dnd2024.creature.defense-basis"]}],"description":"The creature whose selected defense source determines Armor Class."}},"children":{"unarmored":{"mechanicId":"mechanic.dnd2024.armor-class.unarmored","roleBindings":{"subject":"subject"},"inheritInput":false,"input":"{}"}}}
```

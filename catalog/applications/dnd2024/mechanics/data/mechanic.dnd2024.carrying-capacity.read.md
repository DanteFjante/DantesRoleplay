---
id: mechanic.dnd2024.carrying-capacity.read
category: ruleset.dnd2024.core.data.carrying-capacity
name: Derive creature carrying capacities
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Reads exact SRD carry and drag/lift/push capacity with composed physical burden.

## Matches

derive carrying capacity

## Requirements

```json
{"roles":{"creature":{"components":["dnd2024.abilities","dnd2024.creature-size"],"includeContents":true,"contentsDepth":4,"contentComponentIds":["dnd2024.item-instance","dnd2024.item-quantity"],"componentReferences":[{"sourceComponentId":"dnd2024.item-instance","field":"definitionId","targetComponentIds":["dnd2024.item-definition"]}]}},"children":{"burden":{"mechanicId":"mechanic.dnd2024.item-burden.read","roleBindings":{"root":"creature"},"inheritInput":false,"input":"{}"}}}
```

---
id: mechanic.dnd2024.inventory.read
category: ruleset.dnd2024.core.data.inventory-read
name: Inspect bounded physical inventory
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Returns deterministic bounded physical items and visible non-item contents below one custody root.

## Matches

inspect inventory
list carried items

## Requirements

```json
{"roles":{"root":{"components":[],"includeContents":true,"contentsDepth":4,"contentComponentIds":["dnd2024.item-instance","dnd2024.item-quantity","dnd2024.equipment-state"],"componentReferences":[{"sourceComponentId":"dnd2024.item-instance","field":"definitionId","targetComponentIds":["dnd2024.item-definition"]}]}}}
```

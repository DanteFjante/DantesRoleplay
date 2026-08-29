---
id: mechanic.dnd2024.inventory.read
category: ruleset.dnd2024.core.data.inventory-read
name: Inspect bounded physical inventory
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Returns a deterministic, read-only bounded inspection of physical item instances below one custody
root. Contents beyond four levels may be omitted and are explicitly marked as such.

## Matches

inspect inventory
list carried items
read physical inventory

## Requirements

```json
{"roles":{"root":{"components":[],"includeContents":true,"contentsDepth":4,"contentComponentIds":["dnd2024.item-instance","dnd2024.item-quantity","dnd2024.equipment-state"],"componentReferences":[{"sourceComponentId":"dnd2024.item-instance","field":"definitionId","targetComponentIds":["dnd2024.item-definition"]}],"description":"The custody root whose bounded nested physical items are inspected."}}}
```

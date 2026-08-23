---
id: mechanic.dnd2024.item.equip
category: ruleset.dnd2024.core.data.equipment-state
name: Equip physical item
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Sets one directly possessed eligible physical item to held or worn.

## Matches

equip item
hold item
wear item

## Requirements

```json
{"roles":{"item":{"components":["dnd2024.item-instance","dnd2024.item-quantity","dnd2024.equipment-state"],"componentReferences":[{"sourceComponentId":"dnd2024.item-instance","field":"definitionId","targetComponentIds":["dnd2024.item-definition"]}]},"holder":{"components":[]}}}
```

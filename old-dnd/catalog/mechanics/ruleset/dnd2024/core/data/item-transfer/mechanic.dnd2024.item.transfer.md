---
id: mechanic.dnd2024.item.transfer
category: ruleset.dnd2024.core.data.item-transfer
name: Transfer physical item
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Transfers one complete physical item from its declared direct source to an explicit destination
after ordinary direct-container admission.

## Matches

transfer physical item
move item
stow item
take item
pick up item
retrieve item
give item

## Requirements

```json
{"roles":{"item":{"components":["dnd2024.item-instance","dnd2024.item-quantity","dnd2024.equipment-state"],"includeContents":true,"contentsDepth":4,"componentReferences":[{"sourceComponentId":"dnd2024.item-instance","field":"definitionId","targetComponentIds":["dnd2024.item-definition"]}]},"source":{"components":[]},"destination":{"components":["dnd2024.item-instance"],"includeContents":true,"contentsDepth":4,"contentComponentIds":["dnd2024.item-instance","dnd2024.item-quantity"],"componentReferences":[{"sourceComponentId":"dnd2024.item-instance","field":"definitionId","targetComponentIds":["dnd2024.item-definition"]}]}}}
```

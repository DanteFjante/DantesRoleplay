---
id: mechanic.dnd2024.item.transfer
category: ruleset.dnd2024.core.data.item-transfer
name: Transfer physical item
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Moves one whole item from a declared direct source after ordinary destination admission.

## Matches

transfer physical item
stow item
give item

## Requirements

```json
{"roles":{"item":{"components":["dnd2024.item-instance","dnd2024.item-quantity","dnd2024.equipment-state"],"includeContents":true,"contentsDepth":4,"componentReferences":[{"sourceComponentId":"dnd2024.item-instance","field":"definitionId","targetComponentIds":["dnd2024.item-definition"]}]},"source":{"components":[]},"destination":{"components":["dnd2024.item-instance"],"includeContents":true,"contentsDepth":4,"contentComponentIds":["dnd2024.item-instance","dnd2024.item-quantity"],"componentReferences":[{"sourceComponentId":"dnd2024.item-instance","field":"definitionId","targetComponentIds":["dnd2024.item-definition"]}]}}}
```

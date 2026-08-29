---
id: mechanic.dnd2024.item-activity.use
category: ruleset.dnd2024.core.data.item-activity
name: Use fixed physical item activity
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Consumes a descriptor-stated quantity from one fungible physical item stack and atomically creates
the descriptor-stated ordinary item in the same direct container.

## Matches

use item activity
redeem item
open consumable item

## Requirements

```json
{"roles":{"item":{"components":["dnd2024.item-instance","dnd2024.item-quantity"],"includeContents":true,"contentsDepth":1,"description":"The directly contained fungible physical stack whose declared activity is used."},"definition":{"components":["dnd2024.item-definition","dnd2024.item-activity"],"description":"The immutable definition that owns the selected activity."},"grantDefinition":{"components":["dnd2024.item-definition"],"description":"The immutable definition exactly named by the activity's fixed grant."}}}
```

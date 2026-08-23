---
id: mechanic.dnd2024.item-stack.record
category: ruleset.dnd2024.core.data.item-quantity
name: Record fungible item stack
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Records the initial positive quantity for an existing fungible physical item instance. It does not
change definition identity or containment.

## Matches

record item stack
mark item stack

## Requirements

```json
{"roles":{"item":{"components":["dnd2024.item-instance","dnd2024.item-quantity"],"description":"The existing physical item instance becoming a stack."},"definition":{"components":["dnd2024.item-definition"],"description":"The immutable fungible definition the item instance must already name."}}}
```

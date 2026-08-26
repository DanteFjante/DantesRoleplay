---
id: mechanic.dnd2024.item-stack.consume
category: ruleset.dnd2024.core.data.item-quantity
name: Consume fungible item quantity
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Reduces a fungible stack; consuming the final unit deletes the physical stack entity.

## Matches

consume item stack

## Requirements

```json
{"roles":{"item":{"components":["dnd2024.item-instance","dnd2024.item-quantity"],"includeContents":true,"contentsDepth":1},"definition":{"components":["dnd2024.item-definition"]}}}
```

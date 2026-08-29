---
id: mechanic.dnd2024.item-stack.consume
category: ruleset.dnd2024.core.data.item-quantity
name: Consume fungible item stack quantity
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Reduces a selected fungible stack by a positive count. Consuming the whole stack deletes its
physical entity rather than storing zero.

## Matches

consume item stack
consume items
spend item quantity

## Requirements

```json
{"roles":{"item":{"components":["dnd2024.item-instance","dnd2024.item-quantity"],"includeContents":true,"contentsDepth":1,"description":"The fungible stack to consume, with no direct contents."},"definition":{"components":["dnd2024.item-definition"],"description":"The immutable fungible definition named by the stack."}}}
```

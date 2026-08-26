---
id: mechanic.dnd2024.item-stack.create-and-place
category: ruleset.dnd2024.core.data.item-quantity
name: Create and place fungible item stack
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Administrative helper that atomically creates and directly places a positive fungible stack.

## Matches

administratively create item stack

## Requirements

```json
{"roles":{"definition":{"components":["dnd2024.item-definition"]},"destination":{"components":["dnd2024.item-instance","dnd2024.item-quantity"]}}}
```

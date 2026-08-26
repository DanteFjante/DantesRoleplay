---
id: mechanic.dnd2024.item-stack.split
category: ruleset.dnd2024.core.data.item-quantity
name: Split fungible item stack
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Creates a same-definition stack with a strictly smaller positive count in the same direct container.

## Matches

split item stack

## Requirements

```json
{"roles":{"source":{"components":["dnd2024.item-instance","dnd2024.item-quantity"],"includeContents":true,"contentsDepth":1},"definition":{"components":["dnd2024.item-definition","dnd2024.item-instance","dnd2024.item-quantity"]}}}
```

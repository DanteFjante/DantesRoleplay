---
id: mechanic.dnd2024.item-stack.merge
category: ruleset.dnd2024.core.data.item-quantity
name: Merge fungible item stacks
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Adds an explicit source stack to an explicit same-container target and deletes the source atomically.

## Matches

merge item stacks

## Requirements

```json
{"roles":{"source":{"components":["dnd2024.core.definition-link","dnd2024.item.quantity"],"includeContents":true,"contentsDepth":1},"target":{"components":["dnd2024.core.definition-link","dnd2024.item.quantity"],"includeContents":true,"contentsDepth":1},"definition":{"components":["dnd2024.item-definition"]}}}
```

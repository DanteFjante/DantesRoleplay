---
id: mechanic.dnd2024.item-stack.split
category: ruleset.dnd2024.core.data.item-quantity
name: Split fungible item stack
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Creates a new same-definition stack with a strictly smaller positive count, preserving the source
stack's direct containment when it has one. It does not transfer the new stack to another entity.

## Matches

split item stack
split stack

## Requirements

```json
{"roles":{"source":{"components":["dnd2024.item-instance","dnd2024.item-quantity"],"includeContents":true,"contentsDepth":1,"description":"The fungible source stack with no direct contents."},"definition":{"components":["dnd2024.item-definition"],"description":"The immutable fungible definition named by the source."}}}
```

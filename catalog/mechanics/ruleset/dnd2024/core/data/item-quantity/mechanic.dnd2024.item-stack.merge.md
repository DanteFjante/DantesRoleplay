---
id: mechanic.dnd2024.item-stack.merge
category: ruleset.dnd2024.core.data.item-quantity
name: Merge fungible item stacks
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Adds an explicitly selected source stack to an explicitly selected target stack, then deletes the
source. Both stacks must have the same immutable definition/key and direct container.

## Matches

merge item stacks
merge stacks

## Requirements

```json
{"roles":{"source":{"components":["dnd2024.item-instance","dnd2024.item-quantity"],"includeContents":true,"contentsDepth":1,"description":"The stack consumed by the merge, with no direct contents."},"target":{"components":["dnd2024.item-instance","dnd2024.item-quantity"],"includeContents":true,"contentsDepth":1,"description":"The deterministic retained stack, with no direct contents."},"definition":{"components":["dnd2024.item-definition"],"description":"The immutable fungible definition named by both stacks."}}}
```

---
id: mechanic.dnd2024.item-instance.record
category: ruleset.dnd2024.core.data.item-instance
name: Record physical item instance
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Attaches the initial immutable definition reference to an existing campaign entity. It never changes
an existing reference and does not place the item.

## Matches

record item instance
mark physical item

## Requirements

```json
{"roles":{"item":{"components":["dnd2024.item-instance","dnd2024.item-definition"],"description":"The existing campaign entity becoming a physical item."},"definition":{"components":["dnd2024.item-definition"],"description":"The immutable catalog item definition this physical item names."}}}
```

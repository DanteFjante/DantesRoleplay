---
id: mechanic.dnd2024.item-instance.create-and-place
category: ruleset.dnd2024.core.data.item-instance
name: Create and place physical item
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Administrative fixture/bootstrap helper. Atomically creates a campaign item entity, records its
immutable definition reference, and moves it to an explicit destination. It does not evaluate
capacity or content permission; normal gameplay movement uses item transfer.

## Matches

administratively create physical item
administratively grant physical item

## Requirements

```json
{"roles":{"definition":{"components":["dnd2024.item-definition"],"description":"The immutable catalog definition for the new physical item."},"destination":{"components":[],"description":"The explicit entity that will directly contain the new item."}}}
```

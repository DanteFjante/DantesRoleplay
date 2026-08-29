---
id: mechanic.dnd2024.item-instance.move
category: ruleset.dnd2024.core.data.item-instance
name: Move physical item
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Administrative fixture/bootstrap helper. Moves an existing physical item to one explicit direct
container without admission; normal gameplay movement uses item transfer.

## Matches

administratively move physical item

## Requirements

```json
{"roles":{"item":{"components":["dnd2024.item-instance"],"description":"The existing physical item to move."},"destination":{"components":[],"description":"The explicit direct destination container."}}}
```

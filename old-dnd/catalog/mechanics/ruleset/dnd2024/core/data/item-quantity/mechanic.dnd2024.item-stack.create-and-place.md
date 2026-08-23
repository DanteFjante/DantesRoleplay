---
id: mechanic.dnd2024.item-stack.create-and-place
category: ruleset.dnd2024.core.data.item-quantity
name: Create and place fungible item stack
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Administrative fixture/bootstrap helper. Atomically creates a fungible physical item stack with a
positive count and an explicit direct destination. It does not evaluate capacity or permission;
normal gameplay movement uses item transfer.

## Matches

administratively create physical item stack
administratively grant physical item stack

## Requirements

```json
{"roles":{"definition":{"components":["dnd2024.item-definition"],"description":"The immutable fungible catalog definition."},"destination":{"components":[],"description":"The explicit entity that will directly contain the new stack."}}}
```

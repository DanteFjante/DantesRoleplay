---
id: procedure.mechanic.dnd2024.item-instance
category: ruleset.dnd2024.core.data.item-instance
name: Create and place physical D&D 2024 items
governs: dnd2024.core.definition-link; mechanic.dnd2024.item-instance.record; mechanic.dnd2024.item-instance.read; mechanic.dnd2024.item-instance.create-and-place; mechanic.dnd2024.item-instance.move
status: active
---

## Description

Defines a campaign physical item that stores only its exact immutable definition reference. Containment is
its sole custody/location.

## Instructions

Use record once for an existing entity, create-and-place for administrative bootstrap, read for a
derived view, and item transfer for ordinary movement.

## Constraints

Identity is write-once. Administrative create/place and move do no admission; normal custody moves
use item transfer. Quantity, equipment, capacity, permission, and derived totals are separate.

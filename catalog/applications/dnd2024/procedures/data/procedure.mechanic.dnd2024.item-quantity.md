---
id: procedure.mechanic.dnd2024.item-quantity
category: ruleset.dnd2024.core.data.item-quantity
name: Manage fungible D&D 2024 item quantities
governs: dnd2024.item-quantity; mechanic.dnd2024.item-stack.record; mechanic.dnd2024.item-stack.create-and-place; mechanic.dnd2024.item-stack.split; mechanic.dnd2024.item-stack.merge; mechanic.dnd2024.item-stack.consume
status: active
---

## Description

Owns positive-count fungible stacks whose derived compatibility key equals the exact definition ID.

## Instructions

Use record/create for admission, split and merge for conservation, and consume for decrement or
final entity deletion.

## Constraints

Zero is represented only by entity deletion. Split/merge conserve count, merge targets are explicit,
and stacks with direct contents cannot be split, merged, or deleted. Containment remains custody.

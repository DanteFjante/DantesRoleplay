---
id: procedure.mechanic.dnd2024.item-burden
category: ruleset.dnd2024.core.data.item-burden
name: Derive exact containment-tree physical mass
governs: mechanic.dnd2024.item-burden.read
status: active
---

## Description

Derives exact rational canonical kilogram mass for a bounded containment subtree.

## Instructions

Resolve every item to its immutable definition, read `dnd2024.item.physical.weight`, require its
canonical kilogram unit, multiply by positive quantity, and sum with overflow-checked rational
arithmetic.

## Constraints

Burden is never stored. Missing item identity, definition, weight, or positive quantity fails closed;
unbounded traversal, capacity admission, and magic exceptions are separate.

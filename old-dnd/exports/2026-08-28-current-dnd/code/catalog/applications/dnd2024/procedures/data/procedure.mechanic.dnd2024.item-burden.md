---
id: procedure.mechanic.dnd2024.item-burden
category: ruleset.dnd2024.core.data.item-burden
name: Derive exact containment-tree physical mass
governs: mechanic.dnd2024.item-burden.read
status: active
---

## Description

Derives exact rational physical mass for a bounded containment subtree.

## Instructions

Resolve every item to its immutable definition, multiply mass by compatible quantity, and sum with
overflow-checked rational arithmetic.

## Constraints

Burden is never stored. Missing item identity, definition, mass, or required quantity fails closed;
unbounded traversal, capacity admission, and magic exceptions are separate.

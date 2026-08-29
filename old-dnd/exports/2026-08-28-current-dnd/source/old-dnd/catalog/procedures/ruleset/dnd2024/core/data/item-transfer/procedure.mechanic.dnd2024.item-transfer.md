---
id: procedure.mechanic.dnd2024.item-transfer
category: ruleset.dnd2024.core.data.item-transfer
name: Transfer physical items with ordinary admission
governs: mechanic.dnd2024.item.transfer
status: active
---

## Description

Moves one whole physical item from its declared direct source to an explicit destination after
checking the destination's published direct item-count, weight, and permitted-kind capacity.
Containment remains the single custody authority.

## Instructions

1. Use `mechanic.dnd2024.item.transfer`; it names `item`, `source`, and `destination`, and proposes
   one containment move only after direct-custody and admission checks.
2. A non-item destination is an unconstrained custody root in this slice. An item destination with
   no capacity is not an ordinary container and rejects a transfer.

## Constraints

- This slice transfers whole instances only. Stack splitting/merging remains the quantity owner.
- Capacity uses exact definition mass and quantity; item-count capacity counts each unit of a stack.
  Malformed/missing physical data fails closed.
- Direct container capacity intentionally does not become an inferred carrying-capacity or
  ancestor-capacity rule. Those consumers remain separately governed.
- The mechanic refuses direct source mismatch and visible self/descendant cycles; the containment
  writer remains the final cycle authority. A rejected transfer returns no effects.

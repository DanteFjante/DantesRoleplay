---
id: procedure.mechanic.dnd2024.equipment-state
category: ruleset.dnd2024.core.data.equipment-state
name: Set D&D 2024 item equipment state
governs: dnd2024.equipment-state and mechanics dnd2024.item.equip, dnd2024.item.unequip, dnd2024.item.equipment.read
status: active
---

## Description

Records whether a directly possessed physical item is held, worn, or explicitly unequipped.
Containment remains the only custody/location authority; equipment state names neither an owner nor
a body slot.

## Instructions

1. Use `mechanic.dnd2024.item.equip` with the item and its direct holder. The immutable definition
   must explicitly declare the requested `equipmentModes` value.
2. Use `mechanic.dnd2024.item.unequip` with the item and its same direct holder. It records the
   explicit `unequipped` state without moving the item.
3. Use `mechanic.dnd2024.item.equipment.read` for the narrow effect-free equipment seam. A later
   weapon or armor consumer must apply its own eligibility and mechanical consequences.

## Constraints

- An item must be directly contained by the named holder before it can be equipped or unequipped.
  Nested/stowed and uncontained items are inaccessible for this slice.
- Fungible stacks cannot be equipped. A held or worn item cannot use the normal transfer route;
  unequip it before moving it.
- No universal slots, hand counts, armor class, weapon attacks, ammunition, don/doff time, or
  action/resource costs are implied. Those are owned by Features 12, 24, and 25.
- `unequipped` is explicit state, not inferred from absence. Malformed/missing state fails closed.

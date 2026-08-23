---
id: procedure.mechanic.dnd2024.item-instance
category: ruleset.dnd2024.core.data.item-instance
name: Create and place physical D&D 2024 items
governs: dnd2024.item-instance lifecycle and direct containment-based custody
status: active
---

## Description

Defines a campaign-local physical item instance. The instance contains only one immutable
definition id; containment gives its only location/custody. This procedure intentionally provides
no inventory collection, owner field, quantity, stack behavior, capacity check, current equipment
state, or economy operation.

## Instructions

1. Use `dnd2024.item-definition` on an existing, live catalog definition role to establish an
   instance reference. Callers never supply a raw definition id to a lifecycle writer.
2. Use `mechanic.dnd2024.item-instance.record` only to attach the initial instance component to
   an existing non-definition entity. It records once and never corrects a reference in place.
3. Use `mechanic.dnd2024.item-instance.create-and-place` only for administrative fixture or
   bootstrap placement. It creates the entity, adds the instance component, and moves it to an
   explicit destination in one atomic effect list, but deliberately does not admit capacity.
4. Use `mechanic.dnd2024.item-instance.move` only for administrative fixture or bootstrap
   relocation. Normal custody movement is governed by `mechanic.dnd2024.item.transfer`, which
   performs Slice 7 admission before it emits a containment move.
5. Use `mechanic.dnd2024.item-instance.read` for an effect-free declaration of an item's exact
   definition id and direct containment context.

## Constraints

- The definition id is permanent. If an existing instance points at the wrong definition, use a
  separately governed migration; this feature has no `correct` path that silently changes identity.
- A definition entity cannot itself become a physical instance. Existing Feature 7 weapon-profile
  entities remain canonical combat definitions, not carried weapons.
- A rejected record/create/move changes nothing. Create-and-place must produce exactly entity.create,
  component.add, then containment.move effects in that order. Administrative helpers are not
  player-facing transfer routes.
- Do not add quantities, merge/split, capacity, nested admission, legal ownership, equipment
  eligibility, currency transactions, source copies, or derived totals. Current held/worn state is
  separately governed by `procedure.mechanic.dnd2024.equipment-state`.

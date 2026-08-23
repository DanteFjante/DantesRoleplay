---
id: procedure.mechanic.dnd2024.item-quantity
category: ruleset.dnd2024.core.data.item-quantity
name: Manage fungible D&D 2024 item quantities
governs: dnd2024.item-quantity stack lifecycle, split, merge, and consumption
status: active
---

## Description

Defines a positive-count stack for an existing physical item instance whose immutable definition
declares `stackPolicy: "fungible"`. In this slice, the canonical stack key is exactly the
instance's exact versioned definition id. Containment remains the only location/custody model.

## Instructions

1. Use `mechanic.dnd2024.item-stack.create-and-place` only for administrative fixture or bootstrap
   placement. It emits entity creation, the immutable instance reference, quantity, and containment
   in one atomic effect list, but does not perform transfer admission.
2. Use `mechanic.dnd2024.item-stack.record` only to attach the initial quantity to an existing
   fungible item instance. It records once; it never corrects a count or stack key in place.
3. Use `mechanic.dnd2024.item-stack.split` to create a second same-definition stack, reducing the
   selected source by a strictly smaller positive count. The new stack initially shares the
   source's direct containment, if any.
4. Use `mechanic.dnd2024.item-stack.merge` only with an explicitly selected source and target that
   share one exact stack key and direct container. It deletes the source entity after adding its
   count to the target; the target is deterministic because the caller names it.
5. Use `mechanic.dnd2024.item-stack.consume` to reduce one stack. Consuming its final unit deletes
   the physical stack entity instead of writing a zero quantity.

## Constraints

- Quantity is a positive safe integer; no zero, negative, fractional, or manually selected stack
  key is valid. The zero policy is entity deletion only after the entire stack is consumed or
  merged.
- Only definitions whose static `stackPolicy` is `fungible` may receive this component. In this
  slice the key must equal the instance's exact `definitionId`; matching names, kinds, weights, or
  source text is insufficient.
- Split and merge preserve the total count. Merge never searches for a target or moves an item;
  both explicitly selected stacks must already have the same direct container. Capacity and
  permitted-content admission begin in Slice 7.
- A stack with direct contents cannot be split, merged, or wholly consumed. This keeps deletion
  from hiding separately live contained entities.
- Normal player-facing movement of a whole stack is governed by
  `mechanic.dnd2024.item.transfer`; it preserves the count and stack key while it validates the
  declared source and destination admission.
- No quantity may be attached to a `separate` item, definition entity, charged/identified/unique
  object, equipment state, currency value transaction, inventory array, owner field, or derived
  burden. Those later properties must establish their own stack-key compatibility rule before
  merging is allowed.

## Verification

- Fresh-import the catalog and validate the closed quantity schema.
- Prove create, record, split, merge, and consume are atomic; rejected inputs leave all counts and
  entities unchanged.
- Prove count conservation, explicit deterministic merge target, definition/key/container refusal,
  direct-content refusal, and final-unit deletion.

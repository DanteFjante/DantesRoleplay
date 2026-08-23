---
id: procedure.mechanic.dnd2024.item-activity
category: ruleset.dnd2024.core.data.item-activity
name: Use immutable D&D 2024 item activities
governs: dnd2024.item-activity and mechanic.dnd2024.item-activity.use
status: active
---

## Description

Defines the first closed item-activity seam. An immutable definition can declare a
`consume-and-grant-item` activity which consumes a stated positive quantity of its physical
fungible stack and creates one descriptor-specified physical item in that stack's direct container.

## Instructions

1. Attach `dnd2024.item-activity` only to the immutable definition entity that owns the activity.
   Every activity id is stable and unique within that definition.
2. Use `mechanic.dnd2024.item-activity.use` with the selected source stack, its exact immutable
   definition, and the exact immutable definition named by the selected activity's grant.
3. Supply only `activityId` and a new `grantItemId`. The descriptor fixes the granted definition,
   display name, slot, direct-container target, and consumed quantity; callers never supply a
   generic effect, target, component payload, price, or replacement item data.

## Constraints

- Only a compatible fungible stack can be consumed. Its exact stack key must equal the source
  definition id, it must be directly contained, and it must have no direct contents. The activity
  fails before effects if count, definition, grant role, or placement is invalid.
- The only supported kind creates exactly one ordinary physical item, attaches only its immutable
  `dnd2024.item-instance` reference, and places it in the source stack's direct container. The
  consume decrement/delete and grant create/reference/place effects are one atomic list.
- No arbitrary JS, effect list, component grant, item quantity grant, currency change, transfer,
  check, action cost, target selection, magic effect, or item scripting is permitted. New activity
  kinds require a separately confirmed contract and mechanics.
- This slice does not add a source fixture or claim an SRD item has a use action. Feature 25 owns
  ammunition use; Feature 29 owns magic item effects; Feature 30 may author creation-package
  activities through this fixed seam only when it has a source-backed definition.

## Verification

- Prove a descriptor-selected activity preserves source-stack arithmetic while creating and
  placing its exact granted item atomically.
- Prove a mismatched grant-definition role, insufficient quantity, direct source contents, or a
  duplicate grant id produces no partial mutation.
- Prove the descriptor schema rejects arbitrary effect data and an activity without its fixed grant.

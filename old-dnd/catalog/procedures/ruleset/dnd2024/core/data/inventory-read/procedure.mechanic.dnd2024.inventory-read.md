---
id: procedure.mechanic.dnd2024.inventory-read
category: ruleset.dnd2024.core.data.inventory-read
name: Inspect bounded D&D 2024 physical inventory
governs: mechanic.dnd2024.inventory.read and its bounded read-only physical-item inspection shape
status: active
---

## Description

Provides a read-only view of physical D&D 2024 items beneath one explicit custody root. It is an
inspection of containment, not an inventory array, ownership assertion, wallet, burden cache, or
transfer bypass.

## Instructions

1. Use `mechanic.dnd2024.inventory.read` with the creature, container, or other custody root to
   inspect. It returns a deterministic pre-order list of visible physical item instances and any
   visible non-item contents.
2. Treat `mayOmitDeeperContents: true` as material. The generic projection is explicitly bounded
   to four containment levels, so the reader never claims the output is a complete inventory.
3. Use the existing narrow readers for derived concerns: burden/carrying capacity, currency value,
   and equipment state each retain their own contracts and failure behavior.

## Constraints

- The reader emits no effects and never infers legal owner, accessibility, equipment eligibility,
  capacity admission, value, weight, or a missing item from the bounded view.
- An item instance must resolve its exact immutable definition. A fungible definition requires a
  compatible positive quantity; a separate definition must not have quantity. Corrupt visible item
  state fails closed rather than being rendered as a normal item.
- Non-item contained entities are disclosed as `unclassifiedContents`; they are not counted as
  physical items or silently discarded.
- No client may use this shape to write containment, quantities, components, or a cached total.

## Verification

- Prove nested physical items and equipment state appear deterministically without effects.
- Prove visible non-item contents are disclosed, and the four-depth bound is explicit rather than
  misrepresented as a complete inventory.
- Prove the existing currency, carrying, and equipment consumer seams remain usable with the same
  custody fixture.

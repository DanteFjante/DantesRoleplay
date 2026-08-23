---
id: procedure.mechanic.dnd2024.item-burden
category: ruleset.dnd2024.core.data.item-burden
name: Derive exact containment-tree physical mass
governs: mechanic.dnd2024.item-burden.read and its exact recursive mass derivation
status: active
---

## Description

Reads the exact physical mass represented by an entity's bounded containment subtree. Static mass
and ordinary container capability remain solely on immutable item definitions; campaign item
instances retain only their definition reference, and quantities remain separate mutable state.

## Instructions

1. Use `mechanic.dnd2024.item-burden.read` with an explicit root entity. The root may be a
   creature/custody root or a physical item instance.
2. The mechanic requests the bounded containment projection and declared `definitionId` references
   to immutable definitions. It never accepts caller-supplied mass, quantity, or totals.
3. Calculate each item self mass as exact `massPounds × quantity`; a separate item has quantity
   one, and a fungible definition requires a valid quantity component whose stack key equals its
   exact definition id. Sum child subtree masses with exact rational arithmetic.

## Constraints

- This is read-only. Burden, direct load, and remaining capacity are derived output, never stored
  components or authority for inventory.
- A missing/corrupt definition, measure, quantity, stack key, reference, non-item contained node,
  unsupported depth/node count, or arithmetic overflow fails rather than treating mass as zero.
- The resolver has the generic projection's fixed depth/fan-out limits. It reports omission rather
  than silently claiming a complete total outside that bounded view.
- `capacity` fields on immutable definitions are source-backed data for later admission rules. This
  slice does not reject a move, calculate remaining capacity, create volume, invent slots, or apply
  creature carrying limits or magic-container exceptions.

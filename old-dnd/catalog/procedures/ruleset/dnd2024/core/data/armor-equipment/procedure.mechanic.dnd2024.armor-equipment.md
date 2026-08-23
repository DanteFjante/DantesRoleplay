---
id: procedure.mechanic.dnd2024.armor-equipment
category: ruleset.dnd2024.core.data.armor-equipment
name: Read directly equipped D&D 2024 armor and Shield
governs: mechanic.dnd2024.armor-equipment.read and its direct-custody armor/Shield aggregation shape
status: active
---

## Description

Derives one creature's direct worn armor suit and direct held Shield from Feature 23 physical
custody, explicit equipment state, and immutable armor definitions. It is an effect-free selection
seam for later Feature 24 consumers, never a stored loadout or an equipment mutation.

## Instructions

1. Use `mechanic.dnd2024.armor-equipment.read` with a creature as `subject` and exactly `{}` input.
2. Inspect direct containment only. A nested/stowed item does not qualify even if it has an invalid
   or stale equipment component.
3. A qualifying armor item is separate, references a valid immutable definition of kind `armor`,
   and has explicit equipment state `worn`. A qualifying Shield is the equivalent kind `shield` with
   explicit state `held`. Explicit `unequipped` state is valid but unselected.
4. Return `armor` and `shield` selections/null, each with item id, definition id, state, immutable
   profile, and source attribution. Any duplicate qualifying selection or invalid direct relevant
   item/definition/equipment state fails rather than choosing arbitrarily.

## Constraints

- Containment remains the sole custody authority. Do not add owner, wornArmorId, shieldId, slot, or
  loadout state to the creature.
- This reader applies no armor training, AC, D20, Speed, spellcasting, action, don/doff, burden, or
  capacity consequence. Feature 23's burden reader remains the recursive self-mass plus contents
  authority for containers.
- Missing or invalid relevant state is not unequipped and must not create a default selection.

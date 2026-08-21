---
id: procedure.mechanic.dnd2024.spell-resolution-profile
category: ruleset.dnd2024.core.data.spell-resolution-profile
name: Define immutable D&D 2024 spell-resolution profiles
governs: catalog authoring of dnd2024.spell-resolution-profile on versioned spell content entities with dnd2024.spell-identity
status: active
---

## Description

Defines immutable source-cited spell-resolution profile catalog data. A profile declares the
downstream rule families a source spell needs; it is not a cast operation, active spell effect, or
replacement for Feature 31 spellcasting resources.

## Instructions

1. Attach exactly one profile to the same permanent `content.dnd2024.spell.<key>.v<version>`
   entity as its matching `dnd2024.spell-identity`. The key, version, and source reference must
   agree exactly; a correction creates a reviewed successor entity instead of rewriting referenced
   source data.
2. Record only the declared action, range/target/area, duration, concentration, resolution, and
   canonical consequence-family interfaces.
3. Seed only Fire Bolt, Cure Wounds, and Dancing Lights. They demonstrate an instantaneous spell
   attack, an instantaneous declared-special consequence, and a concentration-duration declared-
   special consequence without making any spell playable.

## Constraints

- Feature 31 owns availability, spell lists, slots, casting ability, save DC, attack modifier, and
  all resource transitions. This profile grants none of them.
- Feature 32’s later cast root owns composition with Feature 12 action spending and Feature 31 slot
  spending. A declared action family never spends an Action, Bonus Action, or Reaction here.
- Do not encode numeric range, target id, area coordinates, components, slot level, DC, modifier,
  dice, damage/healing amount, duration remaining, effect id, condition, executable code, or an
  arbitrary payload.
- `requiresConcentration` is source metadata only. It does not create an effect, store a creature
  concentration reference, begin an expiry clock, or end anything; Feature 18 remains the sole
  concentration-state owner.

## Verification

- Fresh-import the catalog and prove each profile agrees with its co-located spell identity and
  has no spellcasting/action/effect mechanic.
- Reject mismatched identity/source/version/family data, impossible duration/concentration pairs,
  and extra executable data through the closed schema and focused assertions. Reads are
  deterministic and effect-free.

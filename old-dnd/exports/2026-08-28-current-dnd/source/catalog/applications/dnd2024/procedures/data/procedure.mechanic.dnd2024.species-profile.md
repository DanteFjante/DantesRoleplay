---
id: procedure.mechanic.dnd2024.species-profile
category: ruleset.dnd2024.core.data.species-profile
name: Define immutable D&D 2024 species profiles
governs: catalog authoring of dnd2024.species-profile on versioned dnd2024.character.content-definition species entities
status: active
---

## Description

Defines the source-cited immutable D&D 2024 species profile catalog used by character creation. A
profile belongs only to a versioned content definition, never to a creature or campaign. It makes
source facts available for deterministic reading without selecting a species or executing a trait.

## Instructions

1. Attach `dnd2024.species-profile` only to an entity whose
   `dnd2024.character.content-definition` has `kind: species`. Identity, version, and source must
   agree exactly.
2. Use permanent `content.dnd2024.species.<key>.v<version>` identities. Correct static facts through
   a reviewed new content version rather than revising an established definition in place.
3. Record Humanoid type, permitted Size values, base five-mode Speed facts, and ordered trait and
   choice-family declarations from `source.dnd2024.srd-5.2.1`, *Character Origins > Character
   Species* (PDF pp. 83–86).
4. Keep trait and choice keys declarative. A later named owner must prove the declaration before
   applying a consequence.

## Constraints

The profile is immutable catalog data, not actor state. It cannot select a species or update Size,
Speed, Hit Points, proficiencies, conditions, inventory, campaign state, or turn state. A zero
special Speed means the base profile has no such movement mode; it is not an executable trait.

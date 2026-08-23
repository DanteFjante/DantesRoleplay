---
id: procedure.mechanic.dnd2024.species-profile
category: ruleset.dnd2024.core.data.species-profile
name: Define immutable D&D 2024 species profiles
governs: catalog authoring of dnd2024.species-profile on versioned dnd2024.character.content-definition species entities
status: active
---

## Description

Defines the source-cited immutable D&D 2024 species profile catalog used by later character-origin
work. A profile belongs only to a versioned content definition, never to a creature or campaign.
It makes source facts available for deterministic reading without selecting a species or executing
one of its traits.

## Instructions

1. Attach `dnd2024.species-profile` only to an entity whose `dnd2024.character.content-definition`
   has `kind: species`. Its content key, content version, and source reference must agree exactly
   with the profile.
2. Use a permanent `content.dnd2024.species.<key>.v<version>` identity. A correction to static
   facts is a reviewed new content version; never revise an established definition in place.
3. Record the source's Humanoid type, permitted Size values, base five-mode Speed facts, and the
   ordered source trait and choice-family declarations. A zero special Speed declares that the
   base profile has no such movement mode.
4. Keep trait and choice keys as declarations. Later selected-species and trait-family owners must
   prove a declaration before applying a consequence through their own state owner.

## Constraints

- This component is immutable catalog data, not actor state. It cannot select a species or update
  creature type, Size, Speed, Hit Points, proficiencies, conditions, inventory, campaign state, or
  a turn budget.
- Do not encode selected ancestry/lineage, spells, skills, Feats, damage types, resources,
  durations, action costs, targets, or executable trait payloads.
- Feature 30 owns character-origin assembly; Features 20 and 23 remain the sole actor Speed and
  Size owners. Feature 17's player-character/monster branch is not inferred from `humanoid`.
- This slice has no action mechanic. Profiles are reviewed catalog definitions, read with zero
  effects; runtime mutation requires a separately governed content revision.

## Verification

- Fresh-import the catalog and prove the nine SRD species have one active v1 identity/profile with
  matching keys, versions, source references, static Size/Speed facts, and ordered declarations.
- Reject malformed, unknown, duplicate, out-of-order, mismatched, or extra-field data through the
  closed schema and focused catalog assertions.
- Prove that reading these definitions creates no actor or campaign state and no effects.

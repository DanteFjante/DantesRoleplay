---
id: procedure.mechanic.dnd2024.species-selection
category: ruleset.dnd2024.character.species-selection
name: Select an immutable D&D 2024 species definition
governs: dnd2024.selected-species and mechanic.dnd2024.species-selection.resolve
status: active
---

## Description

Owns the actor-side reference to one selected immutable species definition and its internal
zero-write staged resolver. It does not apply any species source fact, trait, or choice.

## Instructions

1. `dnd2024.selected-species` is present at most once on an actor and contains exactly one
   canonical `speciesDefinitionId`. The referenced entity must have one active CH1 `species`
   content identity and one matching valid `dnd2024.species-profile`.
2. `ICharacterSpeciesSelectionResolver` accepts a CH5-bound actor ID and trusted definition ID.
   It requires valid C15 scope and absent selection state, then returns exactly one
   `component.add` fragment for `dnd2024.selected-species`.
3. CH5 alone appends and applies the fragment. The resolver opens no transaction, makes no direct
   write, and returns no fragment on malformed actor/definition/scope/content/existing-state input.
4. The stored reference does not select an ancestry, lineage, Size, skill, feat, language, or any
   trait consequence. Each is a later source-choice or named-state-owner concern.

## Constraints

- The component stores no display name, source reference, species key/version, creature type,
  profile copy, trait key, or executable payload. The immutable definition remains authoritative.
- This is not Feature 30's public origin-assembly workflow and records no selection provenance or
  receipt. It is an internal CH5 composition dependency only.
- Size, Speed, skills, languages, feats, Heroic Inspiration, and every species trait remain with
  their established or future owners; this procedure must never write them.

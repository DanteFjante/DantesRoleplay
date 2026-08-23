---
id: procedure.mechanic.dnd2024.character-content-definition
category: ruleset.dnd2024.character.content-definition
name: Record immutable D&D 2024 character content
governs: dnd2024.character.content-definition and mechanic.dnd2024.character-content-definition.record
status: active
---

## Description

Records a versioned, source-cited identity for one D&D 2024 character-content option. It does not
create a character or encode any rules, grants, choices, campaign state, or item state.

## Instructions

1. Create the content entity through the approved catalog authoring workflow, using its permanent
   versioned entity ID and display title.
2. Use `mechanic.dnd2024.character-content-definition.record` only for administrative catalog
   authoring. It records one immutable identity against the registered SRD 5.2.1 source role.
3. Discover content through `dnd2024.character.content-definition` and inspect its closed identity
   and `sourceRef`; active status is not itself a character-creation eligibility decision.

## Constraints

- The identity fields are write-once. A correction or successor is a new versioned entity; archive
  transition and migration belong to CH7.
- The component stores no grant, ability score, proficiency, item, spell, feature behavior,
  character actor, campaign ID, player-control assertion, source prose, or derived result.
- The normal recorder is an internal catalog-authoring dependency, not a player-facing character
  creation action. CH5 alone will own a completed character-creation root transaction.

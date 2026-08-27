---
id: procedure.mechanic.dnd2024.species-versatile-skilled
category: ruleset.dnd2024.character.species-traits.versatile
name: Resolve Human Versatile with the Skilled Origin feat
governs: mechanic.dnd2024.species-versatile-skilled.resolve
status: active
---

## Description

Resolves the source-recommended Human Versatile selection of Skilled into one feat reference and
three skill/tool proficiency contributions without writing character state.

## Instructions

1. Bind a canonical active species profile declaring `versatile` and the canonical active Skilled
   Origin-feat definition.
2. Accept exactly three unique `{kind,id}` choices using the complete skill and tool owner
   vocabularies.
3. Canonicalize choices into independent `set-union` contributions targeting
   `dnd2024.skill-proficiencies.skills` and `dnd2024.tool-proficiencies.tools`.
4. Let the final character-creation root combine these with every other proficiency contribution,
   reject cross-source duplicates when they would fail to grant the selected count, and write each
   complete component once.

## Constraints

This resolver creates no feat state, item, actor component, receipt, effect, proficiency bonus,
Expertise, tool item, spell, or benefit from another Origin feat.

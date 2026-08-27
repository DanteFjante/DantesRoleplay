---
id: procedure.mechanic.dnd2024.species-skillful
category: ruleset.dnd2024.character.species-traits.skillful
name: Resolve the D&D 2024 Skillful species trait
governs: mechanic.dnd2024.species-skillful.resolve
status: active
---

## Description

Resolves the Skillful trait's one skill-proficiency choice as a pure contribution to the eventual
character-creation skill set.

## Instructions

1. Bind an active canonical species definition whose immutable profile declares `skillful`.
2. Accept exactly one of the 18 skill IDs owned by `dnd2024.skill-proficiencies`.
3. Return one `set-union` contribution targeting that component's `skills` field. The atomic
   creation root later combines it with background and class grants and writes the complete set
   once.
4. Never infer entitlement from a species name or caller-supplied ID.

## Constraints

This resolver performs no write and grants no Expertise, Proficiency Bonus, ability association,
modifier, or acquisition history. It does not decide duplicate-choice replacement across creation
sources; that belongs to the final combined grant validator.

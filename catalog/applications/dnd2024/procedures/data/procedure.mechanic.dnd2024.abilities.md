---
id: procedure.mechanic.dnd2024.abilities
category: ruleset.dnd2024.core.data.abilities
name: D&D 2024 ability scores
governs: component definition dnd2024.abilities; any mechanic deriving its modifier
status: active
---

## Description

Stores the six authoritative ability scores used by D&D 2024 rules.

## Instructions

1. Attach one `dnd2024.abilities` component to a creature with exactly `str`, `dex`, `con`, `int`, `wis`, and `cha`, each an integer from 1 through 30.
2. Derive a modifier where it is needed with `Math.floor((score - 10) / 2)`.
3. Never store a modifier, passive value, proficiency, or check result in this component.

Source: `source.dnd2024.srd-5.2.1`, `Playing the Game > The Six Abilities > Ability Scores/Ability Modifiers` (PDF pp. 5–6), CC BY 4.0.

## Constraints

- The component contains no fields beyond the six scores.
- Missing ability state is unknown, never an assumed score of zero.
- This procedure does not establish proficiency, skills, saving throws, or conditions.

---
id: procedure.mechanic.dnd2024.character-sheet
category: ruleset.dnd2024.core.advancement.character-sheet
name: Govern derived core character-sheet numbers
governs: mechanic.dnd2024.character-sheet.read
status: active
---

## Description

Governs a stateless, source-backed view of core character-sheet numbers.

## Instructions

Read the character's authoritative ability scores, total level, skill proficiencies, and saving-
throw proficiencies. Derive Proficiency Bonus, modifiers, Dexterity initiative, and base Passive
Perception for the requested revision. Require exactly an empty input object.

Sources: `source.dnd2024.srd-5.2.1`, `Character Creation > Step 5: Character Creation Details >
Fill In Numbers` (PDF pp. 21–22), and `Rules Glossary > Passive Perception` (PDF p. 185).

## Constraints

- Never store derived modifiers, Proficiency Bonus, initiative, or Passive Perception.
- Contextual Advantage, Disadvantage, expertise, half proficiency, temporary bonuses, spellcasting,
  AC, HP, Speed, inventory, effects, and content entitlements remain separate owners.
- The mechanic consumes no RNG and emits no effects, events, or notifications.

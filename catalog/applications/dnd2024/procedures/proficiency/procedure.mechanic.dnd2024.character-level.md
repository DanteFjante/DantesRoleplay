---
id: procedure.mechanic.dnd2024.character-level
category: ruleset.dnd2024.core.data.character-level
name: Record D&D 2024 character level
governs: dnd2024.character-level; mechanic.dnd2024.character-level.record
status: active
---

## Description

Owns total level as the authoritative source for a character's derived Proficiency Bonus.

## Instructions

Record only an integer from 1 through 20. The recorder fixes the SRD source reference and derives
the bonus as `2 + floor((level - 1) / 4)`; it never stores that bonus.

Source: `source.dnd2024.srd-5.2.1`, `Character Creation > Level Advancement > Character Advancement` (PDF p. 23).

## Constraints

- This is total character level, not class identity, multiclass state, XP, or monster CR.
- Normal writes use the recorder; callers cannot supply sourceRef or a derived bonus.

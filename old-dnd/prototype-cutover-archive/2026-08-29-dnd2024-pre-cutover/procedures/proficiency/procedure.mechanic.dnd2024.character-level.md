---
id: procedure.mechanic.dnd2024.character-level
category: ruleset.dnd2024.core.data.character-level
name: Derive D&D 2024 character level
governs: dnd2024.character.class-membership; dnd2024.character.has-class-membership; mechanic.dnd2024.character-level.read
status: active
---

## Description

Owns independently addressable class memberships and derives their character's total level and
Proficiency Bonus without storing either derived value.

## Instructions

Associate each membership entity with exactly one character through an outgoing
`character.has-class-membership` relationship. Store one canonical class reference and an integer
level from 1 through 20 on each membership. Sum all memberships, reject totals above 20, and derive
the bonus as `2 + floor((totalLevel - 1) / 4)`.

Source: `source.dnd2024.srd-5.2.1`, `Character Creation > Level Advancement > Character Advancement` (PDF p. 23).

## Constraints

- A membership level is class-local; the mechanic output is total character level.
- Duplicate membership entities or duplicate class references are invalid.
- Callers cannot supply total level or Proficiency Bonus.

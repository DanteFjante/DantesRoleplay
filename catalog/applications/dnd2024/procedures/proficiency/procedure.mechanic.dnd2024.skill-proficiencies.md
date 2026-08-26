---
id: procedure.mechanic.dnd2024.skill-proficiencies
category: ruleset.dnd2024.core.data.skill-proficiencies
name: Record D&D 2024 skill proficiencies
governs: dnd2024.skill-proficiencies; mechanic.dnd2024.skill-proficiencies.record
status: active
---

## Description

Owns the explicit known D&D 2024 skill-proficiency set used by named ability checks.

## Instructions

Record only the 18 stable SRD skill IDs. The recorder canonicalizes their order and fixes the source
reference. An empty list means known no skill proficiencies; absence is unknown.

Source: `source.dnd2024.srd-5.2.1`, `Playing the Game > Proficiency > Skill Proficiencies and Skills` (PDF pp. 8–9).

## Constraints

- Do not store a default ability, modifier, Proficiency Bonus, Expertise, class, or acquisition data.
- A named check derives proficiency from this component and character level; it never trusts caller state.

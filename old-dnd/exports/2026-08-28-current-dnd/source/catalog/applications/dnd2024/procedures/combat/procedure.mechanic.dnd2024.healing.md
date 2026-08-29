---
id: procedure.mechanic.dnd2024.healing
category: ruleset.dnd2024.core.gameplay.healing
name: Apply D&D 2024 healing
governs: mechanic.dnd2024.healing.apply
status: active
---

## Description

Owns healing-caused increases to authoritative Hit Points and clamps one positive requested amount
at the existing maximum.

## Instructions

Require exactly one subject with valid Hit Points and input containing only a positive safe-integer
amount. Derive applied and excess amounts, preserve maximum/source, and propose one complete HP set
only when current HP changes. Return the complete requested/applied/lost and before/after values.

## Constraints

Healing never reads or changes Temporary Hit Points, maximum HP, conditions, death state, or another
entity. It consumes no randomness and emits no event or notification on the current direct action
surface. Healing sources and consequences are separate owners.

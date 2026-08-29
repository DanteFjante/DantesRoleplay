---
id: procedure.mechanic.dnd2024.carrying-capacity
category: ruleset.dnd2024.core.data.carrying-capacity
name: Derive D&D 2024 carrying capacities
governs: mechanic.dnd2024.carrying-capacity.read
status: active
---

## Description

Derives an explicit creature's SRD carrying and drag/lift/push capacities from Strength and Size,
and compares them with the composed read-only physical burden.

## Instructions

1. Use `mechanic.dnd2024.carrying-capacity.read` with the creature role. It composes the existing
   burden reader rather than accepting a caller-provided total.
2. Present capacity as exact rational pounds and retain the burden comparison as derived output.

## Constraints

- Tiny: Strength × 7.5 lb carry and ×15 lb drag/lift/push. Small/Medium: ×15/×30. Each larger
  category doubles both values. No Size is assumed.
- This is read-only and creates no Encumbered/Heavily Encumbered speed state; SRD 5.2.1 does not
  supply those thresholds. Movement effects remain Feature 20.
- Missing/corrupt Strength, Size, or burden fails rather than becoming zero. Magic and special
  carrying exceptions remain Feature 29.

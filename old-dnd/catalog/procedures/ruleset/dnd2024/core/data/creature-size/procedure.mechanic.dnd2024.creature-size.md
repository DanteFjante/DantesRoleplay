---
id: procedure.mechanic.dnd2024.creature-size
category: ruleset.dnd2024.core.data.creature-size
name: Record D&D 2024 creature Size
governs: dnd2024.creature-size and mechanic.dnd2024.creature-size.record
status: active
---

## Description

Stores one explicit SRD 5.2.1 Size category on a creature. This shared state is consumed by
Feature 23 carrying and later Feature 20 movement/reach rules; it stores no derived consequence.

## Instructions

1. Use `mechanic.dnd2024.creature-size.record` to attach one closed Size value to a creature.
2. Carrying rules must read this component with `dnd2024.abilities`; they may not infer Size from
   a name, species, token, or containment location.

## Constraints

- Use exactly Tiny, Small, Medium, Large, Huge, or Gargantuan encoded lower-case by the closed
  schema. Missing Size is unknown, never Medium by default.
- Record once. Changing Size needs the later owner of the effect that changes it; this slice has
  no correction path.
- Do not store weight, dimensions, reach, Speed, carrying totals, or encumbrance state here.

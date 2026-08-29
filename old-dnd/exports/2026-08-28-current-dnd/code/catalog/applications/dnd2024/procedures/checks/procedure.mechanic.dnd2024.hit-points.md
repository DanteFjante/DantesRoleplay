---
id: procedure.mechanic.dnd2024.hit-points
category: ruleset.dnd2024.core.data.hit-points
name: Record Hit Points
governs: mechanic.dnd2024.hit-points.write; dnd2024.creature.hit-points
status: active
---

## Description

Records or corrects one bounded current/current-maximum Hit Point pair atomically while preserving
an existing optional maximum reduction.

## Instructions

Accept only `record|correct` plus complete bounded current and maximum values. Add or replace exactly
this component, preserve an existing `maximumReduction`, and keep rule citation in result evidence.

## Constraints

This is neither damage nor healing; it accepts no delta, type, Temporary HP, source, maximum
reduction, or effect. A future reduction mechanic owns changing `maximumReduction`.

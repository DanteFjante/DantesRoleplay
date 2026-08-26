---
id: procedure.mechanic.dnd2024.hit-points
category: ruleset.dnd2024.core.data.hit-points
name: Record Hit Points
governs: mechanic.dnd2024.hit-points.write; dnd2024.hit-points
status: active
---

## Description

Records or corrects one bounded current/maximum Hit Point pair atomically.

## Instructions

Accept only `record|correct` plus complete bounded current and maximum values; fix the source reference and add or replace exactly this component.

## Constraints

This is neither damage nor healing; it accepts no delta, type, temporary HP, source, or effect.

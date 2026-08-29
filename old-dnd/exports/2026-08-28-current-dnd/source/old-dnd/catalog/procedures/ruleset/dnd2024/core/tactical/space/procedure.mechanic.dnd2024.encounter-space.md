---
id: procedure.mechanic.dnd2024.encounter-space
category: ruleset.dnd2024.core.tactical.space
name: Govern D&D 2024 encounter space, placement, and base reach
governs: dnd2024.encounter-space, dnd2024.encounter-position, dnd2024.melee-reach, and Feature 20 Slice 2 mechanics
status: active
---

Defines a bounded five-foot encounter grid, Size-derived creature placements, and effect-free base
reach evidence. Encounter containment remains the roster. This procedure never moves a creature,
spends movement, starts a turn, authorizes an attack, or decides sight/cover.

## Instructions

1. Keep `dnd2024.encounter-space` closed to grid dimensions, sparse blocked and difficult cells,
   and its fixed `Playing the Game > Playing on a Grid` source reference. Grid cells are five feet;
   the writer validates canonical terrain lists, bounds, uniqueness, and non-overlap.
2. Keep `dnd2024.encounter-position` closed to one encounter id, a 2.5-foot anchor, and its fixed
   Creature Size source reference. Absence means unplaced, never an implied origin; Size and the
   encounter space derive its footprint.
3. `mechanic.dnd2024.encounter-space.write`, `mechanic.dnd2024.encounter-position.write`, and
   `mechanic.dnd2024.melee-reach.write` own their respective administrative records.
   `mechanic.dnd2024.encounter-space.read` and
   `mechanic.dnd2024.encounter-participant-tactical-state.read` are effect-free diagnostics;
   placement admission consumes the latter through one declared child per roster participant and
   rejects missing, duplicate, foreign, malformed, or invalid diagnostics before any effect. The
   writer validates the complete map, subject footprint, blocked cells, and other placed
   participant footprints. Base reach is evidence only and never authorizes an attack.
4. This slice changes no roster membership, movement allowance, action, attack result, terrain
   consequence, sight, cover, or clock. Later tactical movement and attack owners consume these
   records under their own contracts.

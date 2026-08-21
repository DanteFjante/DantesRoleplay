---
id: procedure.mechanic.dnd2024.speed
category: ruleset.dnd2024.core.data.speed
name: Govern D&D 2024 creature Speed
governs: commit(kind: "component") declaring dnd2024.speed; commit(kind: "mechanic") authoring mechanic.dnd2024.speed.write or mechanic.dnd2024.speed.read; commit(kind: "action") recording, correcting, or reading creature Speed
status: active
---

## Description

Owns an explicit creature's persistent D&D 2024 base speeds. It supplies the authoritative walk
Speed to the Feature 11/12 turn lifecycle and leaves tactical position, path movement, terrain,
travel pace, and temporary changes to their separate owners.

## Instructions

1. Explanation (SRD 5.2.1, Rules Glossary > Speed; CC-BY-4.0, https://www.dndbeyond.com/srd). A
   creature has one or more speeds and chooses an available Speed when it moves.
2. Keep at most one closed `dnd2024.speed` component on a creature. It contains positive walk
   Speed, zero-or-positive burrow/climb/fly/swim speeds, and the fixed source reference.
3. `mechanic.dnd2024.speed.write` accepts exactly `record` or `correct` and the five speeds. It
   fixes provenance, requires absence for record and complete valid existing state for correction,
   and proposes exactly one component effect.
4. `mechanic.dnd2024.speed.read` accepts `{}` and returns structured present/valid diagnostics for
   composition. It never writes or treats an absent Speed as a default.
5. Feature 11's start/advance lifecycle reads the newly active participant's valid Speed and sets
   only `dnd2024.turn-budget.movementRemainingFeet` to that participant's walk Speed. Turn budget
   owns remaining movement; this component never stores it.

## Constraints

- Every speed is an integer multiple of five from 0 through 1,000 feet, except walk Speed is from
  5 through 1,000. Zero special Speed means absent, not an unlimited or default movement mode.
- The component stores no action budget, movement cost, encounter, position, reach, route,
  distance, travel pace, terrain, condition, derived temporary modifier, or history.
- Missing, malformed, or invalid Speed is unknown and makes a normal movement-budget refresh or
  movement spend fail unchanged. Do not infer 30 feet from a creature name, Size, fixture, or
  existing remaining movement.
- Later class/species/item/condition owners may change Speed only through a reviewed revision of
  this contract; this slice creates no temporary-effect or special-movement policy.

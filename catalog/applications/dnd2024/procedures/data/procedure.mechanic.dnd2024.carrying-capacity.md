---
id: procedure.mechanic.dnd2024.carrying-capacity
category: ruleset.dnd2024.core.data.carrying-capacity
name: Derive D&D 2024 carrying capacities
governs: mechanic.dnd2024.carrying-capacity.read
status: active
---

## Description

Derives SRD carry and drag/lift/push capacities from Strength and explicit Size and compares burden.

## Instructions

Compose the existing burden reader, preserve the SRD pounds formula, convert it using the exact
canonical pounds-to-kilograms factor, and return the read-only comparison.

## Constraints

Tiny carries Strength × 7.5 lb; Small/Medium ×15; each larger category doubles. Drag/lift/push is
twice carry. No Size/default burden is assumed and no encumbrance or movement state is written.
Canonical results are exact kilogram measures; no rounded display value becomes state.

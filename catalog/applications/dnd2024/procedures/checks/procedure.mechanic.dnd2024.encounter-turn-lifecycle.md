---
id: procedure.mechanic.dnd2024.encounter-turn-lifecycle
category: ruleset.dnd2024.core.combat.turns
name: Start, advance, and end encounter turns
governs: mechanic.dnd2024.encounter-turn.start; mechanic.dnd2024.encounter-turn.advance; mechanic.dnd2024.encounter-turn.end; dnd2024.encounter-turn-state
status: active
---

## Description

Owns encounter turn/round lifecycle state after an immutable Initiative snapshot exists.

## Instructions

1. Start creates active round 1/index 0. Advance moves one index or wraps to index 0 and the next round. End explicitly replaces active status with ended.
2. Derive the active participant solely from the validated Initiative snapshot at `turnIndex`; containment remains the roster.
3. Start and advance accept exactly `{}` and atomically restore only the newly active participant's
   turn budget: Boolean resources become available and movement becomes walk Speed minus five feet
   per Exhaustion level, clamped at zero. End changes only lifecycle state.

## Constraints

- Never re-roll or modify Initiative, store a duplicate active participant, or write any participant
  other than the one budget restoration declared for start/advance.
- Roster/order drift, corrupt state, and inactive/ended state fail without effects.
- Spending, position/movement execution, victory detection, reset, and restart remain out of scope.

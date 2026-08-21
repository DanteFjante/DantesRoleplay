---
id: procedure.mechanic.dnd2024.encounter-turn-lifecycle
category: ruleset.dnd2024.core.combat.turns
name: Start, advance, and end an encounter turn lifecycle
governs: commit(kind: "component") declaring dnd2024.encounter-turn-state; commit(kind: "mechanic") authoring mechanic.dnd2024.encounter-turn.start, mechanic.dnd2024.encounter-turn.advance, and mechanic.dnd2024.encounter-turn.end; commit(kind: "action") starting, advancing, or ending an encounter's turns
status: active
---

## Description

Owns the persistent D&D 2024 encounter turn lifecycle state and its start/advance/end transitions. It
consumes the immutable encounter Initiative-order snapshot; it never owns
the roster, Initiative rolls, participant action economy, conditions, damage, or encounter outcome.

## Instructions

1. Explanation (SRD 5.2.1, Playing the Game > Combat > The Order of Combat; CC-BY-4.0,
   https://www.dndbeyond.com/srd). Participants take turns in Initiative order. When every
   participant has taken a turn, the round ends and the same order continues into the next round.
2. Keep one `dnd2024.encounter-turn-state` component on the encounter only. It contains exactly
   `status`, `round`, `turnIndex`, and the fixed source reference.
3. Derive the active participant only as
   `dnd2024.encounter-initiative-order.order[turnIndex].participantId`. Never store a duplicate
   active id, roster, Initiative count, raw roll, seed, action budget, timestamp, outcome, or end
   reason.
4. `mechanic.dnd2024.encounter-turn.start` accepts exactly `{}` and requires an encounter carrying
   a valid Initiative-order snapshot with direct containment equal to its distinct participant ids.
   It rejects a missing, empty, corrupt, or drifted snapshot before proposing an effect.
5. Starting writes exactly one `component.add` on that encounter with `status: "active"`,
   `round: 1`, `turnIndex: 0`, and the fixed Combat / Order of Combat source reference. It never
   writes a participant.
6. Starting an encounter that already has lifecycle state fails without replacing or correcting it.
7. `mechanic.dnd2024.encounter-turn.advance` accepts exactly `{}` and requires a complete active
   lifecycle state plus the same valid snapshot and equal containment. If the next index is within
   the snapshot, it writes that index and retains the round. Otherwise it writes index 0 and
   increments the round exactly once. It rejects a safe-integer overflow before proposing an effect.
8. Advance replaces the complete lifecycle state with exactly one `component.set`; it never writes
   a participant, an order, or an action budget.
9. `mechanic.dnd2024.encounter-turn.end` accepts exactly `{}` and requires the same complete active
   state, valid snapshot, and equal containment. It preserves the current round, turn index, and
   source reference while replacing only `status` with `ended` in one `component.set` effect.
10. Ending is explicit rather than inferred from Hit Points, sides, or an outcome. An ended encounter
    has no active participant; a second end, later start, or later advance fails without changing it.
    Restart/reset and victory/defeat detection remain outside this contract.
11. Feature 12/20 extend only start and advance: each fans out the effect-free
    `mechanic.dnd2024.turn-budget.read` and `mechanic.dnd2024.speed.read` over the encounter roster.
    Start requires every participant to report a valid budget; start/advance require valid Speed for
    the newly active participant. Each successful transition retains its lifecycle effect and then
    applies one full `component.set` restoring that participant's four availability fields and
    setting remaining movement from its walk Speed. End never restores a budget.

## Constraints

- The Initiative-order snapshot remains the sole ordered participant/count source. Encounter
  containment remains the roster.
- Validate snapshot shape, source reference, unique nonempty participant ids, safe integer counts,
  bounded nonempty order, and exact roster membership before any effect.
- The state uses positive safe-integer rounds and a nonnegative index within the current snapshot.
- The start, advance, and end mechanics use no randomness and declare no event, condition, action
  spending, outcome, or participant write.
- A later lifecycle revision may add an explicit reset policy but must retain this component meaning
  and derive every active participant from the same Initiative snapshot.

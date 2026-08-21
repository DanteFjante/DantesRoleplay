---
id: procedure.mechanic.dnd2024.turn-budget
category: ruleset.dnd2024.core.combat.economy
name: Govern a D&D 2024 turn action economy budget
governs: commit(kind: "component") declaring dnd2024.turn-budget; commit(kind: "mechanic") authoring mechanic.dnd2024.turn-budget.write or mechanic.dnd2024.turn-budget.spend; commit(kind: "action") recording, correcting, or spending a participant's turn budget
status: active
---

## Description

Owns a combat participant's D&D 2024 Action, Bonus Action, Reaction, free-interaction, and movement
allowance state. It owns safe administrative admission and correction plus explicit, normal resource
spending. It does not decide what an action costs.

## Instructions

1. Explanation (SRD 5.2.1, Playing the Game > Actions; Bonus Actions; Reactions; Interacting with
   Objects; Combat > Your Turn; CC-BY-4.0, https://www.dndbeyond.com/srd). A creature normally has
   one Action and movement on its turn, at most one Bonus Action when a feature permits it, one free
   object interaction in combat, and one Reaction until its next turn begins.
2. Keep one closed `dnd2024.turn-budget` component on the participant. It contains exactly four
   availability Booleans, remaining movement feet, and the fixed source reference. Base Speed is
   separate persistent `dnd2024.speed` state.
3. `mechanic.dnd2024.turn-budget.write` accepts exactly a mode plus those five mutable values.
   `record` requires absence and uses `component.add`; `correct` requires valid existing state and
   uses `component.set`. It fixes the source reference and rejects caller-supplied provenance.
4. Remaining movement is an integer from 0 through 1000. Feature 20 owns authoritative base
   Speed; Feature 11 reads valid walk Speed at turn start/advance to refresh only this remaining
   allowance. Feature 14 subtracts five feet per validated Exhaustion level from that restored
   allowance, clamped at zero; it does not store or alter a duplicate movement maximum.
5. Admission/correction changes no encounter state, participant placement, initiative order,
   Action cost, Speed, position, condition, event, or resource history. An absent budget means the
   participant has not been admitted; it never means every resource is available.
6. `mechanic.dnd2024.turn-budget.spend` accepts exactly one resource name, plus a positive
   five-foot multiple only when spending movement. It validates complete budget, active turn state,
   Initiative snapshot, containment roster, and subject membership before proposing exactly one
   complete `component.set` budget effect. Movement spending also requires valid Speed and rejects
   remaining movement above that Speed.
7. Compose mechanic.dnd2024.d20-test.state-effects with subject bound to subject, `inheritInput:
   false`, and static `{}` input. An effective Incapacitated prohibits Action, Bonus Action, and
   Reaction; Grappled, Paralyzed, Petrified, Restrained, Stunned, and Unconscious prohibit movement.
   A matching prohibition fails before ordinary budget exhaustion and proposes no effect. Free
   interaction is not prohibited by this rule.
8. Action, Bonus Action, free interaction, and movement require the subject to be the participant
   derived from `order[turnIndex]`. Reaction requires the same validated encounter membership but
   is exempt from that active-participant equality check, because it can occur on another
   participant's turn. A spent Boolean or excessive movement rejects without an effect.

## Constraints

- The writer validates complete existing state before `correct` and rejects malformed/corrupt state
  rather than repairing it. It does not infer or write base Speed.
- No caller provides a participant id, encounter id, round, turn index, source reference, delta,
  history, derived value, or effects.
- The writer proposes exactly one effect on its subject and uses no randomness.
- Turn refresh belongs to the Feature 11 lifecycle transition. Spending changes no turn state,
  Initiative, position, condition, attack, damage, event, or any entity other than the subject.

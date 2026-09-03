---
id: procedure.mechanic.dnd2024.encounter-initiative-order
category: ruleset.dnd2024.core.combat.initiative.order
name: Record an encounter Initiative order
governs: commit(kind: "component") declaring dnd2024.encounter-initiative-order; commit(kind: "mechanic") authoring the encounter Initiative-order parent; commit(kind: "action") setting an encounter's Initiative order
status: active
createdBy: "llm"
changeNote: "Feature 5 Slice 2: the governing contract for the encounter Initiative-order snapshot and its composition parent. The near-duplicate warning against procedure.mechanic.dnd2024.initiative was reviewed and not followed: that contract owns the individual resolver, which explicitly creates no encounter state, and procedure.mechanic.dnd2024.ruleset requires exactly one component per contract, so merging them would authorize two components from one contract."
---

## Description
Owns the single persistent D&D 2024 encounter Initiative-order snapshot and the parent rule that produces it by composing the individual Initiative resolver once per contained participant. Non-goals: rolling one creature's Initiative, turn economy, rounds, surprise, conditions, correcting or clearing an existing order.

## Matches

## Instructions
1. Explanation (SRD 5.2.1, Playing the Game > Combat > The Order of Combat > Initiative; CC-BY-4.0, https://www.dndbeyond.com/srd). At the start of a combat encounter every participant rolls Initiative: a Dexterity-based D20 Test. Participants act in order from highest count to lowest, and that order holds for the rest of the encounter. If two participants tie, the players involved or the GM decide which of them goes first. This paragraph explains the rule; the executable behaviour is defined below.
2. Model the encounter as an entity that CONTAINS its participants. Containment is the roster: this contract never accepts a participant list as input, because a second list would be a second source of truth about who is in the fight.
3. Attach nothing to participants. The order is one component on the encounter entity and is the only persistent owner of Initiative order and Initiative counts.
4. Derive every Initiative count by composing mechanic.dnd2024.initiative.roll as a declared child, once per contained participant. Never read Dexterity, never roll a D20, and never accept a supplied count, modifier or die result in this rule's input.
5. Supply per-participant circumstances through input.participants, an object keyed by participant entity id whose values are exactly the closed input the individual resolver accepts: {} or {"rollCircumstances":[...]}. The map must name every roster participant and nobody else.
6. Supply input.tieDecisions only when the derived counts actually tie. It is an array of ordered id groups, highest first, one group per tied count, in the same descending order as the tied counts themselves. Each group must list exactly the participants tied at that count, with no repeats.
7. Order the snapshot by descending Initiative count, applying each authorized tie decision within its tied group.
8. Write the snapshot with a component.add effect on the encounter. Re-running against an encounter that already carries the snapshot fails and changes nothing; correction and encounter lifecycle belong to a later contract that does not exist yet.
9. Verify a run by querying the encounter back and reading its order, and by confirming that no participant gained a component.

## Constraints
- The rule must declare exactly one role, the encounter, with includeContents true, and exactly one child declaration bound to mechanic.dnd2024.initiative.roll with subject bound to $item.
- Input is closed to participants and the optional tieDecisions. Any other key is rejected.
- An empty roster is rejected. A count, modifier, raw die, ordering or tie-break supplied as input is rejected.
- Exactly one child Initiative result per roster participant is required; a missing, duplicated, unreadable or non-Initiative child result fails the action and applies no effect.
- tieDecisions are required exactly when counts tie and forbidden otherwise. A group naming an untied participant, repeating a participant, or of the wrong size fails the action.
- The snapshot carries the ordered participant identities, their derived counts, and the SRD source reference. It must not carry ability scores, modifiers, raw dice, rounds, turns, conditions or a duplicate roster.
- The snapshot must not store the replay seed. The seed is a 64-bit value that cannot survive the JavaScript number boundary intact, and the action audit already records it exactly; a lossy copy here would be a second, disagreeing source of truth.
- The rule applies exactly one effect, component.add on the encounter, and never writes to a participant.

---
id: procedure.mechanic.dnd2024.initiative
category: ruleset.dnd2024.core.gameplay.initiative.roll
name: Resolve an individual Initiative roll
governs: commit(kind: "mechanic") authoring the D&D 2024 individual Initiative resolver; commit(kind: "action") resolving one creature's Initiative
status: active
createdBy: "llm"
changeNote: "Feature 5 Slice 1: define the individual fixed-Dexterity Initiative resolver."
---

## Description
Defines the D&D 2024 individual Initiative resolver: it reads validated Dexterity state, applies the established D20 roll-circumstance convention, derives the Initiative count, and returns a seeded effect-free result.

## Instructions
Source and scope
- Use source.dnd2024.srd-5.2.1, "Playing the Game > Combat > The Order of Combat > Initiative" and "Playing the Game > D20 Tests > Advantage/Disadvantage".
- This governs only an individual Initiative roll in scope dnd2024-srd-5.2.1. It does not create combat, rank an encounter, decide a tie, advance a turn, or apply combat consequences.

Input and required state
1. Read dnd2024.abilities on subject. Validate its closed six-score shape and integer score range 1 through 30 before randomness.
2. Input is closed: optional rollCircumstances only. Absent and [] mean normal. Reject ability, dc, skill, proficiency, modifier, initiative/count, roll, total, outcome, source, effect, participant, tie, Surprise boolean, and every other caller field.
3. Validate rollCircumstances exactly as procedure.mechanic.dnd2024.check.ability: unique {kind, source} objects; kind is advantage or disadvantage; source is a nonempty trimmed string. Same-kind entries do not stack; any mixture cancels. Surprise is represented only by a validated disadvantage circumstance whose source is "surprised"; it is not persisted or inferred.

Resolution
4. Derive Dexterity modifier as floor((dex - 10) / 2). Roll one or two d20s with ctx.randomInt(1, 20) using the established normal/Advantage/Disadvantage convention, and select the proper die.
5. Initiative count is selected die plus the Dexterity modifier. Natural 1 and 20 have no special Initiative outcome.
6. Return test, ability, die, rollMode, rolls, roll, rollCircumstances, auditable modifiers, initiative, and source locator. Always return effects: [].

Verification and evolution
- Test score boundaries, circumstances, Surprise-as-context, unequal/tied dice, natural rolls, rejection, missing/corrupt state, replay, routing, zero effects, and exact final actor state.
- Do not persist a modifier, raw roll, Initiative count, circumstance, encounter membership, or order on the subject.
- Persistent arbitrary-roster order is blocked on separately planned safe mechanic composition. Do not bypass that dependency with caller-supplied Initiative values or copied participant statistics.

## Constraints
- There is exactly one reusable individual Initiative resolver; no per-creature, per-circumstance, or generic D20 selector mechanics.
- Validate all input and state before random calls. This rule applies zero effects and must not alter entities or components.
- This contract does not revise the ability-check, saving-throw, or dice owners unless their own persisted invariants are defective.
- The live database is authoritative; no repository runtime payload is allowed.

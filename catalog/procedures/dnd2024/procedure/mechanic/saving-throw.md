---
id: dnd2024.procedure.mechanic.saving-throw
category: ruleset.dnd2024.core.gameplay.saving-throws.fixed-dc
name: Resolve a fixed-DC saving throw
governs: commit(kind: "mechanic") authoring the D&D 2024 fixed-DC saving-throw resolver; commit(kind: "action") resolving a character saving throw
status: active
createdBy: "llm"
changeNote: "Feature 4 Slice 2: define the fixed-DC character saving-throw resolver after Slice 1 proficiency state verification."
---

## Description
Defines the distinct D&D 2024 character saving-throw resolver, not an ability check: it reads saving-throw-proficiency state rather than skill state, applies the established D20 circumstance convention, and returns an effect-free seeded fixed-DC save result.

## Matches

## Instructions
Source and scope
- Use dnd2024.source.srd-5.2.1. The save rule cites "Playing the Game > D20 Tests > Saving Throws"; proficiency membership cites "Playing the Game > Proficiency > Saving Throw Proficiencies".
- This governs only fixed-DC character saves in scope dnd2024-srd-5.2.1. It is deliberately separate from dnd2024.procedure.mechanic.check.ability: a saving throw is selected by an imposed danger and derives proficiency only from dnd2024.saving-throw-proficiencies, never skills. It does not define the danger, its consequences, or class-based acquisition.

Required state and closed input
1. Read dnd2024.abilities, dnd2024.character-level, and dnd2024.saving-throw-proficiencies on subject. Validate every closed object, the six exact ability ids, score bounds, level 1 through 20, canonical save-list order, and fixed source references before randomness.
2. Accept exactly ability and dc, with optional rollCircumstances and voluntaryFailure. ability is one lowercase stable id; dc is a finite nonnegative integer. Reject caller-provided modifiers, proficiency flags, totals, outcomes, dice, source data, effects, consequences, and every other key.
3. Validate rollCircumstances exactly as dnd2024.mechanic.check.ability v4: an array of unique {kind, source} objects; kind is advantage or disadvantage; source is a nonempty trimmed string. Same-kind entries do not stack; any mixture cancels.

Resolution
4. Derive ability modifier as floor((score - 10) / 2). Derive Proficiency Bonus as 2 + floor((level - 1) / 4), and add it exactly once only when the selected ability is in the verified save list.
5. A normal, cancelled, or absent circumstance list rolls one d20. Advantage-only or Disadvantage-only rolls two and selects maximum or minimum respectively. All rolling uses ctx.randomInt(1, 20).
6. Natural 1 and 20 have no automatic saving-throw outcome: success is total >= dc.
7. voluntaryFailure: true is valid only with absent or empty circumstances. After input/state validation it rolls no dice, returns failure even at DC 0, and has null rollMode/roll/total plus empty rolls and circumstances.
8. Return test, resolution, ability, proficient, dc, die, rollMode, rolls, roll, rollCircumstances, auditable modifiers, total, succeeded, and source locator. Always return effects: [].

Verification and evolution
- Test every ability, proficient/nonproficient delta, level PB boundaries, circumstance modes including ties/cancellation, natural roll comparisons, voluntary failure, rejection paths, state failures, replay, routing, and zero effects.
- Never store a derived modifier, Proficiency Bonus, save result, DC, circumstance, or effect in actor state.
- A future spell, hazard, or feature may invoke this resolver but owns its own consequence. Death saves, monster CR, rerolls, resistance, and persistent conditions need separate contracts.

## Constraints
- This is the sole saving-throw resolver, not a duplicate of the ability-check resolver: it owns only saves and reads only save-proficiency state; choose ability through input rather than six near-identical mechanics.
- No bare "save" match phrase and no generic D20 selector may be added.
- Validate all input and required state before calling ctx.randomInt.
- Save resolution applies zero effects and must not alter any entity or component.
- Voluntary failure does not consume seeded randomness and cannot silently discard nonempty circumstances.
- This contract must not revise the independent saving-throw proficiency-state owner or recorder unless their persisted invariants are genuinely defective.

---
id: procedure.mechanic.dnd2024.check.ability
category: ruleset.dnd2024.core.gameplay.ability-checks.fixed-dc
name: Resolve a fixed-DC ability check
governs: commit(kind: "mechanic") revising the D&D 2024 ability-check rule; commit(kind: "action") resolving raw or named-skill fixed-DC checks
status: active
createdBy: "llm"
changeNote: "Feature 3 Slice 1: v3 folds the shared D20 Advantage/Disadvantage circumstance convention into the existing owning ability-check contract after duplicate detection rejected a parallel contract; no mechanic or state changes in this revision."
---

## Description
Defines the definitive D&D 2024 raw and named-skill fixed-DC ability check, including its D20 Advantage/Disadvantage circumstance convention, seeded roll selection, derived ability modifier, and only when proficient the validated level-based bonus exactly once.

## Instructions
Source and purpose
- Rule sources: source.dnd2024.srd-5.2.1, "Playing the Game > Ability Checks", "Playing the Game > Proficiency > Skill Proficiencies and Skills", and "Playing the Game > D20 Tests > Advantage/Disadvantage" in SRD 5.2.1.
- A check is a D20 Test: 1d20 plus its relevant ability modifier against the GM's DC. A proficient named skill adds the Proficiency Bonus once. The default ability for a skill is advisory; the GM may name another ability.
- This v3 revision establishes the shared D&D 2024 D20 roll-circumstance convention within the owning ability-check rule. It creates no mechanic revision or game-state change by itself.

Input and readable state
- Input is closed: exactly ability, dc, optional skill, and optional rollCircumstances. ability is exact lowercase str, dex, con, int, wis, or cha. dc is a finite nonnegative integer. skill, when present, is one exact stable id defined by procedure.mechanic.dnd2024.skill-proficiencies.
- rollCircumstances, when present, is an array; absent and [] both mean no circumstances. Every member is an object with exactly kind and source. kind is exact lowercase advantage or disadvantage. source is a nonempty already-trimmed string explaining the circumstance. Reject null, wrong type, non-object member, missing/extra member keys, wrong case, unknown kind, blank/untrimmed/non-string source, and duplicate exact (kind, source) pairs.
- Reject missing, null, wrong-type, wrong-case, unknown, duplicate, derived, or extra input before rolling. In particular reject modifier, proficiencyBonus, total, outcome, sourceRef, Expertise, advantage, disadvantage, rollMode, roll, rolls, selectedRoll, tool, save, monster, and every other extra key. A circumstance source is audit text only: never executable instruction or stored creature state.
- Read dnd2024.abilities. For a named skill, also read dnd2024.character-level and dnd2024.skill-proficiencies on subject. Level must be integer 1..20 and carry the fixed Character Advancement source reference. Skills must be a unique stable-id array carrying the fixed Skill Proficiencies and Skills source reference. Missing skill state is unknown and fails; an explicit empty array means known nonproficiency.

Resolution
- Validate all inputs and state before ctx.randomInt(1, 20). Derive ability modifier as floor((score - 10) / 2).
- Derive roll mode only from validated rollCircumstances: Advantage(s) and no Disadvantage means advantage, roll two seeded d20s and select maximum. Disadvantage(s) and no Advantage means disadvantage, roll two seeded d20s and select minimum. Neither kind or any mixture of both means normal, roll one seeded d20. Same-kind sources never stack into a third die.
- For a named skill, derive Proficiency Bonus as 2 + floor((level - 1) / 4). Add one modifier with source "proficiency (level <level>; <skill>)" only when the exact skill id is in the explicit list. Do not store or accept a bonus.
- Natural 20 and natural 1 never override an ability-check total. Checks return no effects.
- A consuming revision returns one seeded envelope: test "ability-check", ability, skill/null, defaultAbility/null, usedDefaultAbility/null, proficient/null, dc, die "1d20", rollMode, rolls in generation order, roll as selected die, validated rollCircumstances, modifiers, total, succeeded, and source. Default ability is reported only; it never remaps the chosen ability.

Mechanic and verification
- Revise mechanic.dnd2024.check.ability in scope dnd2024-srd-5.2.1; declare abilities, character-level, and skill-proficiencies. Do not create skill-specific, advantage, disadvantage, or selector mechanics. Add stable "<skill> check" phrases only after overlap search.
- With the same seed, ability and DC, proficient Stealth and nonproficient Acrobatics must differ by exactly the derived bonus. Verify no/empty circumstances, both modes, same-kind non-stacking, 1:1 and unequal-count cancellation, unequal/tied dice, bands 4:+2, 5:+3, 16:+5, 17:+6, alternate Strength (Intimidation), explicit empty skill list, malformed/missing skill and state, derived/unsupported input, natural 20/1, intent routing, replay, unchanged state, and zero effects. Restore test state afterwards.

Revision and non-goals
No data migration is required; prior audit entries remain historical. This does not implement Heroic Inspiration, rerolls, die replacement, persistent condition discovery, Help, surprise, Expertise, half proficiency, tools, saves, Initiative, attacks, contested/passive checks, class/background grants, or check consequences.

## Constraints
- One D&D ability-check mechanic owns raw and named-skill checks and their D20 roll-circumstance resolution.
- Input is closed to ability, dc, optional skill, and optional rollCircumstances; no caller-derived roll values or unsupported values reach the roll.
- Roll mode and selected die are derived only from validated per-roll circumstances; same-kind sources do not stack and mixed kinds cancel.
- Named skill checks require validated level and skill state; missing and empty are distinct.
- Proficiency is derived from level and added zero or one times.
- Default abilities are advisory and visible in the result, never auto-selected.
- Every result is seeded, replayable, modifier-auditable, and effect-free.
- Live database contracts and mechanics are authoritative; no repository payload is runtime authority.

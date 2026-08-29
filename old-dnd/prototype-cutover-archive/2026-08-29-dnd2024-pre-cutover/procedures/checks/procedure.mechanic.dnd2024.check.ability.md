---
id: procedure.mechanic.dnd2024.check.ability
category: ruleset.dnd2024.core.gameplay.ability-checks.fixed-dc
name: Resolve a raw fixed-DC ability check
governs: mechanic.dnd2024.check.ability; an action resolving its raw fixed-DC check
status: active
---

## Description

Resolves one D&D 2024 raw or named-skill ability check from authoritative state, a kernel-seeded d20, and a supplied fixed DC.

## Instructions

1. Accept a named ability and integer DC, plus optional exact skill ID and explicit roll circumstances; derive every other value from state and the kernel seed.
2. Resolve Advantage/Disadvantage before rolling: same-kind circumstances use two d20s and select high/low; mixed kinds cancel to one d20.
3. Add `floor((score - 10) / 2)`, and for a proficient named skill add the level-derived Proficiency Bonus once.
4. Report mode, all rolled dice, selected roll, modifier, total, and outcome, but propose no effects, events, or notifications.

Source: `source.dnd2024.srd-5.2.1`, `Playing the Game > D20 Tests > Ability Checks > Ability Modifier/Difficulty Class` (PDF p. 6). The automatic 20/1 text reviewed at `Playing the Game > D20 Tests > Attack Rolls > Rolling 20 or 1` (PDF p. 7) is attack-only and is not applied here.

## Constraints

- `rollCircumstances`, when supplied, is a nonempty list of unique exact `{kind, source}` pairs; `kind` is `advantage` or `disadvantage` and `source` is an explicit trimmed audit label. Persistent/derived conditions are not accepted in this slice.
- A named skill check requires known character-level and skill-proficiency state; an explicit empty skill list is known nonproficiency.
- Natural 20 and natural 1 do not override the total for this raw ability check.
- The result is effect-free and leaves canonical state unchanged.

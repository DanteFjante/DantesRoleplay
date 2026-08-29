---
id: procedure.mechanic.dnd2024.encounter-sides
category: ruleset.dnd2024.core.tactical.sides
name: Govern D&D 2024 encounter sides
governs: dnd2024.encounter-sides, encounter-scoped ally/enemy/neutral relation evidence, and their normal administrative writer
status: active
---

Defines closed encounter-scoped side and hostility facts for direct participants. It is the only
source of ally/enemy/neutral semantics for tactical consumers.

## Instructions

1. Store `dnd2024.encounter-sides` only on an encounter. Its assignments cover every direct
   `participant` exactly once, and its unordered hostility pairs refer only to distinct assigned
   sides. The component derives its fixed SRD source reference.
2. `mechanic.dnd2024.encounter-sides.write` is the only normal record/correct writer. It accepts
   no caller provenance, relation result, faction, Initiative, position, or participant list
   outside the closed assignment payload.
3. `mechanic.dnd2024.encounter-sides.relation` is effect-free. Same side means ally, a declared
   hostile pair means enemy, different undeclared sides mean neutral, and an absent component
   means unknown. Present malformed or roster-stale state is an error, never an inferred value.

## Constraints

- Do not create a participant-side component, a world-faction bridge, or a relationship kind.
- Changing sides changes only the encounter component and never movement, action budget, position,
  condition, attack, damage, or Initiative state.

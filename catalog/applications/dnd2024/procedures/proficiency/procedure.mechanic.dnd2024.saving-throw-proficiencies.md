---
id: procedure.mechanic.dnd2024.saving-throw-proficiencies
category: ruleset.dnd2024.core.data.saving-throw-proficiencies
name: Record saving-throw proficiencies
governs: mechanic.dnd2024.saving-throw-proficiencies.record; authoritative saving-throw proficiency state
status: active
---

## Description

Records a complete known D&D 2024 character saving-throw proficiency list, separately from skills.

## Instructions

1. Accept exactly the `abilities` array, containing unique exact ability IDs from `str`, `dex`, `con`, `int`, `wis`, and `cha`.
2. Canonicalize valid members to ability order; fix the SRD source reference and add or replace only this component.
3. Record membership only. Class grants, level, scores, bonuses, and acquisition history are outside this procedure.

## Constraints

- An empty list is valid and means known no save proficiencies; a missing component remains unknown.
- Reject derived values, source references, duplicate or display-name values, and all undeclared fields.
- This administrative action consumes no randomness and proposes exactly one component effect.

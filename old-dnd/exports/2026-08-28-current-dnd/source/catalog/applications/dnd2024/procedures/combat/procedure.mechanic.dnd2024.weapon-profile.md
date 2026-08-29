---
id: procedure.mechanic.dnd2024.weapon-profile
category: ruleset.dnd2024.core.data.weapon-profile
name: Record canonical weapon profiles
governs: dnd2024.weapon-profile; mechanic.dnd2024.weapon-profile.write
status: active
---

## Description

Owns static Simple/Martial, Melee/Ranged, attack-ability, and base-damage profile state.

## Instructions

Use explicit record/correct with category, kind, canonical unique str/dex abilities, and one bounded
damage expression. The writer fixes SRD Equipment > Weapons provenance and proposes one add/set.

## Constraints

Profiles contain no inventory custody, equipment state, selected ability, proficiency result, attack
roll, damage result, or Hit Point effect. Corrupt existing state cannot be silently corrected.

---
id: procedure.mechanic.dnd2024.weapon-proficiencies
category: ruleset.dnd2024.core.data.weapon-proficiencies
name: Record weapon proficiencies
governs: mechanic.dnd2024.weapon-proficiencies.write; dnd2024.creature.proficiencies
status: active
---

## Description

Records complete known Simple/Martial weapon-category proficiency membership and any
property-qualified Martial membership.

## Instructions

Accept record/correct mode plus a category subset and optional Finesse/Light restricted-Martial
any-of subset. Canonicalize both, reject duplicates and a redundant full-Martial/restricted
combination, fix the SRD source, and write only this component. Successful writes always store an
explicit restriction array; correction may upgrade valid legacy category-only state.

## Constraints

No class, weapon ID, property match, Proficiency Bonus, attack, damage, or caller-authored effect is
accepted. Membership does not itself implement conditional attack behavior.

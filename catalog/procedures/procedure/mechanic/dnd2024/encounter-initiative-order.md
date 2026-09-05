---
id: procedure.mechanic.dnd2024.encounter-initiative-order
category: ruleset.dnd2024.core.combat.initiative.order
name: Record an encounter Initiative order
governs: migration-only reference; no callable capability
status: deprecated
createdBy: "llm"
changeNote: "Feature 5 Slice 2: the governing contract for the encounter Initiative-order snapshot and its composition parent. The near-duplicate warning against procedure.mechanic.dnd2024.initiative was reviewed and not followed: that contract owns the individual resolver, which explicitly creates no encounter state, and procedure.mechanic.dnd2024.ruleset requires exactly one component per contract, so merging them would authorize two components from one contract."
---

## Description

Retained legacy D&D procedure for export, migration, and historical operation references.
This record is not an executable route and must not be selected for new gameplay.

## Matches

## Instructions

Use `orient` and `query(kind: "capabilities")` to discover the current application-scoped
read, direct-action, and planned-interaction contracts. Current D&D rules are owned by
`catalog/applications/dnd2024` and its registered optional extensions.

The former loose action, component, effects, and mechanic commit routes are retired.
Do not translate this retained identity into those calls. The original instructions are
preserved in version control and the pre-release live export, not republished as callable guidance.

Retention owner and deletion conditions are recorded in `catalog/compatibility-retention.json`.

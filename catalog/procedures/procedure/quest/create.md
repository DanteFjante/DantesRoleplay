---
id: procedure.quest.create
category: quest
name: Create a campaign-scoped quest
governs: commit(kind: "system.interaction-execute") creating one closed draft quest
status: active
createdBy: "seed"
changeNote: "Re-seeded: the bootstrap file changed."
---

## Description
Create one draft campaign-scoped quest with three dormant objectives from validated campaign,
arc, chapter, and world references.

## Instructions
1. Use the closed Q1 request only for one active C3 campaign, its active linked arc, and one or
   two active-or-closed linked chapters whose sole chapter-in-arc edge targets that arc.
2. The runner derives every child ID, entity name, component, and link. Entity Name is the
   canonical quest/objective title; component data never duplicates a title.
3. Objective references must be active compatible campaign/world records: campaign-referenced
   motive-bearing actors, contained active locations, world-linked active factions, or world-linked
   active knowledge. Party references cannot expose GM-only material or unrevealed clues.
4. Confirm by reading the returned quest entity. `procedure.quest.modify` owns all later lifecycle changes.

## Constraints
- There is no `quest` commit kind. An application supplies this operation as a mechanic: resolve it with
  `query(kind: "system.interaction-plan")` and run it with
  `commit(kind: "system.interaction-execute")`.
- Creation writes only one draft quest and three dormant objectives, with no lifecycle operation.
- No caller effects, child IDs, component/link data, rewards, world changes, or campaign transitions.

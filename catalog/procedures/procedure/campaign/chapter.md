---
id: procedure.campaign.chapter
category: campaign
name: Maintain campaign chapters and arcs
governs: commit(kind: "system.interaction-execute") continuity operations and the campaign readback that follows one
status: active
createdBy: "seed"
changeNote: "Seeded from bootstrap file."
---

## Description
Campaign continuity has exactly one active chapter and one active arc in this first delivery.
The campaign runner derives all structural effects, validates expected state inside one transaction,
and records its audit with the structural events. The resume view is trusted-host material, not
player authorization.

## Instructions
1. Initialize one active C2 campaign once with a re-ratified chapter and arc seed.
2. Advance or close only the named active chapter with a factual closing summary.
3. Conclude only the named active arc as `resolved` or `abandoned`; this never changes a chapter.
4. Read the campaign root and its active chapter and arc back with `query(kind: "entities")`
   after a successful operation.

## Constraints
- There is no `campaign` commit kind and no `campaign-resume` query kind. An application supplies this operation as a mechanic: resolve it with
  `query(kind: "system.interaction-plan")` and run it with
  `commit(kind: "system.interaction-execute")`.
- Callers never supply effects, child IDs, relationship data, events, audits, or milestones.
- C3 does not create quests, world content, characters, clocks, sessions, or branching arcs.
- A closed chapter does not resolve an arc; a terminal arc is never revived by this procedure.

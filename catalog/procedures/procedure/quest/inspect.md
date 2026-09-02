---
id: procedure.quest.inspect
category: quest
name: Inspect an active campaign-scoped quest
governs: commit(kind: "system.interaction-execute") reading one active quest through its owning application
status: active
createdBy: "seed"
changeNote: "Re-seeded: the bootstrap file changed."
---

## Description
Return a bounded trusted-host summary of one active campaign-scoped quest, including its
objectives, evidence links, and recent status transitions.

## Instructions
1. Supply one active quest id. The reader verifies the complete Q1–Q2 campaign, arc, chapter,
   objective, and dependency context before returning anything.
2. Use the fixed result only to resume the quest: root status and summary, three ordered
   objectives, each objective's bounded evidence links, and up to twelve verified recent component
   status transitions.
3. Treat `visibility` and evidence `audience` as descriptive editorial labels. This trusted-host
   read does not make an authorization decision.
4. If the summary is unavailable, correct the quest or its context and inspect the entity graph;
   do not infer missing state from operation prose or an unbounded event query.

## Constraints
- There is no `quest-summary` query kind. Read the quest and its objectives with
  `query(kind: "entities")` or `query(kind: "graph")` against the owning application state space.
- This is a read-only fixed projection, not a general graph, history, or audience-filter API.
- It returns an active quest only, exactly three display-ordered owned objectives, no more than
  five evidence links per objective, and no more than twelve transition records.
- Evidence returns only target id, role, and audience. It never projects target names, components,
  status, or other target data.
- A malformed present quest graph or campaign context fails closed. A malformed historical event is
  omitted rather than fabricated or used to widen the response.

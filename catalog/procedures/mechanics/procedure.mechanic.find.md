---
id: procedure.mechanic.find
category: mechanics
name: Find and read mechanics
governs: query(kind: "mechanics"), discovering and inspecting stored JavaScript mechanics
status: active
---

## Description
How to discover a reusable mechanic and read the exact version that would be revised or
executed. Search results are summaries; an id lookup returns the stored source and requirements.

## Instructions
1. Search with the words a player would use, not a guessed mechanic id:
   `query(kind: "mechanics", query: "shove him")`. The result is ranked by the existing
   deterministic matcher; a scope-specific rule outranks a shared rule.
2. Use the returned id with `query(kind: "mechanics", id: "...")` before revising a mechanic or
   relying on one. This reads the full current version, including its source, requirements and
   match phrases.
3. Read it before running an action too. An action selects a rule by intent and you cannot name
   one, so knowing which roles the likely rule declares is the only way to supply them.
4. Supply `version` when investigating a historical result. Mechanic versions are append-only,
   so the source that ran in an earlier operation remains readable.
5. Treat status as information: drafts and deprecated mechanics may be inspected, while archived
   mechanics are hidden from the default list unless `includeInactive: true` is requested. Only an
   active mechanic can be selected by an action.
6. If no result matches, clear one filter or search with the words the player used. Do not infer
   that a mechanic does not exist from one overly specific query.

## Constraints
- Do not execute source returned by this query. `commit(kind: "action")` is the only mechanic
  execution path.
- Do not infer role names, component meanings or game vocabulary from the kernel. Read the
  mechanic's declared requirements and source as authored content.
- Do not treat a summary as the full source. Read the id-specific result before revising it.


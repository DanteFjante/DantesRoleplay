---
id: procedure.system.inspect
category: system
name: Inspect the system before changing it
governs: orient(), query(kind: "procedures"), query(kind: "entities"), query(kind: "history"), any diagnosis
status: active
createdBy: "seed"
changeNote: "Re-seeded: the bootstrap file changed."
---

## Description
How to find out what already exists before you add, change or conclude anything.

## Instructions
1. Call `orient()` first. It tells you what this system is, what state it is in, and — importantly
   — what is NOT built yet.
2. Browse the procedure taxonomy with `query(kind: "categories", catalog: "procedures")` when
   the catalog is large. Open the relevant branch with `category: "..."`, then list that branch
   with `query(kind: "procedures", category: "...")`. A category means that node and everything
   below it; use a text query only when you know the words you need. Each contract states what it
   governs; match that against what you are about to do.
3. Read the contracts governing the area you are about to touch, with
   `query(kind: "procedures", id: "...")`. A summary is not a contract.
4. Look at what already exists in that area before assuming something is missing:
   `query(kind: "world")` for the data model, `query(kind: "entities", nameQuery: "...")` for
   things in it, and `query(kind: "categories", catalog: "mechanics")` followed by
   `query(kind: "mechanics", category: "...")` for rules.
5. Read `query(kind: "history")` when diagnosing rather than building. The last operations usually
   explain the state you are looking at. Filter with `tool` or `subject` to narrow it, and
   `failuresOnly: true` when something is not working.

## Constraints
- Never report something as missing until you have listed what exists.
- Never conclude from an empty search result alone; list without a filter before concluding.
- If `orient()` says a capability is not built, believe it. Do not infer from a procedure's
  existence that the thing it describes is reachable yet.


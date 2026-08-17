---
id: procedure.system.inspect
category: system
name: Inspect the system before changing it
governs: orient, find_procedures, get_procedure, history, any diagnosis
status: active
---

## Description
How to find out what already exists before you add, change or conclude anything.

## Instructions
1. Call `orient` first. It tells you what this system is, what state it is in, and — importantly —
   what is NOT built yet.
2. List the procedure contracts with `find_procedures()`. There are few enough that reading the
   list beats searching. Each one states what it governs; match that against what you are about
   to do.
3. Read the contracts governing the area you are about to touch.
4. Look at what already exists in that area before assuming something is missing.
5. Read `history` when diagnosing rather than building. The last operations usually explain the
   state you are looking at. Filter by tool or subject to narrow it.

## Constraints
- Never report something as missing until you have listed what exists.
- Never conclude from an empty search result alone; list without a filter before concluding.
- If `orient` says a capability is not built, believe it. Do not infer from a procedure's
  existence that the thing it describes is reachable yet.

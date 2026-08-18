---
id: procedure.mechanic.write
category: mechanic
name: Write a game rule
governs: write_mechanic, adding or revising a game mechanic, authoring JavaScript
status: active
---

## Description
How to add a rule to the game. This system ships with no game at all: every rule that exists was
written like this, during play, and the first one was written by an agent in your position.

## Instructions
1. Search before writing. `find_mechanics(query: "...")` with the words a player would actually
   use. Two rules answering the same phrase is the failure this system is most prone to, because
   the same action then resolves differently depending on which one ranked first.
2. If something close exists, read it with `find_mechanics(id: "...")` and prefer revising it.
   Revising appends a version and keeps the old source, so it is not a destructive act.
3. Decide what the rule reads, and declare it in `requirements`. Roles are your own names —
   `subject`, `speaker`, whatever fits — and the kernel never interprets them. For each role, list
   the component definitions the rule reads. It will be handed exactly those and nothing else.
4. Declare honestly rather than minimally. The requirements are what a supervisor is shown instead
   of your source, so a rule whose declaration understates what it reads is a rule that misleads.
5. Write the source as a function body over `ctx`. Return
   `{ narration: "...", effects: [...] }`. The effects are the same vocabulary as `apply_effects`.
6. Use `ctx.randomInt(min, max)` for anything chance-based, never `Math.random()`. The seeded
   source is reproducible and recorded; `Math.random()` makes the outcome unexplainable afterwards.
7. `JSON.parse` component data before reading it — components arrive as JSON strings, not objects.
8. `write_mechanic(..., dryRun: true)` and read every check. `components-exist` failing means you
   named a component definition that does not exist, and the rule would otherwise run against empty
   data and quietly behave as though the entity has none.
9. Commit, then **run it** with `run_action(..., dryRun: true)`. A rule that has never been run is
   a guess.

## Constraints
- A mechanic never writes. It returns proposed effects; the system validates and applies them.
  There is no database, no network and no host inside `ctx`, and nothing that can be imported.
- Never widen a rule by having it read a component it did not declare — it cannot, and attempting
  it produces `undefined` rather than an error.
- Ids are permanent. There is no rename and no delete, only status deprecated or archived.
- Do not write one rule per creature, per item or per situation. A rule that reads a component is
  reusable; a rule with a name hard-coded in it is a rule you will write again tomorrow.
- Keep rules small enough to read. The point of this system is that a person can approve what an
  AI wrote, and a hundred-line rule defeats that.

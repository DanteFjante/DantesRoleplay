---
id: procedure.mechanic.write
category: mechanic
name: Write a game rule
governs: commit(kind: "mechanic"), adding or revising a game mechanic, authoring JavaScript
status: active
createdBy: "seed"
changeNote: "Re-seeded: the bootstrap file changed."
---

## Description
How to add a rule to the game. This system ships with almost no game at all: every rule that
exists was written like this, and the first one was written by an agent in your position. MCP
commit is the live route; repository development uses the canonical `.md` and `.js` files under
`catalog/mechanics/`.

## Instructions
1. Search before writing. `query(kind: "mechanics", query: "...")` with the words a player would
   actually use. Two rules answering the same phrase is the failure this system is most prone to,
   because the same action then resolves differently depending on which one ranked first.
2. If something close exists, read it with `query(kind: "mechanics", id: "...")` and prefer
   revising it. Revising appends a version and keeps the old source, so it is not a destructive
   act.
3. Decide what the rule reads, and declare it in `requirements`. Roles are your own names —
   `subject`, `speaker`, whatever fits — and the kernel never interprets them. For each role, list
   the component definitions the rule reads. It will be handed exactly those and nothing else.
4. Declare honestly rather than minimally. The requirements are what a supervisor is shown instead
   of your source, so a rule whose declaration understates what it reads is a rule that misleads.
5. Write the source as a function body over `ctx`. Return `{ narration: "...", effects: [...] }`.
   The effects are the same vocabulary as `commit(kind: "effects")`.
6. Use `ctx.randomInt(min, max)` for anything chance-based, never `Math.random()`. The seeded
   source is reproducible and recorded; `Math.random()` makes the outcome unexplainable afterwards.
7. `JSON.parse` component data before reading it — components arrive as JSON strings, not objects.
8. Write the match phrases as the things a player would SAY, one per line. Selection is by intent
   and nothing else: a rule nobody's words reach is a rule that will never run.
9. In repository mode, edit the `.md` and `.js` pair, run `.\roleplay validate catalog`, and add a
   focused execution test. Do not also commit the source through MCP or query it back from the
   persistent database during ordinary development.
10. In MCP-only mode, `commit(kind: "mechanic", payload: {...}, dryRun: true)` and read every check.
    Commit the identical payload, then **run it** with `commit(kind: "action")` somewhere a real
    change is acceptable. There is no action dry run; proposed effects are validated atomically.
    A rule that has never been executed—by a focused test or a live action—is a guess.

## Constraints
- A mechanic never writes. It returns proposed effects; the system validates and applies them.
  There is no database, no network and no host inside `ctx`, and nothing that can be imported.
- Never widen a rule by having it read a component it did not declare — it cannot, and attempting
  it produces `undefined` rather than an error.
- Ids are permanent. There is no rename and no delete, only status deprecated or archived.
- A rule committed as `draft` cannot be selected by an action. Commit it `active` when it is meant
  to be used, and check `orient()` — it reports how many rules are runnable, not just how many
  exist.
- Do not write one rule per creature, per item or per situation. A rule that reads a component is
  reusable; a rule with a name hard-coded in it is a rule you will write again tomorrow.
- Keep rules small enough to read. The point of this system is that a person can approve what an
  AI wrote, and a hundred-line rule defeats that.


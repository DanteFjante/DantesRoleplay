---
id: procedure.action.run
category: mechanic
name: Resolve what a player is trying to do
governs: commit(kind: "action"), resolving an action, deciding an outcome during play
status: active
---

## Description
The procedure for play itself. A player says what they are trying to do; this turns that into a
rule being run and the world changing.

## Instructions
1. Find the rule before you run anything: `query(kind: "mechanics", query: "what the player said")`
   returns candidates ranked by the same matcher an action uses, and changes nothing.
2. If nothing matches, the rule probably has not been written yet. That is the ordinary state of
   this system, not a fault. See `procedure.mechanic.write`.
3. Read the likely rule in full with `query(kind: "mechanics", id: "...")`. You need its role names
   to call it, and you should know what it does before it does it.
4. Work out which entity fills each role, using `query(kind: "entities", ...)`. You pass them as
   `roleEntityIds: {"<role>": "<entityId>"}` using the rule's own role names — the kernel never
   guesses what a role means.
5. Call `commit(kind: "action", payload: {"intent": "...", "roleEntityIds": {...}, "input": "{}"})`.
   `input` is JSON text whose root must be an object. Omit it or use `{}` when the action has no
   arguments. `null`, arrays, scalars, whitespace-only text, and malformed JSON are rejected
   before any mechanic is selected; a valid object is passed unchanged to `ctx.input`.
   **This runs the rule.** The action selects the best-ranked ACTIVE rule matching your intent —
   you cannot name one, and there is no caller-facing dry run. Use words the rule you read
   actually matches, or a different rule will answer.
6. Read what came back: the narration, the effects applied, the seed, and the rule and version that
   produced them. If the narration is wrong, the rule is wrong — revise the rule rather than
   working around it in the story you tell the player.
7. Pass the returned `seed` back to reproduce a run exactly; omit it to roll afresh.
8. Confirm with `query(kind: "entities", ids: [...])` using the affected ids, report what happened
   using the returned `narration`, and quote the `operationId`.

## Constraints
- Never describe an outcome the system did not produce. If a rule was not run, or its effects were
  rejected, nothing happened — narrating it anyway makes the audit log and the story disagree, and
  the audit log is the one that will still be there next session.
- Never invent a roll, a threshold or a result. If you find yourself deciding the outcome, either
  a rule is missing or you have not run the one that exists.
- Do not use `commit(kind: "effects")` to hand-apply what a rule should decide. Direct effects are
  for setting the world up; rules are for resolving what happens in it.
- An explicit input must be a valid JSON object. Do not rely on invalid input being treated as
  `{}`; correct the caller payload before retrying.
- If the rule fails or is stopped by a limit, say so plainly and revise it. Retrying an unchanged
  rule produces the same failure.
- The whole run is one transaction. A rule that proposes five changes and gets the fourth wrong
  applies none of them — so a partial outcome is never something you need to reason about. A rule
  that proposes no changes at all is a legitimate success: narration is an outcome.
- A rule may use declared child mechanics. They run first under derived replay seeds and are visible
  to the parent only as frozen `ctx.children` data; if any child fails, the parent does not run and
  no effects are applied.


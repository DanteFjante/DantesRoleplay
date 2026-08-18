---
id: procedure.action.run
category: mechanic
name: Resolve what a player is trying to do
governs: run_action, resolving an action, deciding an outcome during play
status: active
---

## Description
The procedure for play itself. A player says what they are trying to do; this turns that into a
rule being run and the world changing.

## Instructions
1. Call `run_action(intent: "what the player said")` with no `mechanicId`. This runs nothing — it
   returns the rules that could apply, so that choosing one is a decision you make rather than one
   the system makes for you.
2. If nothing matches, the rule probably has not been written yet. That is the ordinary state of
   this system, not a fault. See `procedure.mechanic.write`.
3. Read the chosen rule with `find_mechanics(id: "...")`. You need its role names to call it, and
   you should know what it does before it does it.
4. Work out which entity fills each role, using `get_entities`. Pass them as
   `roles: {"<role>": "<entityId>"}` using the rule's own role names.
5. `run_action(..., dryRun: true)` first. You get the narration and the exact effects it proposes,
   with nothing applied.
6. Read what came back. If the narration is wrong, the rule is wrong — revise the rule rather than
   working around it in the story you tell the player.
7. Commit by calling again without `dryRun`. Pass the `seed` from the dry run to get exactly the
   outcome you previewed; omit it to roll afresh.
8. Report what happened using the returned `narration`, and quote the `operationId`.

## Constraints
- Never describe an outcome the system did not produce. If a rule was not run, or its effects were
  rejected, nothing happened — narrating it anyway makes the audit log and the story disagree, and
  the audit log is the one that will still be there next session.
- Never invent a roll, a threshold or a result. If you find yourself deciding the outcome, either
  a rule is missing or you have not run the one that exists.
- Do not use `apply_effects` to hand-apply what a rule should decide. Direct effects are for
  setting the world up; rules are for resolving what happens in it.
- If the rule fails or is stopped by a limit, say so plainly and revise it. Retrying an unchanged
  rule produces the same failure.
- The whole run is one transaction. A rule that proposes five changes and gets the fourth wrong
  applies none of them — so a partial outcome is never something you need to reason about.

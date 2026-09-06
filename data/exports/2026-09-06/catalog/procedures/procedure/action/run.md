---
id: procedure.action.run
category: mechanic
name: Resolve what a player is trying to do
governs: commit(kind: "application.action.execute"), exact application mechanic execution, and adjudicating an attempted action
status: active
createdBy: "seed"
changeNote: "Re-seeded: the bootstrap file changed."
---

## Description
The procedure for play itself. An already selected exact mechanic can run in one call. A player's
ambiguous request is planned first, reviewed, and only then executed.

## Matches

## Instructions
1. Decide whether the request is exact. Exact means you already have the application id, state-space
   id, qualified mechanic id, version, content fingerprint, every role binding, and the complete
   object input. If any of those choices still needs interpretation, use
   `query(kind: "system.interaction-plan")`; do not guess the missing identity or provenance.
2. For an exact request, read the mechanic's current full contract and the required entities. Use
   `query(kind: "system.catalog.record")` for the effective application mechanic and
   `query(kind: "entities", ...)` for role bindings. Copy the returned version and content
   fingerprint exactly.
3. Call `commit(kind: "application.action.execute", payload: ...)` once with exactly
   `idempotencyKey`, `applicationId`, `stateSpaceId`, `qualifiedMechanicId`, `mechanicVersion`,
   `contentFingerprint`, `roleEntityIds`, and object-valued `input`. The kernel never guesses what
   a role means and never reselects a mechanic from prose.
4. Existing confirmation policy applies to this commit. Confirmation is not a payload field and
   this route does not create a second consent model. Equal retries with the same idempotency key
   replay; reuse for a different exact request fails closed.
5. Read the returned affected entity ids, narration, receipt, and structured next actions. A stale
   activation or mechanic fingerprint changes nothing; refresh the named record and deliberately
   decide whether the new version is still the intended action.
6. Confirm with the returned entity-read next action, report only the returned narration, and quote
   the receipt operation id.

## Constraints
- Never describe an outcome the system did not produce. If a rule was not run, or its effects were
  rejected, nothing happened — narrating it anyway makes the audit log and the story disagree, and
  the audit log is the one that will still be there next session.
- Never invent a roll, a threshold or a result. If you find yourself deciding the outcome, either
  a rule is missing or you have not run the one that exists.
- Do not use `commit(kind: "system.world-state.sync")` to hand-apply what a rule should decide.
  Reviewed authoring manifests set the world up; mechanics resolve what happens in it.
- Input must be a JSON object. Do not rely on invalid input being treated as `{}`; correct the
  caller payload before retrying.
- The retired unscoped action selector is not callable. Use this exact route or interaction
  planning; do not attempt a compatibility fallback.
- If the rule fails or is stopped by a limit, say so plainly and revise it. Retrying an unchanged
  request with the same key replays the same recorded result.
- The whole run is one transaction. A rule that proposes five changes and gets the fourth wrong
  applies none of them — so a partial outcome is never something you need to reason about. A rule
  that proposes no changes at all is a legitimate success: narration is an outcome.
- A rule may use declared child mechanics. They run first under derived replay seeds and are visible
  to the parent only as frozen `ctx.children` data; if any child fails, the parent does not run and
  no effects are applied.

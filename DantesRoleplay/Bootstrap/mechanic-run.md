---
id: procedure.mechanic.run
category: mechanics
name: Implement the action runner
governs: IActionRunner.RunAsync, implementing or modifying the action runner inside the kernel
revised-by: Claude Fable 5, 2026-08-17 — corrected the atomicity constraint, which wrongly implied a zero-effect mechanic cannot succeed; Claude Opus 5, 2026-08-18 — scoped governs to the kernel and added the redirect, matching procedure.mechanic.projection
status: active
---

## Description
A kernel-development contract: how the runner turns a free-form action intent into one stored
mechanic execution and one atomic world change, without adding game vocabulary to the kernel. If
you are calling `commit(kind: "action")` to resolve something in play, this is not your contract —
read `procedure.action.run` instead; the runner performs all of this for you.

## Instructions
1. Take the caller's intent and explicit role-to-entity id map as given. Role names belong to the
   mechanic author; do not infer kernel meanings for names such as `actor` or `target`.
2. Search mechanics using the intent and optional scope, then choose the first ranked mechanic
   whose status is `active`. Draft, deprecated and archived mechanics do not run.
3. Load the selected append-only version and generate a seed when the caller did not provide one.
   A supplied seed replays an action exactly.
4. Parse the mechanic's requirements and resolve the declared projection before Jint starts —
   see `procedure.mechanic.projection`. Missing required roles or entities fail the action;
   optional roles may be absent.
5. Run Jint with the host's execution limits and without CLR access. A mechanic returns narration,
   data and proposed effects; it never writes the world.
6. Dry-run the exact effect list, apply that same list through `IEffectApplier`, record the
   mechanic version, seed and projection, and commit the whole action as one transaction.
7. If any step fails, commit nothing. Record the failed operation after rollback with the stable
   error code and a concrete next call — a literal `query(...)` or `commit(...)`, never advice.
8. Record the operation against the public verb (`commit`), not the handler's own name. History is
   read by sessions that only know the three verbs.

## Constraints
- Only `MechanicStatus.Active` mechanics are executable.
- The runner must use `IProjectionResolver`, `IMechanicEngine` and `IEffectApplier`; it must not
  perform direct database writes or expose a store to JavaScript.
- The projection is restricted to declared components, identity and containment context.
- The exact effect list that passed dry-run validation is the list that is applied.
- There is no partial application: a successful action commits the mechanic's entire proposed
  effect list in the same transaction as its audit row. A mechanic that proposes zero effects —
  narration only — is a legitimate success that changes nothing.
- The selected mechanic version, seed and frozen projection remain associated with the operation.
- No game-specific verbs, role meanings, predicates or SQL belong in the action runner.

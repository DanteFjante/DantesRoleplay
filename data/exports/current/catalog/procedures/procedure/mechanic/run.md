---
id: procedure.mechanic.run
category: mechanics
name: Execute an application mechanic
governs: IApplicationActionRunner.ExecuteAsync, exact application mechanic execution inside the kernel
status: active
createdBy: "seed"
changeNote: "Re-seeded: the bootstrap file changed."
---

## Description
A kernel-development contract for executing one exact mechanic from the active application
catalog as one atomic state-space change, without adding game vocabulary to the kernel. Play
callers should read `procedure.action.run`; this procedure governs the internal execution owner.

## Matches

## Instructions
1. Require the exact application, state space, qualified mechanic id, version, content
   fingerprint, role-to-entity map, object input, and idempotency key. Never select a mechanic from
   free-form intent inside the execution owner.
2. Recheck the state-space binding, active application snapshot, authorization, mechanic status,
   exact version, and content fingerprint before projection.
3. Derive a deterministic seed from the accepted execution request and replay the recorded result
   for an equal idempotency key.
4. Parse the mechanic's requirements and resolve the declared projection before Jint starts —
   see `procedure.mechanic.projection`. Missing required roles or entities fail the action;
   optional roles may be absent.
5. Run Jint with the host's execution limits and without CLR access. A mechanic returns narration,
   data and proposed effects; it never writes the world.
6. Dry-run the exact effect list, apply that same list through `IEffectApplier`, record the
   mechanic version, seed and projection, and commit the whole action as one transaction.
7. If any step fails, commit nothing. Record the failed operation after rollback with the stable
   error code and a concrete next call — a literal `query(...)` or `commit(...)`, never advice.
8. Record the application, state space, exact mechanic provenance, idempotency disposition, and
   affected entities in the application execution receipt.

## Constraints
- Only an active mechanic from the exact current application catalog snapshot is executable.
- The runner must use `IProjectionResolver`, `IMechanicEngine` and `IEffectApplier`; it must not
  perform direct database writes or expose a store to JavaScript.
- The projection is restricted to declared components, identity and containment context.
- The exact effect list that passed dry-run validation is the list that is applied.
- There is no partial application: a successful action commits the mechanic's entire proposed
  effect list in the same transaction as its audit row. A mechanic that proposes zero effects —
  narration only — is a legitimate success that changes nothing.
- The selected qualified mechanic id, version, content fingerprint, seed, and frozen projection
  remain associated with the operation.
- No game-specific verbs, role meanings, predicates or SQL belong in the action runner.

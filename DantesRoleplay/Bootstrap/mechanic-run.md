---
id: procedure.mechanic.run
category: mechanics
name: Run a mechanic action
governs: run_action, selecting and executing a stored JavaScript mechanic
status: active
revised-by: Claude Fable 5, 2026-08-17 — corrected the atomicity constraint, which wrongly implied a zero-effect mechanic cannot succeed
---

## Description
How to turn a free-form action intent into one stored mechanic execution and one atomic world
change. This is the automated path for actions; it does not add game vocabulary to the kernel.

## Instructions
1. Supply the player's intent and an explicit role-to-entity id map. Role names belong to the
   mechanic author; do not infer kernel meanings for names such as `actor` or `target`.
2. The runner searches mechanics using the intent and optional scope, then chooses the first
   ranked mechanic whose status is `active`. Draft, deprecated and archived mechanics do not run.
3. The runner loads the selected append-only version and generates a seed when the caller did not
   provide one. Supply a seed when replaying an action.
4. The runner parses the mechanic's requirements and resolves the declared projection before Jint
   starts. Missing required roles or entities fail the action; optional roles may be absent.
5. Jint runs with the host's execution limits and without CLR access. A mechanic returns narration,
   data and proposed effects; it never writes the world.
6. The runner dry-runs the exact effect list, applies that same list through `IEffectApplier`,
   records the mechanic version, seed and projection, and commits the whole action as one
   transaction.
7. If any step fails, nothing is committed. The failed operation is recorded after rollback with
   the stable error code and a concrete next call.
8. After success, call `get_entities(ids: [...])` using the returned affected ids to confirm the
   resulting world state, and quote the returned action `operationId` when reporting the outcome.

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

---
id: procedure.mechanic.projection
category: mechanics
name: Materialise a mechanic projection
governs: IProjectionResolver.ResolveAsync, implementing or modifying the projection layer inside the kernel
status: active
revised-by: Claude Fable 5, 2026-08-17 — scoped governs to the kernel and added the redirect, so action callers stop retrieving instructions they cannot follow; Claude Opus 5, 2026-08-18 — repointed the redirect at the caller-facing contract
---

## Description
A kernel-development contract: how the projection layer turns a mechanic's declared requirements
and explicit role assignments into the frozen, minimal world data handed to the JavaScript
engine. If you are calling `commit(kind: "action")` over MCP, this is not your contract — read
`procedure.action.run` instead; the runner performs all of this for you.

## Instructions
1. Parse the stored `requirements` with `MechanicRequirements.Parse`. The stored declaration is the
   authority for what the mechanic may see.
2. Take role-to-entity ids from the action caller as given. Role names are author-defined; do not
   invent kernel meanings for names such as `actor` or `target`.
3. Resolve with `IProjectionResolver.ResolveAsync`, passing the action input and a recorded seed.
4. Treat every reported problem as a failed action. A projection with missing required data must
   never be handed to the mechanic as though the data were empty.
5. Hand the resulting `MechanicProjection` to Jint. The mechanic receives only the components it
   declared, plus identity and containment context; it does not query the store.
6. Keep the projection and seed associated with the mechanic version in the action result and
   audit record so the run can be explained or replayed.

## Constraints
- Optional roles may be absent; required roles may not.
- Extra role assignments are ignored because the requirements, not the caller, control visibility.
- Do not add predicates, SQL, game-specific role names, or lazy store access to the projection layer.
- A projection is read-only input. World changes still go through the effect applier.

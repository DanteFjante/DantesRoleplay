---
id: procedure.mechanic.projection
category: mechanics
name: Materialise a mechanic projection
governs: IProjectionResolver.ResolveAsync, implementing or modifying the projection layer inside the kernel
status: active
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
3. Resolve with `IProjectionResolver.ResolveAsync`, passing a valid JSON-object action input and a
   recorded seed. Preserve valid object text unchanged; reject malformed or non-object input
   rather than normalising it to `{}`.
4. Treat every reported problem as a failed action. A projection with missing required data must
   never be handed to the mechanic as though the data were empty.
5. Hand the resulting `MechanicProjection` to Jint. The mechanic receives only the components it
   declared, plus identity and containment context; it does not query the store.
6. When `requirements.children` declares child mechanics, use `IMechanicComposer` before running
   the parent source. Bind child roles only from declared parent roles (or `$item` while iterating
   an `includeContents` role). A child may inherit the parent input, use a static object, or select
   a named parent-input object (including a per-`$item` object); it must never receive unrelated
   parent metadata by accident. Derive deterministic child seeds, and give the parent only the
   serialised, frozen `ctx.children` results, including the declared role identities used for each
   child invocation. Never expose a CLR callback, store, or async host object to JavaScript.
7. Keep the enriched projection and every seed associated with the mechanic version in the action
   result and audit record so the run can be explained or replayed.

## Constraints
- Optional roles may be absent; required roles may not.
- Extra role assignments are ignored because the requirements, not the caller, control visibility.
- Do not add predicates, SQL, game-specific role names, or lazy store access to the projection layer.
- A projection is read-only input. World changes still go through the effect applier.
- A child run proposes effects but never applies them. Only the top-level parent action can return
  effects for the effect applier.
- Composition is bounded: no more than eight nested levels and 100 contained children per declared
  fan-out. A child failure fails the whole parent action before parent source or effects run.


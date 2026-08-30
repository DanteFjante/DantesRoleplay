---
id: procedure.mechanic.projection
category: mechanics
name: Materialise a mechanic projection
governs: IProjectionResolver.ResolveAsync, implementing or modifying the projection layer inside the kernel
status: active
createdBy: "seed"
changeNote: "Re-seeded: the bootstrap file changed."
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
   declared, identity and containment context, plus contents or relationships a role explicitly
   requests with `includeContents` or `includeRelationships`; it does not query the store.
6. When `requirements.children` declares child mechanics, use `IMechanicComposer` before running
   the parent source. Bind child roles only from declared parent roles (or `$item` while iterating
   an `includeContents` role). A child may inherit the parent input, use a static object, select a
   named parent-input object (including a per-`$item` object), or use the complete object-valued
   `data` result of one declared non-foreach sibling through
   `inputFromChildData: { resultKey: "<sibling key>" }`. The last form is exclusive of every
   other input source and foreach declaration; validate the sibling graph before execution, run
   dependencies first with lexical result-key tiebreaks, and deep-copy only that `data` object.
   It must never receive unrelated parent metadata by accident. Derive deterministic child seeds,
   and give the parent only the serialised, frozen `ctx.children` results, including the declared
   role identities used for each child invocation. Never expose a CLR callback, store, or async
   host object to JavaScript.
7. Keep the enriched projection and every seed associated with the mechanic version in the action
   result and audit record so the run can be explained or replayed.

## Constraints
- Optional roles may be absent; required roles may not.
- Extra role assignments are ignored because the requirements, not the caller, control visibility.
- Do not add predicates, SQL, game-specific role names, or lazy store access to the projection layer.
- A projection is read-only input. World changes still go through the effect applier.
- `includeRelationships` defaults to false. An opted-in role receives a frozen, canonically ordered
  list of incoming and outgoing relationship records touching that role, with only from id, to id,
  kind, and raw object data. It never grants the other endpoint's components or world traversal.
- `includeContents` defaults to false. Its default opted-in view is the existing direct children
  with id, name, and slot only. `contentsDepth` may request one through four containment levels;
  `contentComponentIds` is the separate, bounded allow-list of components visible on those nodes.
  New bounded-content declarations fail before the mechanic runs if a role would exceed 100 contained
  nodes or reaches corrupt cyclic containment. The legacy direct identity-only request remains
  compatible. The resolver never truncates a declared view or grants ancestry, relationships,
  root components, or undeclared child components.
- A child run proposes effects, declared events, and notifications but never applies them. The
  composer carries those proposals upward in depth-first child execution order; the top-level
  parent action appends its own output and alone dry-runs, validates, applies, audits, and commits
  the one combined proposal. No earlier child proposal is visible to a later child as state or
  input, and any child or parent failure rolls the entire root action back.
- A dependent child may consume neither sibling effects, events, notifications, narration, logs,
  roles, nor a JSON path. Missing, malformed, scalar, array, or null producer data fails the whole
  composition before the dependent child or parent source runs.
- Composition is bounded: no more than eight nested levels and 100 contained children per declared
  fan-out. A child failure fails the whole parent action before parent source or effects run.

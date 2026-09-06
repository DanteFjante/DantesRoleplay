---
id: procedure.world.model
category: world
name: Model an application component type
governs: commit(kind: "system.component-type.register"), representing a new application concept as typed data
status: active
createdBy: "seed"
changeNote: "Re-seeded: the bootstrap file changed."
---

## Description
How to introduce a typed component owned by one registered application without changing the
kernel database schema or writing to the retired generic component store.

## Matches

## Instructions
1. Search the effective application catalog first. Prefer an existing type or a new field on its
   next reviewed schema version over a duplicate concept.
2. Author the component descriptor and JSON Schema in the owning catalog. Game-specific names and
   validation belong there, not in C#.
3. Use `system.component-type.register` only at an explicit synchronization boundary. Supply the
   exact application id, owner-qualified type id, raw schema, request token, and expected schema
   hash. Use a null expected hash only when the type is genuinely absent.
4. Preview first, read all checks, then submit the identical request. A changed application or
   schema fingerprint makes the commit stale and requires a new preview.
5. Attach data through a reviewed application mechanic or `system.world-state.sync`; both validate
   the value against the registered schema and preserve application/state-space provenance.

## Constraints
- Component identities and schema meaning are permanent governed contracts; do not repurpose one.
- Component data is a JSON object and must validate against the exact registered schema version.
- No game concept justifies a kernel table or game-specific C# branch.
- Registration does not mutate existing state-space data or activate authored catalog content.

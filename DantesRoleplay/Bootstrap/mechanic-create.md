---
id: procedure.mechanic.create
category: mechanics
name: Implement mechanic storage
governs: IMechanicStore.WriteAsync, IMechanicStore.CheckAsync, implementing or modifying mechanic storage inside the kernel
revised-by: Claude Opus 5, 2026-08-18 — scoped governs to the kernel and added the redirect. It previously governed the same call as procedure.mechanic.write, so a session matching contracts by governs found two answers and no way to choose
status: active
---

## Description
A kernel-development contract: what the mechanic store guarantees when a rule is written or
revised. If you are authoring a rule through `commit(kind: "mechanic")`, this is not your contract
— read `procedure.mechanic.write` instead.

## Instructions
1. Keep writes append-only. An id is permanent; writing an existing id adds a version and never
   overwrites source, because an operation recorded last week ran against the old one.
2. Report checks by name rather than as a single verdict. A caller has to be able to see WHAT was
   validated: id format, whether this creates a mechanic or a version, whether the requirements
   parse, whether the component definitions they name exist, and whether something near-identical
   already exists.
3. Keep near-duplicate detection a WARNING, not a block. It is the anti-sprawl guard (§P12) and it
   is crude on purpose; a blunt check that fires honestly is worth more than a tuned one nobody
   believes.
4. Default a new mechanic to `draft`, and keep an existing mechanic's status when the caller omits
   it. A revision that silently activated a draft would put unreviewed JavaScript into play.
5. Store the source as text. Nothing in the store executes it; the action runner owns projection,
   sandbox execution and effect application.
6. Store the content fingerprint that was computed from the authored fields, and compare against
   the STORED one when reseeding. Re-deriving a hash from round-tripped content is how a seeder
   starts appending an identical version on every restart.

## Constraints
- Requirements JSON, referenced component definitions, source and match phrases must pass their
  checks before a write commits.
- Never overwrite or delete a mechanic version. Retirement is `status: deprecated` or
  `status: archived`.
- Never put game-specific verbs, role meanings or direct world writes into the kernel. Mechanics
  propose structural effects; the sandbox and effect applier enforce the execution boundary.
- Do not bypass `IMechanicStore`, write arbitrary SQL, or execute JavaScript during authoring.

---
id: procedure.mechanic.create
category: mechanics
name: Implement mechanic storage
governs: IMechanicStore.WriteAsync, IMechanicStore.CheckAsync, implementing or modifying mechanic storage inside the kernel
status: active
createdBy: "seed"
changeNote: "Re-seeded: the bootstrap file changed."
---

## Description
A kernel-development contract: what the mechanic store guarantees when a rule is written or
revised. If you are authoring a rule through `system.mechanic-sandbox.draft`, this is not your contract
— read `procedure.mechanic.write` instead.

## Matches

## Instructions
1. Keep writes append-only. An id is permanent; writing an existing id adds a version and never
   overwrites source, because an operation recorded last week ran against the old one.
2. Report checks by name rather than as a single verdict. A caller has to be able to see WHAT was
   validated: id format, whether this creates a mechanic or a version, whether the requirements
   parse, whether the component definitions they name exist, and whether something near-identical
   already exists.
3. Keep the single-record near-duplicate check advisory. It is an early authoring nudge, not the
   activation authority. Whole-overlay application preview separately blocks deterministic
   conflicts such as identical match phrases, overlapping declared effect ownership, equivalent
   child graphs, and incompatible namespace claims. Token similarity creates review candidates
   only and must never establish equivalence by itself.
4. Default a new mechanic to `draft`, and keep an existing mechanic's status when the caller omits
   it. A revision that silently activated a draft would put unreviewed JavaScript into play.
5. Store the source as text. Nothing in the store executes it; the action runner owns projection,
   sandbox execution and effect application.
6. Store the content fingerprint that was computed from the authored fields, and compare against
   the STORED one when reseeding. Re-deriving a hash from round-tripped content is how a seeder
   starts appending an identical version on every restart.
7. A coexistence decision must name both exact mechanic fingerprints. Editing either mechanic
   expires that decision. Only a trusted `distinct-responsibility` or `intentional-override`
   disposition permits two conflicting active mechanics to coexist; `merge` and `replacement`
   remain blocking until the catalog completes the decision.

## Constraints
- Requirements JSON, referenced component definitions, source and match phrases must pass their
  checks before a write commits.
- Never overwrite or delete a mechanic version. Retirement is `status: deprecated` or
  `status: archived`.
- Never put game-specific verbs, role meanings or direct world writes into the kernel. Mechanics
  propose structural effects; the sandbox and effect applier enforce the execution boundary.
- Do not bypass `IMechanicStore`, write arbitrary SQL, or execute JavaScript during authoring.
- Draft mechanics may overlap without blocking activation, but their findings remain visible for
  review before either draft becomes active.

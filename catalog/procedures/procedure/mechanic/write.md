---
id: procedure.mechanic.write
category: mechanic
name: Draft a governed application mechanic
governs: system.mechanic-sandbox.draft, isolated candidate requirements, JavaScript, match phrases, and tests
status: active
createdBy: "seed"
changeNote: "Re-seeded: the bootstrap file changed."
---

## Description
How an AI prepares an inert mechanic candidate without writing a permanent catalog identity or
activating code. Repository development still authors reviewed `.md` and `.js` pairs under the
owning application catalog.

## Matches

## Instructions
1. Search active mechanics and unresolved anti-sprawl candidates before drafting. Prefer an
   existing responsibility when it already owns the intended effects.
2. Start from a reviewed mechanic opportunity. Declare roles, closed object input, exact child
   dependencies, effect allowlist, ownership, match phrases, and captured scenarios.
3. Invoke `system.mechanic-sandbox.draft`. The candidate runs only inside the bounded Jint sandbox
   with no filesystem, database, network, or CLR access.
4. Read schema, catalog, replay, effect-preview, quota, expiry, and anti-sprawl evidence. Correct
   the draft and rerun its focused scenarios until all deterministic checks pass.
5. Promotion records authorized export-review approval only. It assigns no permanent id, writes no
   catalog file, changes no schema, runs no migration, and activates nothing.
6. At an explicit synchronization boundary, export the approved candidate, choose its permanent
   identity during human review, add focused catalog tests, validate the catalog, and activate the
   reviewed application snapshot through the normal catalog path.

## Constraints
- Drafts stay application-scoped in SQLite and expire under quota.
- Match phrases, requirements, child fingerprints, and declared effects must be complete enough
  for anti-sprawl and replay checks.
- Unresolved deterministic overlap prevents activation; fuzzy similarity requests review only.
- JavaScript proposes typed effects and narration. It never writes state directly.
- No sandbox operation may assign a permanent identity or bypass explicit promotion and catalog
  activation.

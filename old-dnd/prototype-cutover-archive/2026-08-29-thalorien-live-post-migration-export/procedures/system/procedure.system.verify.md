---
id: procedure.system.verify
category: system
name: Verify a change before handing it over
governs: finishing any change to this application's code
status: active
createdBy: "seed"
changeNote: "Re-seeded: the bootstrap file changed."
---

## Description
What “done” means for a repository change. Verification is risk-based while iterating and complete
at feature acceptance; repeating the largest gate after every file adds cost without adding new
evidence.

## Instructions
1. Add or update a focused test that would fail without the changed behavior. Run the smallest
   relevant build and tests while iterating so failures remain attributable.
2. After catalog changes, run `.\roleplay validate catalog`. This parses every catalog format,
   imports a disposable copy into a freshly migrated database, applies production authoring
   checks and verifies a clean round trip without touching the persistent database.
3. Run guard tests when changing the kernel or MCP surface. They enforce invariants reviewers do
   not reliably catch: game vocabulary leaking into C#, an advertised kind with no dispatcher, a
   recovery call naming a retired verb and the three-tool budget.
4. Run the protocol walk only when changing the MCP surface, serialization, service registration
   or a dependency reached through MCP. It proves behavior that unit construction cannot, including
   dependency registration and callable recovery instructions.
5. At completion of a coherent feature or before release, build the whole solution and run the
   full test suite once. A focused pass is iteration evidence; the complete pass is acceptance
   evidence.
6. Import into the persistent database only when integration play or release needs the feature.
   Inspect the import dry run, apply it and verify catalog/database agreement. Do not require this
   live synchronization for ordinary file edits.
7. Report what was verified and what was not. Keep durable evidence concise: named commands,
   counts and failures are enough. Operation ids belong to live writes, not repository changes.

## Constraints
- Never report a behavior as working from syntax, compilation or catalog parsing alone. Execute a
  focused behavioral test for the changed outcome.
- Never take unit tests as evidence that a changed MCP path is callable; use the protocol walk when
  that boundary changed.
- Never run a smaller gate merely because the larger required acceptance gate currently fails.
  Record and resolve the failure or report the feature incomplete.
- Never assert a fact about code or state that was not inspected or exercised.
- Never change a test merely to make it green unless the previous expectation is demonstrably
  wrong and the intended behavior is recorded.
- Validation may replace repeated manual confirmation only when it asserts the same invariant.


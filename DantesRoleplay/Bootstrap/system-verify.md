---
id: procedure.system.verify
category: system
name: Verify a change before handing it over
governs: finishing any change to this application's code
revised-by: Claude Opus 5, 2026-08-18 — added the protocol walk, after a change that passed every unit test left the whole commit verb unusable
status: active
---

## Description
What "done" means for a change to this application, and why compiling is not it.

## Instructions
1. Build the whole solution, not just the project you touched. A change to the core project
   breaks its dependents silently until they are built.
2. Run the full test suite. Not the tests you wrote — all of them. Most regressions here land in
   a neighbouring subsystem, not the one being edited.
3. Run the guard tests specifically if you touched the MCP surface or the kernel. They enforce
   invariants no reviewer reliably catches by eye: game vocabulary leaking into C#, a kind the
   catalog advertises that nothing dispatches, a recovery call naming a verb that no longer
   exists, the three-tool budget.
4. Run the protocol walk if you touched anything the MCP surface reaches. It is the only test that
   speaks JSON-RPC to a real server, and it is the only one that can see a dependency that is not
   registered, a response that is unreadable, or a `fix` that cannot be called.
5. Add a test that would have failed before your change. If you cannot write one, you probably
   cannot describe the change precisely enough to be finished.
6. Hand over one reviewable unit at a time — roughly five files or one subsystem — and stop.
   A large batch means several defects surface at once with no way to attribute them.
7. Say plainly what was verified and what was not. "Builds" and "tested" are different claims,
   and so are "tested" and "run against a live client".

## Constraints
- Never report a change as working on the strength of a syntax or type check alone. Those catch
  structure, never behaviour. A real instance: a change that compiled cleanly shipped seven
  query-translation failures and a hash function missing a field, and none of them were
  detectable without executing the tests.
- Never take a passing unit suite as evidence that the surface works. A second real instance: the
  action runner was never registered in the container, so every `commit` failed at invocation
  with no envelope and no audit row — and all 167 tests passed, because each one constructed its
  dependencies by hand.
- Never assert a fact about the code that you have not observed. Read the file or run the check.
- When a patch is applied by search-and-replace, assert on the specific transformation. Asserting
  that some substring "is present" is not verification when it was already present before.
- A failing test is evidence, not an obstacle. Never adjust a test so that it passes unless the
  test itself is demonstrably wrong.

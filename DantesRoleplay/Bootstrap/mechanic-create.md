---
id: procedure.mechanic.create
category: mechanics
name: Create or revise a mechanic
governs: write_mechanic, authoring or revising stored JavaScript mechanics
status: active
---

## Description
How to add a new JavaScript mechanic or append a revision to an existing mechanic without
creating an unreviewable or silently duplicated rule.

## Instructions
1. Search with `find_mechanics` first. If an existing mechanic already answers the intent, revise
   or reuse it rather than creating a second rule for the same words.
2. Read the full existing mechanic with `find_mechanics(id: "...")` before revising it. The id is
   permanent; a revision appends a version and never overwrites old source.
3. Declare every role and component the source may read in `requirements`. The declaration is the
   projection authority used before Jint runs; do not rely on undeclared or lazy world reads.
4. Provide match phrases that a player might actually use, and keep the description short enough
   for a candidate list.
5. Call `write_mechanic(..., dryRun: true)` first and read every named check. Hard failures must
   be corrected before committing. A near-duplicate is a warning: explain why a distinct mechanic
   is necessary or revise the existing one.
6. New mechanics default to `draft`. Request `status: "active"` explicitly only when the source,
   requirements and match phrases are ready for `run_action`; revising an existing mechanic keeps
   its current status when status is omitted.
7. Commit only through `write_mechanic`. The source is stored as text and is never executed by
   this tool; `run_action` owns projection, sandbox execution and effect application.

## Constraints
- Requirements JSON, referenced component definitions, source and match phrases must pass their
  checks before a write commits.
- Never overwrite or delete a mechanic version. Use a new version, or deprecate/archive it through
  a future lifecycle operation.
- Never put game-specific verbs, role meanings or direct world writes into the kernel. Mechanics
  propose structural effects; the sandbox and effect applier enforce the execution boundary.
- Do not bypass `IMechanicStore`, write arbitrary SQL, or execute JavaScript during authoring.

## Example
```text
write_mechanic(
  id: "mechanic.check.ability",
  category: "check",
  name: "Ability check",
  matches: "try an attribute\nmake a check",
  requirements: "{\"roles\":{\"subject\":{\"components\":[\"stats\"]}}}",
  source: "return { narration: 'Checked.', effects: [] };",
  status: "active",
  dryRun: true
)
```

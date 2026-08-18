# Terra implementation handoff

This file is the execution checklist for continuing the D&D ruleset with a lower-cost model while
preserving the same quality bar. It supplements, and never replaces,
`procedure.system.create-feature` v4 and the current feature plan.

For planning a future feature, first follow
`ruleset/dnd2024/TERRA-FEATURE-PLANNING-GUIDE.md`. That guide governs planning passes; this handoff
governs implementation passes. Never plan and implement a new feature in the same pass.

## Current assignment

Feature 4 is complete. Its live artifacts include the v2 shared proficiency-state contract,
dnd2024.saving-throw-proficiencies, mechanic.dnd2024.saving-throw-proficiencies.record,
procedure.mechanic.dnd2024.saving-throw v1, and mechanic.dnd2024.saving-throw v2; read the
query-backed evidence in the Feature 4 plan before relying on them.

Feature 5 Slice 0 is verified: the shared action/projection transport now rejects non-object and
malformed JSON instead of converting it to `{}`. The next work is Feature 5 Slice 1 only: re-read
the Initiative plan and live contracts, then reactivate and fully verify the draft Initiative
resolver. Persistent encounter ordering remains separately blocked on
engine-level mechanic composition.

## Read in this order

1. Query `procedure.system.create-feature` from the live database.
2. Read `ruleset/dnd2024/TERRA-FEATURE-PLANNING-GUIDE.md` when creating or revising a plan.
3. Read `ruleset/dnd2024/ROADMAP.md`.
4. Read only the current feature's complete dependency plan.
5. Query every live dependency and governing procedure named by the selected slice.
6. Read `STATUS.md` only for repository/kernel context; live MCP query results are authoritative
   for runtime game artifacts.

Do not use old JSON payload files under Feature 1 as runtime authority. They are historical
repository material. Current game contracts, components, entities, mechanics, and action history
live in the database.

## One-pass operating contract

At the start of a pass, identify exactly one lowest unimplemented slice. State its dependencies
and exit gate. If any dependency lacks concrete live or repository evidence, descend to it and
revise the plan before writing.

During the pass:

1. Search before creating; revise an owning artifact instead of making a parallel rule.
2. Retrieve the current live contract immediately before its governed write.
3. Dry-run procedure, mechanic, and effects writes whenever supported.
4. Commit the identical dry-run payload. A changed payload requires a new dry run.
5. Query every committed artifact back before using it.
6. Exercise mechanics through real seeded actions selected by intent.
7. Parse and compare structured result data, modifier lists, dice, effects, and selected mechanic.
8. Test invalid input, missing/corrupt state, boundaries, replay, and final state.
9. Restore test state through its normal recording mechanic. Use disposable fixtures only for
   impossible states, and delete them through validated effects.
10. Run the full repository suite and `git diff --check`.
11. Add operation IDs and objective evidence to the plan, mark only that slice complete, and stop.

## MCP payload facts that prevent repeat mistakes

- The tools are `orient`, `query`, and `commit`; do not invent another tool.
- Commit payloads are JSON strings.
- A mechanic's `matches`, `requirements`, and `source` fields are strings;
  `requirements` itself contains encoded JSON.
- Stored mechanic source is executed directly. It must be a source body ending in `return {...}`;
  wrapping it in `function run(ctx)` makes the action return nothing.
- An action's `input` is encoded JSON text; `roleEntityIds` is a role-to-entity object.
- A failed mechanic action can be the expected result of a negative test. Assert its exact reason
  and verify it changed no state.
- The current generic action error may misleadingly say the rule is broken for valid input
  rejection. Judge the mechanic's specific `why` text and state/effect evidence.

## Definition of done

A slice is done only if the live artifact exists at the intended version, its actual behavior
meets every exit-gate assertion, all temporary state is removed or restored, repository checks
pass, and the plan records reproducible evidence. Validation success alone is not implementation;
`ok: true` alone is not a behavioral assertion; plausible narration alone is not evidence.

If time or token budget ends before the gate is met, leave the slice pending and record the exact
last verified point. Never promote a partial matrix to complete.

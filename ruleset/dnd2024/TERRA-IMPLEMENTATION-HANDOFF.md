# Terra implementation handoff

This file is the execution checklist for continuing the D&D ruleset with a lower-cost model while
preserving the same quality bar. It supplements, and never replaces,
`procedure.system.create-feature` and the current feature plan.

For planning a future feature, first follow
`ruleset/dnd2024/TERRA-FEATURE-PLANNING-GUIDE.md`. That guide governs planning passes; this handoff
governs implementation passes. Never plan and implement a new feature in the same pass.

## Current assignment

Features 5–8 are complete through their file-first catalog import gates. Feature 8 provides the
effect-free weapon-attack resolver against final Armor Class, including derived category
proficiency and natural-20/1 classification; it deliberately does not roll damage or change Hit
Points. Read completed feature plans only as prior evidence.

Feature 9 is complete through catalog import: its effect-free confirmed-hit weapon-damage resolver
and its declared-child transactional target Hit Point parent are verified. Damage never changes
target maximum/source, and Feature 9 adds no zero-Hit-Point consequence.

Feature 10 is complete at `ruleset/dnd2024/feature-10/FEATURE-10-DEPENDENCY-PLAN.md`: its
catalog-owned hero, training-target, and encounter fixtures import cleanly, and its two-database
seeded replay proves the Feature 1–9 vertical session is deterministic.

E1 Slice 1 is complete: the event-type registry, migration, catalog round trip, event-type MCP
kinds, contract, and nine reserved structural schemas are installed and verified.

E1 Slice 2 is complete: the versioned guard/reaction middleware registry, mechanic event
declarations, append-only migration, file-first catalog round trip, subscription contracts,
`query(kind: "subscriptions")`, and `commit(kind: "subscription")` are installed and verified.
The shared database and catalog agree; the full suite passed 308/308.

E1 Slice 3 is complete: structural effect batches create transaction-local proposals, matching
guards run deterministically before commit, denials return `EVENT_BLOCKED` and roll the complete
root back, and dry runs execute the same guard path in a rollback-only transaction. The failed
root audit retains structured proposal and guard-decision evidence. The shared database and
catalog agree; the full suite passed 311/311.

No subsequent event implementation is authorized automatically. The sole next candidate is
`EVENTS_AND_SUBSCRIPTIONS_PLAN.md` Slice 4: transactional structural event ledger. It may be
started only after review and explicit authorization. Slice 3 adds no accepted-event ledger,
reaction routing, notifications, retries, wildcard types, or event mutation.

For later slices, the non-negotiable guard model is middleware around proposed events: effects are
applied only inside the root transaction, immutable proposals are built from receipts, matching
guards run by order then ID against final uncommitted state, and every guard must explicitly allow.
The first deny returns `EVENT_BLOCKED`, records exact subscription/mechanic/version/seed/proposal
evidence in the failed audit, and rolls back the whole root. Guards are read-only and may never
rewrite proposals or return effects, events, or notifications. Slice 3 implements this pipeline;
Slice 4 persists accepted events; Slice 5 runs reaction chains; Slice 6 adds notifications.

## Read in this order

1. Query `procedure.system.create-feature` from the live database.
2. Read `ruleset/dnd2024/TERRA-FEATURE-PLANNING-GUIDE.md` when creating or revising a plan.
3. Read `ruleset/dnd2024/ROADMAP.md`.
4. Read only the current feature's complete dependency plan (`EVENTS_AND_SUBSCRIPTIONS_PLAN.md`
   for the current assignment).
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

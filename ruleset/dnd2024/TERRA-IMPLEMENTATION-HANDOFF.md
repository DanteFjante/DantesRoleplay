# Terra implementation handoff

This file is the execution checklist for continuing the D&D ruleset with a lower-cost model while
preserving the same quality bar. It supplements, and never replaces,
`procedure.system.create-feature` and the current feature plan.

For planning a future feature, first follow
`ruleset/dnd2024/TERRA-FEATURE-PLANNING-GUIDE.md`. That guide governs planning passes; this handoff
governs implementation passes. Never plan and implement a new feature in the same pass.

## Current assignment

Features 1–10 are verified. Feature 10's fresh-import, two-database replay proves the existing
check, save, Initiative, attack, and damage vertical session is deterministic. E1 events and
subscriptions is also complete through its six verified slices; it is a dependency for later
conditions and reactions, not work that remains in this assignment.

**Feature 28 Slice 1 is verified.** Its evidence is in
`ruleset/dnd2024/feature-28/FEATURE-28-SLICE-1-RECEIPT.md`. Do not begin origin grants,
backgrounds, feats, class features, item state, or character creation until that boundary is
the next lowest prerequisite is selected.

**Feature 23 is accepted.** Its evidence is in
`ruleset/dnd2024/feature-23/FEATURE-23-SLICE-11-RECEIPT.md`. It provides physical items,
containment, quantity, carrying, equipment state, currency, fixed item activities, and a bounded
inventory read model.

**Feature 37 is planned but blocked.** Its plan is
`ruleset/dnd2024/feature-37/FEATURE-37-DEPENDENCY-PLAN.md`. Do not implement D&D travel pace
until the official source is registered, a generic on-foot route-distance owner, Feature 33 rests,
and Feature 32 duration lifecycle are each confirmed. Feature 20 Slice 1 supplies base Speed.
Core world travel/time is already
the owner of routes, itineraries, movement, and the root clock.

**Feature 36 Slice 1 experience state and eligibility is implemented.**
Its plan is `ruleset/dnd2024/feature-36/FEATURE-36-DEPENDENCY-PLAN.md`, with campaign authority
in `campaign/feature-14/CAMPAIGN-FEATURE-14-ADVANCEMENT-AUTHORIZATION-PLAN.md`. Do not store an
eligibility flag, copied XP total, or campaign policy on a character; C14 owns authorization and
CH9 owns the level-up transaction. Slice 1 records only a closed XP total and reads a derived,
effect-free exact-next-level result. C15 now provides campaign-bound active-character scope; do
not begin the campaign award bridge until C14 semantic confirmation and the CH9 consume seam are
accepted.

Before implementation, resolve the catalog/database drift currently reported by
`roleplay verify catalog`. It is unrelated live/catalog work; export or reconcile it deliberately,
then establish a clean verification baseline. Never use `--force-files` to overwrite it.

**Feature 18 concentration is planned, not assigned for implementation.** Its complete dependency
plan is `ruleset/dnd2024/feature-18/FEATURE-18-DEPENDENCY-PLAN.md`. It found two upstream
ownership gaps: a Feature 32 persistent-effect identity/ending protocol, and a confirmed platform
path for an event reaction to reuse an effect-free child with closed event-payload input. Do not
implement concentration by duplicating the Constitution-save algorithm or by accepting an
unvalidated caller-supplied effect id or DC.

**Feature 19 reactions in play is planned, not assigned for implementation.** Its complete
dependency plan is `ruleset/dnd2024/feature-19/FEATURE-19-DEPENDENCY-PLAN.md`. Opportunity
attacks need Feature 20 to own spatial position, reach, movement classification, and an atomic
pre-departure per-reactor trigger, Feature 21 obstacle geometry, and Feature 34 for its “can see”
condition. They also need a confirmed platform route for reactions to bind the event's dynamic
reactor/mover into the existing turn-budget and weapon-attack owners. Do not fake those facts with
caller-supplied coordinates, targets, or an inline attack roll.

**Feature 21 cover and ranged combat is planned.** Its complete dependency plan is
`ruleset/dnd2024/feature-21/FEATURE-21-DEPENDENCY-PLAN.md`. Its next and only authorised
implementation candidate is the Feature-7-owned static range-data migration for ranged weapon
profiles. Do not add cover to permanent Armor Class, invent a target-side “firing into melee”
penalty, or accept a caller-supplied cover/range/sight result.

**Feature 20 Slice 1 is verified.** Its evidence is in
`ruleset/dnd2024/feature-20/FEATURE-20-SLICE-1-RECEIPT.md`. The next candidate is Slice 2 only
after its map/position/reach IDs are confirmed. Do not implement a path move by directly mutating
the budget: a confirmed derived-cost-to-budget-spender composition contract is required.

**Feature 22 unarmed, improvised, and two-weapon combat is planned.** Its complete dependency
plan is `ruleset/dnd2024/feature-22/FEATURE-22-DEPENDENCY-PLAN.md`. Its next and only authorised
implementation candidate is the effect-free Unarmed Strike Damage resolver. Do not make a fictional
unarmed weapon profile or use a caller-supplied position, hand count, Grapple source, Shove
destination, improvised damage profile, or Attack-action history. Full Grapple, Shove push,
improvised weapon, and Light two-weapon behavior remains blocked on the named Feature 20, 15, 25,
capacity, and ledger seams.

## Read in this order

1. Query `procedure.system.create-feature` from the live database.
2. Read `ruleset/dnd2024/TERRA-FEATURE-PLANNING-GUIDE.md` when creating or revising a plan.
3. Read `ruleset/dnd2024/ROADMAP.md`.
4. Read only the current feature's complete dependency plan
   (`ruleset/dnd2024/feature-11/FEATURE-11-DEPENDENCY-PLAN.md` for the current assignment).
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

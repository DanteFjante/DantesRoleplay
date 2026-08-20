# Subsystem implementation handoff

Status: **Template — no active assignment; do not implement from this file as written**  
Last updated: 2026-08-20

## How to use this file

Copy this template for one implementation pass, fill every required field, change status to
Active, and name exactly one subsystem slice. If any required field remains unknown, use a
high-capability planning model to complete the plan instead of asking an implementation model to
guess.

The implementation model reads only the bounded document set named here plus live artifacts
required by governing procedures. It does not reopen the whole product architecture unless an
escalation condition fires.

## Assignment identity

- Assignment ID: REQUIRED
- Status: Draft or Active
- Subsystem: REQUIRED
- Owning plan: REQUIRED
- Exact slice: REQUIRED
- Target model profile: High-planning, standard-implementation, or small-mechanical
- Requested outcome: REQUIRED one sentence
- Explicitly excluded work: REQUIRED
- Stop point: REQUIRED
- Reviewer/authority: REQUIRED

## Why this model is appropriate

State why the chosen model can safely perform this assignment.

For a small-mechanical model, all boxes must be true:

- [ ] Ownership is already ratified.
- [ ] Exact artifact IDs and allowed files are listed.
- [ ] No migration, new public API, new effect, new event vocabulary, or architecture decision is
      required.
- [ ] Input/state semantics are closed and fully specified.
- [ ] Expected acceptance results are exact.
- [ ] Cleanup/restoration is explicit.
- [ ] Escalation does not prevent the rest of the repository remaining valid.

If any box is false, use a stronger planning/implementation model.

## Authority and required reads

Read in this order:

1. Live procedure.system.create-feature or the governing system contract.
2. Exact owning subsystem plan and slice.
3. GAME_SYSTEM_MASTER_PLAN.md sections relevant to ownership/integration.
4. Exact prerequisite procedure/component/mechanic/event records listed below.
5. Relevant source record/official locator for ruleset content.
6. Current STATUS.md/receipt evidence named below.
7. Allowed implementation files only after the live inventory is complete.

Required live reads:

| Artifact/query | Expected ID/version/state | Why required | Evidence to record |
| --- | --- | --- | --- |
| REQUIRED | REQUIRED | REQUIRED | Operation/query ID |

Required repository reads:

| File | Required section | Why |
| --- | --- | --- |
| REQUIRED | REQUIRED | REQUIRED |

## Verified prerequisite baseline

| Dependency | Implemented evidence | Expected behavior/state |
| --- | --- | --- |
| REQUIRED | Live ID/version plus operation/test evidence | REQUIRED |

If a dependency does not match this table, stop. Do not repair it as part of this assignment unless
the handoff explicitly owns that repair.

## Allowed changes

Allowed artifact IDs:

- REQUIRED

Allowed files/directories:

- REQUIRED

Allowed database/catalog operations:

- REQUIRED

Forbidden changes:

- unrelated user work or dirty files;
- artifacts outside the allowed list;
- additional subsystem slices;
- opportunistic refactors;
- new packages, tools, APIs, schemas, migrations, or public command kinds unless explicitly listed;
- changing tests to weaken an expected behavior;
- generating bulk content;
- marking later slices complete.

## Closed data and behavior contract

### Authoritative input/state

- REQUIRED

### Derived values that callers may not supply

- REQUIRED or explicitly none

### Missing, null, and empty semantics

- Missing: REQUIRED
- Null: REQUIRED
- Empty: REQUIRED

### Canonical ordering and ID rules

- REQUIRED

### State transitions or algorithm

Write exact branch order and formulas without embedding final runtime source:

1. REQUIRED

### Result and effects

- Exact result fields: REQUIRED
- Exact effect count/types: REQUIRED
- Affected entities/components: REQUIRED
- Event/notification behavior: REQUIRED
- Transaction/rollback behavior: REQUIRED

### Error contract

| Condition | Stable code | Domain reason | Recovery call/action |
| --- | --- | --- | --- |
| REQUIRED | REQUIRED | REQUIRED | REQUIRED |

## Implementation sequence

Perform these steps in order and stop if one fails:

1. Orient and read every governing contract.
2. Query exact dependencies and representative baseline state.
3. Search IDs, official terms, synonyms, and neighboring intent phrases for ownership overlap.
4. Record baseline operation/query IDs and exact state bytes/revisions where required.
5. Modify only allowed artifacts/files.
6. Run supported dry runs and read every named check.
7. Commit/import the identical reviewed content.
8. Query every written artifact back at the expected version/status/scope.
9. Run exact behavioral tests and negative cases.
10. Restore shared actors and delete disposable fixtures through governed operations.
11. Run the specified focused tests, full suite, catalog verification, and diff check.
12. Update only the named receipt/status/handoff evidence.
13. Stop. Do not begin the next slice.

## Acceptance matrix

| Test class | Input/setup | Exact expected result | State/evidence assertion |
| --- | --- | --- | --- |
| Happy path | REQUIRED | REQUIRED | REQUIRED |
| Boundary | REQUIRED | REQUIRED | REQUIRED |
| Differential | REQUIRED or N/A with reason | REQUIRED | REQUIRED |
| Closed invalid input | REQUIRED | REQUIRED error | Exact unchanged state |
| Missing state | REQUIRED | REQUIRED error | Exact unchanged state |
| Corrupt state | REQUIRED | REQUIRED error | Exact unchanged state |
| Determinism/replay | REQUIRED or N/A | REQUIRED | Same result/effects |
| Routing | REQUIRED | Exact selected ID/version | No neighbor captured intent |
| Effects/events | REQUIRED | Exact count/type/order | Correlation/causation IDs |
| Rollback | REQUIRED | Stable failure | No partial state/event/success audit |
| Readback | REQUIRED | Exact version/status/scope | Query evidence |
| Restoration | REQUIRED | Declared baseline | Fixture deleted/restored |
| Repository | Focused/full commands | Expected passing result | Diff/catalog checks |

## Required verification commands

List exact non-destructive commands and expected outcomes:

- Focused tests: REQUIRED
- Full test suite: REQUIRED
- Catalog import/verify: REQUIRED or N/A
- Format/diff check: REQUIRED
- Additional protocol/browser test: REQUIRED or N/A

Do not pin a test count unless the current repository deliberately treats it as an assertion.
Record the actual final count/output in the receipt.

## Evidence and completion report

The completion report must contain:

- changed artifacts/files;
- live IDs/versions/status/scope;
- decisive operation/query IDs;
- exact behavioral results and effect/event counts;
- failure/rollback evidence;
- fixture restoration state;
- focused/full test results;
- catalog/diff verification;
- known limitations that remain inside the owning plan;
- statement that no later slice was started.

Do not include chain-of-thought or dump full model prompts.

## Escalation conditions

Stop without broadening the assignment when:

- an expected dependency/artifact/version is missing or differs;
- another artifact already owns the proposed behavior;
- a new field/status/effect/event/command/migration/API seems necessary;
- expected result or failure semantics are ambiguous;
- catalog and live database disagree;
- a state-changing dry run is unsupported or recovery path is unclear;
- a guard/reaction/workflow introduces an unplanned transaction boundary;
- a required fixture cannot be safely restored/deleted;
- unrelated dirty work overlaps an allowed file;
- focused tests reveal an adjacent-system defect not owned by the slice.

Report the blocker, evidence, and smallest planning decision needed. Do not invent the decision.

## Exit gate

The assignment is complete only when all acceptance rows pass, every artifact is read back,
fixtures are restored, required repository checks pass, evidence is recorded, and no out-of-scope
change was made.

Status after completion: Complete and awaiting review. Never change the handoff to a new slice in
the same implementation pass.


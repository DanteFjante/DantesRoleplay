# Application Kernel Slice 12B implementation — bounded exact ancestry checks

Status: **accepted**
Owner/roadmap: application ECS effect transaction and reviewed World authoring
Dependency tree/leaf: verified G9 live application-ECS authoring; mature-root additive sync repair
Ruleset alignment: **ruleset-neutral**
Source ID and locator: not applicable; no game rule is implemented
Outcome: permit a reviewed World manifest beneath a mature root without snapshotting every unrelated
direct child, while retaining exact in-transaction scope-race rejection.
Exclusions: public MCP shape changes, database migrations, ruleset logic, relaxed root membership,
deletion/removal/rename support, increased manifest/effect limits, and live content changes.
Allowed files/areas: application ECS effect contracts, validation, persistence, focused tests, this
implementation document, one receipt, and the blocked Ganji slice status/validation after proof.
Stop point: exact ancestry edges are bounded and rechecked inside the effect transaction; focused
tests prove mature-root success and stale-path rollback; the catalog validates; the reviewed Ganji
manifest can dry-run and commit unchanged through the existing live operation.

## Confirmed decisions

The user's 2026-08-30 instruction to fix the failed import confirms this ruleset-neutral repair.
The public `system.world-state.sync` manifest remains unchanged. The host may replace the internal
complete-roster concurrency evidence used by this adapter with exact child-parent-slot-revision
evidence for only the ancestry paths whose scope was validated.

## Prerequisite evidence

- `ApplicationWorldAuthoringSynchronizer` currently derives relevant existing endpoints correctly,
  but `BuildContainmentExpectations` expands every ancestor into its complete direct-child roster.
- `ApplicationEcsEffectValidation.MaximumContentsPerExpectation` limits each such roster to 100,
  causing `WORLD_SCOPE_TOO_LARGE` when an otherwise unrelated mature root has more children.
- `IStateSpaceEdgeStore.GetContainmentAsync` already provides the exact child-owned edge needed to
  revalidate one path without enumerating siblings.
- Existing synchronizer tests prove scope rejection, replay, dry-run, rollback, and stale ancestry.

## Runtime artifacts and authoritative state

Add one host-only bounded exact-containment expectation to `ApplicationEcsEffectBatch`. Each record
contains the contained entity ID, expected container ID, slot, and positive revision. The World
synchronizer derives these records from current state for every relevant existing endpoint and each
edge from that endpoint to the selected root. Callers cannot supply them through MCP.

## Behavior, failure, replay, and rollback

Before applying any effect, the existing effect transaction reloads every exact expected child edge
and compares container, slot, and revision. Missing, moved, or revised edges reject with
`REVISION_STALE` and roll back the whole batch. Duplicate or malformed internal expectations fail
shape validation. The count is bounded independently of unrelated sibling count. Existing complete
roster expectations remain supported for callers that require roster equality.

## Implementation sequence

1. Add and validate the host-only exact edge expectation contract.
2. Verify exact edges inside the existing application-ECS transaction.
3. Make World authoring derive exact path expectations rather than complete ancestor rosters.
4. Add focused mature-root, malformed-shape, and stale-path tests.
5. Run focused tests, catalog validation, broader relevant tests, then retry the exact Ganji import.

## Acceptance matrix

| Case | Expected result |
| --- | --- |
| root has more than 100 unrelated children | a small valid additive manifest dry-runs and commits |
| endpoint path moves after scope resolution | every authored effect rolls back as stale |
| expected edge is missing/revised/wrong parent or slot | transaction rejects before effects |
| malformed/duplicate/unbounded exact expectations | batch validation rejects |
| endpoint is outside the selected root | synchronizer still rejects before delegation |
| existing roster-expectation caller | behavior remains unchanged |

## Verification commands

- focused `ApplicationWorldAuthoringSynchronizerTests`
- focused `ApplicationEcsEffectApplierTests`
- `roleplay validate catalog`
- relevant system test projects and `git diff --check`
- exact Ganji manifest dry-run, byte-identical commit, and live readback

## Completion receipt and exit gate

Write `APPLICATION-KERNEL-SLICE-12B-RECEIPT.md`, mark the Ganji narrative slice accepted only after
live readback, and stop before creating Ganji's playable actor or choosing character mechanics.

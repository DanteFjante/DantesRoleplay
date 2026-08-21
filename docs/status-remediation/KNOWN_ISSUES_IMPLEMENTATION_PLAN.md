# Known issues implementation plan

Status: **Implementation-ready; approval of this plan ratifies the permanent ids and semantic
boundaries listed below.**

Prepared: 2026-08-21

## Outcome

Close every open entry in `KNOWN_ISSUES.md`. Four entries need only verified closure because their
reported failures are already corrected in the current clean checkout. The remaining entry is
Feature 20 Slice 5: voluntary tactical movement must derive difficult-terrain cost and the SRD
rules for passing through another creature's space without accepting caller verdicts, duplicating
condition state, or bypassing the Feature 12 movement-budget spender.

This is a repository/catalog implementation plan. Do not write to or reconcile the persistent
game database while executing it. Use disposable catalog imports and repository tests. Do not use
MCP `orient`, live commits, `roleplay import catalog`, or `--force-files` for these slices.

## Verified planning baseline

The following was true in a clean worktree when this plan was prepared:

- `dotnet build DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore` succeeded with zero
  errors. The sole warning was the pre-existing xUnit analyzer warning in
  `KnowledgeAcquisitionCoordinatorTests.cs`; it is outside this issue set.
- `roleplay validate catalog` accepted 380 records: 90 mechanics, 106 procedures, 75 components,
  12 event types, 5 subscriptions, and 92 entities. Its 66 near-duplicate advisories are not open
  issues in `KNOWN_ISSUES.md` and are outside this plan.
- The full repository suite passed: 720 passed, 0 failed, 0 skipped.
- The encounter-space procedure has a complete active `## Instructions` section.
- `DantesRoleplayDbContext.SystemFeedbackReports`,
  `SystemFeedbackReport.RequestToken`, and the non-capturing feedback validation flow exist.
- `KnowledgeTimelineCoordinator.Interval` contains a valid bounded predicate.
- `IKnowledgeVectorIndex` and `SqliteVecKnowledgeVectorIndex` both contain
  `ReplaceWorldAsync` and `MarkOtherGenerationsStaleAsync`.

These observations make closure slices C1-C4 verification/documentation work, not invitations to
rewrite working implementations.

## Fixed decisions

No implementation pass may reopen these decisions without first revising this plan.

### Ownership and reuse

- `dnd2024.encounter-space.difficultCells` remains the only persistent terrain-cost input.
- `dnd2024.creature-size` remains the only Size input.
- `dnd2024.conditions` remains the only stored condition state. Movement consumes effective
  Incapacitated evidence from `mechanic.dnd2024.d20-test.state-effects`; it does not inspect or
  reimplement condition implications.
- `mechanic.dnd2024.turn-budget.spend` remains the only movement-budget authority.
- `mechanic.dnd2024.tactical-move.execute` remains the only player-facing movement root and keeps
  the exact input `{"path":[{"dx":1,"dy":0}]}`.
- Feature 21 owns encounter ally/enemy/neutral semantics. Feature 20 consumes its effect-free
  relation reader and never infers a side from factions, names, Initiative order, or containment
  order.

### New permanent ids

The following ids are ratified as a set:

- `dnd2024.encounter-sides`
- `procedure.mechanic.dnd2024.encounter-sides`
- `mechanic.dnd2024.encounter-sides.write`
- `mechanic.dnd2024.encounter-sides.relation`
- `mechanic.dnd2024.encounter-participant-movement-state.read`

Do not create an additional hostility relationship kind, participant-side component, movement
cost component, terrain component, or condition reader.

### Encounter-side state

`dnd2024.encounter-sides` is attached to the encounter and has exactly this canonical shape:

~~~json
{
  "assignments": [
    {"participantId": "creature.example.hero", "sideId": "side.party"}
  ],
  "hostilePairs": [
    {"firstSideId": "side.opposition", "secondSideId": "side.party"}
  ],
  "sourceRef": {
    "sourceId": "source.dnd2024.srd-5.2.1",
    "locator": "Playing the Game > Movement and Position > Moving around Other Creatures; Making an Attack > Ranged Attacks in Close Combat"
  }
}
~~~

Rules for the state are fixed:

- `assignments` contains every direct `participant` roster member exactly once and no other id,
  sorted by `participantId` using ordinal order.
- A `sideId` is 1-100 lowercase characters, starts with `side.`, and otherwise contains only
  lowercase letters, digits, dots, and hyphens.
- `hostilePairs` contains only distinct assigned sides. Each pair is internally ordered so
  `firstSideId < secondSideId`; the array is unique and sorted by first then second id.
- Same-side participants are `ally`. A listed pair is `enemy`. Different unlisted sides are
  `neutral`. If the component is absent, the reader reports `unknown`. Present malformed or stale
  roster state rejects; it is never repaired or treated as unknown.
- The writer accepts exactly `mode`, `assignments`, and `hostilePairs`; `mode` is `record` or
  `correct`. It derives `sourceRef`, proposes one add/set effect, and rejects duplicate record,
  missing correct state, malformed state/input, or roster mismatch unchanged.
- The training fixture assigns the hero to `side.party`, the training target to
  `side.training-opposition`, and records that pair as hostile.

### Movement formulas

For each accepted five-foot step, derive the mover's entered Size footprint before charging cost.

1. `mapDifficult` is true when that footprint overlaps one or more committed
   `difficultCells`.
2. For every overlapped other participant, passage is allowed when at least one is true:
   relation is `ally`; the other creature is effectively Incapacitated; the other creature is
   Tiny; or the absolute difference between Size ranks is at least 2. Size ranks are exactly
   Tiny 0, Small 1, Medium 2, Large 3, Huge 4, Gargantuan 5.
3. If any overlapped participant fails that admission test, reject the whole path before spending
   movement.
4. `occupiedDifficult` is true when an overlapped participant is neither an ally nor Tiny. This is
   true even when passage is allowed because of Incapacitated or a two-rank Size difference.
5. The step costs 10 feet when `mapDifficult || occupiedDifficult`; otherwise it costs 5 feet.
   Multiple terrain cells, creatures, or causes never raise one step above 10 feet.
6. The final entered footprint may not overlap any other participant, even when passage through
   that participant was allowed on an earlier step.
7. `stepCostsFeet` is an array parallel to `path`, containing only 5 or 10. `feet` equals its exact
   sum and may not exceed 1,000. The budget-input adapter and movement root both revalidate this
   frozen evidence before accepting the child result.

Missing condition state means no effective Incapacitated condition. Corrupt condition state
rejects through the existing condition resolver. Missing side state yields `unknown`, which does
not grant ally passage or the ally terrain exemption. All unrelated movement remains legal when
the side state is absent.

### Non-goals

Do not add pathfinding, Dash, crawling, jumping, squeezing, special Speeds, forced movement,
teleportation, mounts, Opportunity Attacks, Disengage, sight, cover, elevation, Prone-on-forced
co-occupancy, or turn-end co-occupancy handling. Do not alter blocked-cell or diagonal-corner
rules.

## Dependency order

~~~text
C1-C4 stale-entry closure verification (independent of movement work)

M1 encounter-side state and writer
└─ M2 encounter-side relation reader and fixture
   └─ M5 occupied-space movement

M3 effective participant movement-state reader
└─ M5 occupied-space movement

M4 difficult terrain from map cells
└─ M5 occupied-space movement

C1-C4 + M1-M5
└─ A1 complete acceptance and issue-register closure
~~~

Execute one slice per Terra pass. C1-C4 may be done in any order. Execute M1-M5 in numeric order.
Each pass starts from a clean worktree, runs only its focused checks while iterating, runs
`roleplay validate catalog` after catalog changes, records its receipt only after its gate passes,
and stops. Run the full suite only in A1.

## C1 - close the encounter-space procedure blocker

### Boundary

Verify the already completed procedure contract and close only the first open issue. Do not revise
the procedure if the current content and validation remain green.

### Work

1. Confirm
   `catalog/procedures/ruleset/dnd2024/core/tactical/space/procedure.mechanic.dnd2024.encounter-space.md`
   has active front matter, description, `## Instructions`, and `## Constraints`.
2. Run `roleplay validate catalog` and require exit code 0.
3. Run the focused Feature 20 catalog tests:
   `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter FullyQualifiedName~DantesRoleplay.Tests.CatalogFeature20Tests`.
4. Move the corresponding bullet from `KNOWN_ISSUES.md` to its closed history. Record that the
   procedure is complete and the disposable catalog accepts all records. Do not pin warning or
   record counts as a permanent expectation.

### Exit gate

Catalog validation and `CatalogFeature20Tests` pass, the issue is no longer under Open issues, and
the diff contains documentation only.

## C2 - close the System Feedback build blocker

### Boundary

Verify the current model/context/service implementation. Do not refactor working feedback code.

### Work

1. Confirm `DantesRoleplayDbContext.SystemFeedbackReports`,
   `SystemFeedbackReport.RequestToken`, and `SystemFeedbackService.Validated.RequestToken` exist.
2. Confirm `SystemFeedbackService.Validate` assigns its `out` result outside LINQ/lambda capture.
3. Run:
   `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter "FullyQualifiedName~DantesRoleplay.Tests.SystemFeedbackTests|FullyQualifiedName~DantesRoleplay.Tests.SystemFeedbackAdministrationTests|FullyQualifiedName~DantesRoleplay.Tests.SystemFeedbackRetentionTests"`.
4. Run the no-restore test-project build.
5. Move the issue to the closed history with the focused-test and successful-build evidence.

### Exit gate

All three feedback test classes and the shared build pass; only `KNOWN_ISSUES.md` changes.

## C3 - close the knowledge-timeline syntax blocker

### Boundary

Verify the current `Interval` predicate and timeline behavior. Do not change interval semantics.

### Work

1. Confirm `Interval(long from, long? until)` accepts starts 0-1,000,000,000 and accepts an absent
   end or an end strictly greater than the start and no greater than 1,000,000,000.
2. Run
   `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter FullyQualifiedName~DantesRoleplay.Tests.KnowledgeTimelineCoordinatorTests`.
3. Run the no-restore test-project build.
4. Move the issue to the closed history, naming the corrected predicate and focused test.

### Exit gate

The timeline class and shared build pass; only `KNOWN_ISSUES.md` changes.

## C4 - close the vector-index synchronization blocker

### Boundary

Verify interface/implementation parity and retrieval behavior. Do not change the retrieval API.

### Work

1. Compare signatures for `ReplaceWorldAsync` and `MarkOtherGenerationsStaleAsync` in
   `DantesRoleplay/Retrieval/EmbeddingModels.cs` and
   `DantesRoleplay.DataAccess/Retrieval/SqliteVecKnowledgeVectorIndex.cs`.
2. Run the no-restore test-project build.
3. Run:
   `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter "FullyQualifiedName~DantesRoleplay.Tests.KnowledgeHybridSearchCoordinatorTests|FullyQualifiedName~DantesRoleplay.Tests.SqliteVecExtensionProbeTests"`.
4. Move the issue to the closed history with interface parity, build, and focused-test evidence.

### Exit gate

Both method pairs compile, focused retrieval tests pass, and only `KNOWN_ISSUES.md` changes.

## M1 - Feature 21 Slice 2A: encounter-side state and writer

### Boundary

Add the ratified encounter-owned side state and its administrative record/correct path. Stop before
adding a relation reader or changing movement.

### Files

- Add `catalog/components/dnd2024.encounter-sides.json` and `.schema.json`.
- Add
  `catalog/procedures/ruleset/dnd2024/core/tactical/sides/procedure.mechanic.dnd2024.encounter-sides.md`.
- Add
  `catalog/mechanics/ruleset/dnd2024/core/tactical/sides/mechanic.dnd2024.encounter-sides.write.md`
  and `.js`.
- Register all three records in `catalog/manifest.json` in canonical neighboring sections.
- Add `DantesRoleplay.Tests/CatalogFeature21CombatSideTests.cs`.
- Split Feature 21 Slice 2 into 2A state/writer and 2B relation reader in
  `ruleset/dnd2024/feature-21/FEATURE-21-DEPENDENCY-PLAN.md`.

### Implementation

Implement exactly the fixed state, ordering, roster coverage, source, and input rules above. Use
the encounter role with direct contents projected. `record` proposes one `component.add`; `correct`
proposes one `component.set`. Neither mode changes containment, Initiative, position, faction,
condition, or any participant component.

### Acceptance

Test valid record/correct/readback; canonical assignment and hostile-pair ordering; same-side and
unlisted-side data validity; empty hostility; every missing/extra/duplicate/stale participant;
unknown side references; self/duplicate/reversed hostile pairs; invalid side ids; extra fields;
caller source; duplicate record; missing/corrupt correct state; replay; and zero mutation on every
rejection. Assert routing selects only the writer for record/correct side phrases.

### Exit gate

Focused combat-side tests and catalog validation pass. Add
`FEATURE-21-SLICE-2A-COMBAT-SIDE-STATE-RECEIPT.md`, mark only 2A verified, and stop.

## M2 - Feature 21 Slice 2B: side relation reader and fixture

### Boundary

Add one effect-free pair reader and migrate the training encounter to explicit sides. Stop before
movement, cover, sight, or ranged attacks.

### Files

- Add
  `catalog/mechanics/ruleset/dnd2024/core/tactical/sides/mechanic.dnd2024.encounter-sides.relation.md`
  and `.js`; register it in the manifest.
- Revise the encounter-sides procedure to govern relation semantics.
- Add the fixed `dnd2024.encounter-sides` fixture to
  `catalog/world/entities/encounter.dnd2024.feature-10.training.json`.
- Extend `CatalogFeature21CombatSideTests.cs` and update the Feature 21 plan.

### Reader contract

Roles are exactly `encounter`, `first`, and `second`; input is exactly `{}`. Both participant roles
must be direct encounter participants. Output is closed and contains test id, encounter id, both
participant ids, nullable side ids, one relation (`ally`, `enemy`, `neutral`, or `unknown`), and
the fixed source reference. It always has zero effects. Absent state yields unknown; malformed or
roster-stale state rejects.

### Acceptance

Test ally/enemy/neutral in both argument orders; absent-state unknown; same participant; missing
roster member; malformed/stale state; pair ordering; deterministic replay; zero effects; fixture
hero-target enemy result; and routing separation from the writer and world factions.

### Exit gate

Focused side tests and catalog validation pass. Add
`FEATURE-21-SLICE-2B-COMBAT-SIDE-READER-RECEIPT.md`, mark Feature 21 Slice 2 verified, and stop.

## M3 - Feature 20 Slice 5A: effective participant movement state

### Boundary

Create a narrow effect-free composition reader for Size, position, and effective Incapacitated.
Do not change path admission or movement cost yet.

### Files

- Add
  `catalog/mechanics/ruleset/dnd2024/core/tactical/movement/mechanic.dnd2024.encounter-participant-movement-state.read.md`
  and `.js`; register it in the manifest.
- Revise the tactical-move procedure to name this internal reader.
- Add `DantesRoleplay.Tests/CatalogFeature20MovementStateTests.cs`.
- Split Feature 20 Slice 5 into 5A movement state, 5B map terrain cost, and 5C occupied spaces in
  `FEATURE-20-DEPENDENCY-PLAN.md`.

### Reader contract

The only role is `participant`; input is `{}`. Compose exactly one existing
`mechanic.dnd2024.encounter-participant-tactical-state.read` child and one existing
`mechanic.dnd2024.d20-test.state-effects` child, both bound to that participant with empty input.
Validate child ids, role ids, closed outputs, and subject identity. Return the existing Size and
position diagnostics plus `conditionsKnown`, `incapacitated`, and frozen child identity/version/
seed metadata. `incapacitated` is true only when `effectiveConditions` contains the canonical
`incapacitated` id. Return zero effects.

### Acceptance

Test direct Incapacitated; Paralyzed, Petrified, Stunned, and Unconscious implications; known empty
and absent conditions; unrelated conditions; malformed Size/position diagnostics; corrupt
conditions; exact child identities; zero effects; replay; and no writes. Do not duplicate the
condition implication table in the new source or tests.

### Exit gate

Focused movement-state tests and catalog validation pass. Add
`FEATURE-20-SLICE-5A-MOVEMENT-STATE-RECEIPT.md`, mark only 5A verified, and stop.

## M4 - Feature 20 Slice 5B: map difficult-terrain cost

### Boundary

Charge committed difficult terrain while retaining the current reject-all-occupied-footprints
behavior. Stop before creature-space passage.

### Files

- Revise the `.md` and `.js` files for:
  `mechanic.dnd2024.tactical-move.path`,
  `mechanic.dnd2024.tactical-move.budget-input`, and
  `mechanic.dnd2024.tactical-move.execute`.
- Revise the tactical-move procedure and Feature 20 plan.
- Extend `CatalogFeature20TacticalMovementTests.cs` only for changed evidence compatibility.
- Add `DantesRoleplay.Tests/CatalogFeature20DifficultTerrainTests.cs`.

### Implementation

For each entered footprint, set its `stepCostsFeet` entry to 10 when any difficult cell overlaps
and 5 otherwise. A multi-square footprint overlapping several difficult cells still costs 10.
Sum the array into `feet`; reject a sum above 1,000. The adapter and root independently verify
array length, values, sum, path identity, and child budget result. Return `stepCostsFeet` in the
root audit data. Keep every occupied footprint rejected and retain all existing bounds, blocked,
corner, roster, active-turn, and atomicity behavior.

### Acceptance

Test normal/difficult/normal mixed paths; exact 5-versus-10 differential; one large footprint over
multiple difficult cells; diagonal difficult entry; repeated difficult steps; difficult plus
blocked remains blocked; exact and one-short budgets; total above 1,000; malformed frozen evidence
at adapter unit level; same-seed replay; caller `feet`/terrain verdict rejection; and unchanged
budget/position on every failure. Existing normal paths must retain the same state delta.

### Exit gate

Focused movement and difficult-terrain tests plus catalog validation pass. Add
`FEATURE-20-SLICE-5B-DIFFICULT-TERRAIN-RECEIPT.md`, mark only 5B verified, and stop.

## M5 - Feature 20 Slice 5C: occupied-space passage and cost

### Boundary

Consume M2 and M3 from the path validator, apply the fixed creature-space admission/cost formulas,
and keep final occupancy forbidden. This is the only behavior-changing closure slice for the last
open issue.

### Files

- Revise `mechanic.dnd2024.tactical-move.path.md` and `.js` requirements and source.
- Revise the tactical-move procedure and Feature 20 plan.
- Extend existing movement tests and add
  `DantesRoleplay.Tests/CatalogFeature20OccupiedMovementTests.cs`.

### Composition

Replace the path validator's per-roster tactical diagnostic child with
`mechanic.dnd2024.encounter-participant-movement-state.read`. Add a second per-roster child using
`mechanic.dnd2024.encounter-sides.relation`, binding `encounter` and the moving `subject` plus each
direct participant. Require exactly one movement-state and one relation result per roster member,
with matching ids and no duplicates. Ignore the subject's own footprint after validating its two
reports.

### Acceptance

Use normal writers and actions to prove:

- ally passage succeeds and adds no occupied-space difficult cost;
- direct and implied Incapacitated passage succeeds and costs 10 unless the creature is also an
  ally or Tiny;
- Tiny ally/enemy/neutral passage succeeds and costs 5 from occupancy alone;
- a Size-rank difference of exactly 2 and greater succeeds; a difference of 0 or 1 rejects unless
  another admission rule applies;
- neutral and unknown are not ally and grant no passage by themselves;
- map difficult plus occupied difficult still costs 10, never 15 or 20;
- multi-step travel through a Huge/Gargantuan footprint charges each overlapping entered step
  once and exits to an unoccupied final footprint;
- every otherwise admitted creature rejects when the final footprint remains occupied;
- malformed/stale side state, corrupt movement-state evidence, invalid roster, insufficient
  budget, and one late invalid step leave both budget and position byte-identical;
- root input remains closed and contains no target, side, condition, cost, terrain, or destination;
- replay, child ids/versions/seeds, effect count, routing, and atomic rollback remain exact.

### Exit gate

`CatalogFeature20OccupiedMovementTests`, all existing Feature 20 movement tests, the Feature 21
side tests, Feature 13 condition tests, Feature 12 budget tests, and catalog validation pass. Add
`FEATURE-20-SLICE-5C-OCCUPIED-SPACES-RECEIPT.md`, mark Feature 20 Slice 5 verified, and stop.

## A1 - complete acceptance and close the register

### Work

1. Run the tactical compatibility set:
   `CatalogFeature12Tests`, `CatalogFeature13Tests`, `CatalogFeature20Tests`,
   `CatalogFeature20TacticalMeleeTests`, `CatalogFeature20TacticalMovementTests`, all new Feature 20
   Slice 5 classes, and `CatalogFeature21CombatSideTests`.
2. Run `roleplay validate catalog` and require success. Treat advisory count changes as review
   information, not a pinned assertion.
3. Run the full suite once:
   `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore`.
4. Run `git diff --check` and inspect `git status --short`. Do not modify unrelated user changes.
5. Move the tactical-movement bullet from Open issues to the closed history in `KNOWN_ISSUES.md`.
   Summarize the authoritative inputs, exact cost/pass-through behavior, atomic transaction, and
   focused/full verification. If no open bullets remain, retain the `## Open issues` heading with
   the single sentence `No known open issues.`
6. Update `TERRA-IMPLEMENTATION-HANDOFF.md` so Feature 20 Slice 5 and Feature 21 Slice 2 are verified
   and their next boundaries remain Slice 6 and Slice 3 respectively.

### Final exit gate

All five original open entries are in closed history, no new open regression was discovered, the
catalog validates, all 720 baseline tests plus the new tests pass, `git diff --check` is clean, and
no persistent database was changed. Test count may increase and must be reported from the actual
run rather than predeclared.

## Failure and replanning rules

- If C1-C4 no longer pass in the executing checkout, leave that issue open, repair only the named
  owner with a regression test, and rerun the slice gate. Do not carry a speculative rewrite from
  this plan.
- If a new permanent id above already exists with incompatible semantics, stop M1-M3 and revise
  this plan; do not create an alias.
- If E6 cannot compose either per-roster child shape, stop M5 and add the smallest platform slice
  to the plan. Do not accept caller-provided side, condition, occupancy, or cost evidence.
- If the existing condition resolver changes its effective-condition output, revise M3 to consume
  the current closed output. Never duplicate the implication rules.
- A failed movement action is valid negative evidence only when both the budget and position are
  queried afterward and are byte-identical to their pre-action values.
- Do not mark a slice complete from build success alone. Its focused behavior, disposable catalog
  validation, receipt, and parent-plan status must all agree.

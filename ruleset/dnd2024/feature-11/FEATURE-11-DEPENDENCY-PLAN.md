# Feature 11 dependency plan — encounter turn and round lifecycle

Status: **Planned; Slice 1 is the next and only authorized implementation pass**
Last updated: 2026-08-20

## Execution rule

This is a planning-only artifact. It follows the active `procedure.system.create-feature` and
the Terra feature-planning guide: catalog files will be the runtime source, each implementation
pass will complete one lowest slice, dry-run/import/query it, record objective evidence, and stop
for review. This plan creates no procedure, component, mechanic, fixture, or game state.

Before Slice 1, resolve the current catalog/database drift reported by `roleplay verify catalog`.
The verification reported two catalog-only entities and five database-only entities, unrelated to
this feature. Do not use `--force-files` to get around that conflict; export or deliberately
reconcile the live work first, then establish a clean import baseline.

## Target capability

A GM can start a combat encounter that already has a valid Initiative order, advance exactly one
participant at a time through persistent rounds, and explicitly end that encounter so play can
resume from its recorded state without re-rolling Initiative.

### Included

- One encounter-owned lifecycle state derived from the existing immutable Initiative snapshot.
- Starting at round 1 with the first participant in the recorded order.
- Advancing one turn; after the final participant, advancing to round 2 and returning to index 0.
- Explicit GM ending of an active encounter.
- Deterministic state transitions, audit/replay, strict state validation, and roster-drift
  rejection.

### Excluded

- Encounter creation, sides, victory/defeat detection, automatic end conditions, or restarting an
  ended encounter.
- Action, Bonus Action, Reaction, interaction, Move, movement, position, range, or target
  legality (Features 12 and 20–21).
- Conditions, unconsciousness, dying, healing, damage mitigation, and event-triggered
  consequences (Features 13 and 15–19).
- Initiative rerolls, delay, Ready, simultaneous turns, and turns outside combat.
- Persisted active-participant identity, duplicate roster, Initiative counts, raw dice, or
  action-spending data.

## Official source basis

The authoritative source is `source.dnd2024.srd-5.2.1`, *System Reference Document 5.2.1*,
`Playing the Game > Combat > The Order of Combat` (PDF p. 13; official URL in the source entity).
It establishes a cycle of rounds and turns: every participant takes a turn in Initiative order;
when all participants have taken a turn, the round ends; the cycle continues while the fight
continues; and the Initiative order remains the same between rounds. The source does not supply a
machine-readable victory predicate, so ending an encounter is an explicit GM lifecycle action in
this feature, never an inferred consequence.

## Planning inventory and overlap result

| Inquiry | Evidence and conclusion |
| --- | --- |
| Feature workflow | `catalog/procedures/system/procedure.system.create-feature.md` is active and requires a recursive, one-slice, file-first implementation. |
| Initiative order | `procedure.mechanic.dnd2024.encounter-initiative-order` and `mechanic.dnd2024.encounter-initiative-order` own the immutable order snapshot only; both explicitly exclude turns, rounds, and lifecycle. |
| Encounter data | `dnd2024.encounter-initiative-order` stores ordered participant IDs and Initiative counts on the encounter. Encounter containment is the authoritative roster. |
| Existing turn owner | Searches for `turn`, `round`, `active turn`, `advance turn`, `encounter lifecycle`, and `combat state` found no D&D turn component, procedure, or mechanic. |
| Action/runtime boundary | `procedure.mechanic.run` and `procedure.mechanic.projection` require declared projections, JavaScript-proposed effects, and one atomic top-level action; no kernel or MCP-tool change is needed. |
| Integration baseline | Feature 10 provides a catalog-owned two-participant encounter plus hero/target fixtures and fresh-database deterministic action coverage. |
| Selection safety | E2 is implemented: direct player match phrases outrank incidental names/descriptions, covered by `MechanicStoreTests.Player_match_phrases_exclude_rules_that_only_share_generic_description_words`. New Feature 11 phrases still require routing tests and collision review. |
| Live catalog state | `roleplay verify catalog` on 2026-08-20 reported 89 unchanged records but unrelated catalog/database drift. It is an import preflight blocker, not a justification for direct database edits. |

## Verified existing dependencies

| Dependency | Evidence |
| --- | --- |
| Source registry | `source.dnd2024.srd-5.2.1` is catalog-owned with official SRD 5.2.1 version, canonical PDF URL, CC-BY attribution, and heading-plus-page locator format. |
| Encounter roster and Initiative order | Feature 5 catalog regression (`CatalogFeature5Tests`) verifies arbitrary-roster child Initiative resolution, authorized ties, one encounter snapshot, and no participant order component. |
| Atomic action/effects | `procedure.mechanic.run`, `procedure.mechanic.projection`, and the 365/365 repository test baseline verify top-level action atomicity, immutable declared projections, and effect validation. |
| Encounter/participant fixture | Feature 10 fresh-import tests create/replay the training encounter and demonstrate the expected Initiative snapshot before a later action changes target HP. |
| Event infrastructure | E1 is complete, but Feature 11 does not declare, subscribe to, or react to events. Events become a dependency when later features need automatic consequences. |

## Recursive dependency analysis

```text
Feature 11: encounter turn and round lifecycle
├─ SRD combat round/turn order                                    [implemented source basis]
├─ authoritative encounter roster = containment                   [implemented: Feature 5]
├─ immutable ordered Initiative snapshot                           [implemented: Feature 5]
├─ action projection, atomic effects, audit and replay             [implemented kernel]
├─ phrase-aware mechanic selection                                 [implemented: E2]
└─ encounter lifecycle state                                      [blocked parent]
   ├─ closed encounter-turn state definition                       [missing leaf: Slice 1]
   ├─ safe start transition from valid order                       [missing leaf: Slice 1]
   ├─ safe next-turn/round-wrap transition                         [blocked: Slice 2]
   └─ explicit terminal transition                                 [blocked: Slice 3]
```

The component and start transition are one leaf: creating state without its only normal creation
path would leave a malformed administrative surface. Advancing and ending consume that state and
must remain later slices. No dependency requires a new database migration, effect type, query
kind, commit kind, generic game helper, vector search, or external service.

## Dependency and ownership decisions

1. **Roster and order remain Feature 5 data.** Encounter containment is the roster and
   `dnd2024.encounter-initiative-order.order` is the sole ordered participant/count snapshot.
   Feature 11 revalidates both; it never copies them.
2. **Turn state is one new encounter component.**
   `dnd2024.encounter-turn-state` contains only `status`, `round`, `turnIndex`, and `sourceRef`.
   `status` is `active` or `ended`; `round` is a positive safe integer; `turnIndex` is a
   nonnegative safe integer less than the current snapshot length; and the fixed source reference
   is the Combat Order of Combat locator.
3. **The active participant is derived, not stored.** While status is `active`, it is exactly
   `order[turnIndex].participantId`; while status is `ended`, there is no active participant.
   This prevents a stale active ID or a second ordering source.
4. **Round and index are authoritative temporal state.** The current order cannot tell whether a
   participant has acted or how many times the order has wrapped, so `round` and `turnIndex` must
   persist. They are never caller input.
5. **Three mechanics are structurally justified.** Start can require an order but must tolerate
   absence of lifecycle state; advance/end must require valid lifecycle state. The declared
   projection model has required components rather than optional components, so one mechanic
   cannot truthfully serve both projections without a kernel change. The three transitions share
   one component and one governing contract rather than becoming per-creature or per-encounter
   mechanics.
6. **End is an explicit terminal transition.** It does not decide which side won, look at Hit
   Points, or remove the order. Ended state remains readable for audit; a later encounter-reset or
   campaign lifecycle feature must own any restart/correction policy.
7. **Roster drift fails closed.** Each transition materialises encounter contents and validates
   that their distinct identities exactly match the Initiative snapshot identities. A containment
   change after Initiative is not silently absorbed into a turn order.

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Encounter turn-state and start transition | Plan reviewed; catalog/database drift reconciled; Feature 5/10 baselines verified | A valid ordered encounter gains exactly one active round-1/index-0 state through its only normal start action, with all rejection/routing/replay/readback checks passing. |
| 2 | Advance one turn and wrap a round | Slice 1 verified in a later reviewed pass | A valid active encounter moves exactly one index or wraps once to the next round; no illegal/corrupt/drifted state changes. |
| 3 | Explicitly end an encounter | Slice 2 verified in a later reviewed pass | An active encounter becomes terminal once, retains only legitimate historical lifecycle data, and rejects subsequent start/advance/end attempts. |

## Slice 1 — encounter turn-state and start transition

### Runtime artifacts

| Artifact | Proposed ID/category | Change |
| --- | --- | --- |
| Governing contract | `procedure.mechanic.dnd2024.encounter-turn-lifecycle` in `ruleset.dnd2024.core.combat.turns` | New, initially governing the turn-state component and start action only. |
| Component definition and schema | `dnd2024.encounter-turn-state` | New encounter-owned closed state component. |
| Start mechanic | `mechanic.dnd2024.encounter-turn.start` in `ruleset.dnd2024.core.combat.turns` and scope `dnd2024-srd-5.2.1` | New deterministic state-changing mechanism. |
| Regression coverage | `CatalogFeature11Tests` (or the project’s equivalent focused catalog test) | New fresh-import coverage; no production fixture is needed. |

Future Slice 2 and 3 mechanics are not created in this slice. The governing procedure receives a
revised version alongside each later transition so it never advertises an unavailable action.

### Governing contracts and source locator

Immediately before writing, re-read `procedure.system.create-feature`, the Feature 5 encounter
order procedure, `procedure.mechanic.run`, `procedure.mechanic.projection`, and
`procedure.world.change` for disposable-fixture cleanup. Re-read the source registry and the
SRD Combat / Order of Combat locator above. Re-search `turn`, `round`, `start combat`, `start
encounter`, `initiative order`, and the proposed IDs/phrases against the imported catalog.

### Data/input contract and required state

- The start action input is exactly `{}`. Omitted input resolves to the existing `{}` default;
  `round`, `turnIndex`, `activeParticipantId`, `status`, roster, order, counts, source, outcome,
  effects, and every additional key are rejected.
- It has exactly one required role, `encounter`, declaring
  `dnd2024.encounter-initiative-order` and `includeContents: true`.
- Before effects, validate the full closed Initiative snapshot, its source reference, nonempty
  bounded order, unique participant IDs, integer counts, and exact membership equality with the
  current contained roster. Corrupt snapshot/containment state fails before any effect.
- The lifecycle state is intentionally absent before this action. The action uses exactly one
  `component.add` effect, so an already-present (including corrupt) lifecycle component prevents
  a second start atomically. It must never replace it.
- The produced state has `status: active`, `round: 1`, `turnIndex: 0`, and the fixed source
  reference. It contains no roster, active participant ID, Initiative count, seed, action budget,
  timestamps, end reason, or future-feature fields.

### Resolution behavior

1. Validate closed empty input, encounter projection, Initiative snapshot, and unchanged roster.
2. Reject malformed snapshot/roster data before constructing an effect.
3. Derive the active participant from index 0 solely for narration and result data.
4. Propose one `component.add` of the complete state to the encounter.
5. Return a structured start result with encounter ID, `active` status, round 1, index 0, derived
   active participant ID, participant count, and source locator. It uses no random calls.

### Invariants, failures, and non-goals

- Exactly one lifecycle state can be created for an Initiative snapshot; retrying start fails and
  leaves prior bytes unchanged.
- The start action never writes a participant, alters containment/order, rolls Initiative, spends
  an action, chooses an outcome, or applies a condition/event.
- Missing Initiative order fails through projection; an empty/corrupt/drifted order fails in the
  mechanic; all failures propose/apply zero effects.
- A successful start is a combat-lifecycle action, not a generic world correction. There is no
  `record`/`correct` administrative writer for this temporal state.

### Slice 1 implementation sequence

1. Resolve the catalog drift and record a clean `roleplay verify catalog` baseline.
2. Re-read the listed live contracts/dependencies and repeat overlap/routing searches.
3. Add the contract, component definition/schema, mechanic markdown/source, manifest entries, and
   focused fresh-import test in catalog files first.
4. Run `roleplay import catalog --dry-run`; inspect all conflicts/checks; import only the
   identical reviewed catalog once clean; query the artifacts back at their intended active
   versions/statuses.
5. In a fresh imported test database, create the existing Feature 10 Initiative snapshot through
   its parent action, run the start action with a seed, and parse result data/effects/state.
6. Run the complete acceptance matrix. For any manual live verification, use a disposable
   encounter/participants, delete all of them with dry-run-first effects, and query their absence.
   Do not alter the catalog-owned Feature 10 baseline.
7. Run focused tests, full `dotnet test DantesRoleplay.slnx --no-restore`, `roleplay verify
   catalog`, and `git diff --check`; record evidence in this plan, mark only Slice 1 verified, and
   stop for review.

### Slice 1 acceptance matrix

| Class | Required assertion |
| --- | --- |
| Happy path | Given Feature 10’s seeded untied order `hero > target`, start returns active/round 1/index 0/hero, applies one encounter `component.add`, and changes no participant or order component. |
| Differential | Reversing only the authorized Initiative tie order before start changes only the derived active participant; round/index/state shape remain identical. |
| Boundaries | A one-participant order starts at index 0; a 100-participant valid order starts at index 0; both report the exact participant count. |
| Closed input | Omitted and `{}` succeed equivalently; null/non-object roots fail in the shared action validator; every supplied lifecycle/order/roster/count/source/effect field and unknown key fails with no state change. |
| Missing/corrupt state | Missing order fails projection. Empty order, duplicate participant ID, noninteger count, wrong sourceRef, malformed JSON, and a duplicated/missing contained roster member fail before effect application. |
| Existing lifecycle | Starting an already-started encounter fails the one `component.add` validation and preserves the original lifecycle bytes, order bytes, and participant revisions. |
| Determinism | Equivalent fresh databases with the same snapshot/input/seed return equivalent structured data and exactly the same lifecycle component bytes; no random call is made. |
| Routing | `start encounter turns` and `begin combat turns` select only the start mechanic; `start the encounter` must remain an Initiative-order phrase or be deliberately revised with collision tests, never silently captured. |
| Readback/cleanup | Query back the procedure, definition, mechanic, and created state. Disposable live fixtures are deleted and absent; Feature 10 catalog fixtures remain baseline-only. |
| Repository | Import dry-run/import/verify are clean after drift resolution; focused tests and the full suite pass; `git diff --check` passes. |

### Slice 1 exit gate

All matrix rows must pass with recorded selected IDs/versions, parsed result fields, exact effect
count/type/data, state before/after evidence, query-backs, cleanup evidence, and repository
checks. Only then may the plan say Slice 1 is verified. Slice 2 remains blocked until a new review
authorizes it.

## Slice 2 — advance one turn and wrap a round

### Status and prerequisite

Blocked until Slice 1 is verified. This slice revises the lifecycle procedure and adds only
`mechanic.dnd2024.encounter-turn.advance`; it does not add action economy or ending behavior.

### Data/state and resolution contract

- Input is exactly `{}` and role `encounter` requires both Initiative-order and turn-state
  components with contents included.
- Revalidate the snapshot, roster equality, and full lifecycle shape/source before effects. State
  must be `active`; `round >= 1`; `0 <= turnIndex < order.length`.
- Derive the current participant from the validated pre-state. If `turnIndex + 1 < order.length`,
  set that next index and keep the round. Otherwise set index 0 and increment round exactly once.
  Reject a round increment beyond the safe-integer boundary.
- Propose exactly one full `component.set` on the encounter. Return before/after round/index and
  derived participant IDs plus an explicit `startedNewRound` Boolean. Never call randomness.

### Acceptance and exit gate

Prove nonfinal advance, final-index wrap, a one-participant encounter wrapping on every advance,
round 1 and safe-integer boundaries, ended-state rejection, missing/corrupt state, roster drift,
closed input, exact replay, routing, one-effect atomicity, and fixture cleanup. Verify that order
and participants are byte-identical. Slice 2 is complete only after those checks, import/query
evidence, full repository checks, and a review stop; Slice 3 is otherwise blocked.

## Slice 3 — explicitly end an encounter

### Status and prerequisite

Blocked until Slice 2 is verified. This slice revises the lifecycle procedure and adds only
`mechanic.dnd2024.encounter-turn.end`.

### Data/state and resolution contract

- Input is exactly `{}` and requirements mirror Slice 2.
- Validate order, containment equality, source references, and an `active` lifecycle state before
  effects. No caller supplies a winner, side, reason, final participant, round, or index.
- Preserve round, turnIndex, and source reference; replace only status with `ended` in one full
  `component.set` effect. The result reports the final historical round/index and states that no
  participant is active after completion.
- End is permitted once. A second end, later advance, or attempted start fails without modifying
  lifecycle/order/participants. Restart/reset/automatic defeat stay excluded.

### Acceptance and exit gate

Prove ending from index 0 and a later index, preservation of exact order/round/index/source,
no active participant after end, double-end/start/advance rejection, missing/corrupt/drifted
state rejection, closed input, replay, routing, one-effect atomicity, readback, cleanup, and full
repository checks. Feature 11 is verified only after the Slice 3 gate passes and the plan records
evidence; then stop before Feature 12.

## Plan-quality audit

1. Yes — one encounter lifecycle capability with explicit exclusions.
2. Yes — SRD 5.2.1 source entity, heading, and PDF page are concrete.
3. Yes — Initiative, turn/round, lifecycle, start/advance, and combat-state searches were made;
   Feature 5 is the only existing owner of adjacent persistent state.
4. Yes — dependencies cite catalog artifacts, Feature 5/10 regression coverage, and the current
   repository verification result.
5. Yes — the lifecycle component/start leaf is standalone; advance and end are blocked parents.
6. Yes — roster/order, stored temporal state, derived active participant, transient input, and
   downstream consequences have distinct owners.
7. Yes — Slice 1 creates a component together with its only safe normal creation mechanism.
8. Yes — Slice 1 alone is named as next.
9. Yes — absent, existing, corrupt, active, ended, and closed-input semantics are explicit.
10. Yes — state transitions, wrap branch, effects, result fields, and source data are testable.
11. Yes — acceptance covers happy/differential/boundary/invalid/missing/corrupt/replay/routing/
    effects/integrity/cleanup/readback/repository cases.
12. Yes — dry-run/import/query sequence and real action limitations are explicit.
13. Yes — fresh tests and disposable live fixture cleanup preserve baseline catalog entities.
14. Yes — every slice has an objective all-or-nothing exit gate.
15. Yes — no executable source, runtime payload, or duplicate schema is embedded here.
16. Yes — this planning pass stops before implementation.

## Plan-change rule

Stop and revise before implementation if a live query finds an existing turn/round owner, if the
catalog/database reconciliation changes a relevant Feature 5 artifact, or if the SRD source
requires a state distinction not represented here. Descend to a new dependency rather than adding
a second roster/order source, accepting caller-derived turn state, adding a kernel game helper, or
bundling Feature 12 action economy into Feature 11.

# Quest Feature 2 dependency plan — manual lifecycle and reconciliation

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Verified — Q2 manual lifecycle and reconciliation is complete.**
Last updated: 2026-08-20

## Execution rule

This is Q2's authoritative repository dependency plan. It implements the base
[Quest implementation plan](../../QUEST_IMPLEMENTATION_PLAN.md) without duplicating its roadmap.
Follow [AGENTS.md](../../AGENTS.md), `procedure.system.create-feature`,
`procedure.system.modify`, and `procedure.mcp.add-tool`. Implement exactly one accepted slice,
record its receipt, and stop. Do not import the persistent database during ordinary repository work.

Q2.1 replaced the unreachable provisional lifecycle sketch with the reviewed offer/accept runner.
Do not extend it outside a separately accepted Q2.2 or Q2.3 slice.

## Target capability

A trusted host can move one valid Q1 quest through a closed manual lifecycle with expected-state
protection, a factual reason retained in its immutable operation audit, inspectable objective
progress, and one atomic derived structural batch.

### Included

- Offer/accept a draft, activating eligible initial objectives.
- Complete, block, fail, unblock, and reopen an owned objective.
- Explicitly reconcile the parent; fail/reopen a quest; archive an unaccepted offer.
- Use only `commit(kind: "quest")` and derived `component.set` effects.

### Excluded

Player authorization/UI/reads, notifications, rewards, item/character/time/world writes,
conditions/query language, events/subscriptions, automatic event progress, templates, history
projection, campaign transitions, deletion/unarchiving, and generic correction. Q3 owns history
projection; Q4 owns event reactions; Q8–Q10 own item/time criteria; Q13 owns authorization.

## Rule basis and verified dependencies

Q2 is campaign workflow, not an SRD rule; no D&D source locator applies. Its authority is Q0's
ratified lifecycle boundary, Q1's creation contract, and these inspected repository artifacts.

| Dependency | Status and evidence |
| --- | --- |
| Q1 quest graph/statuses | Implemented: `QuestCreator`, schemas, and five focused Q1 tests prove campaign context, three objectives, ordering, dependencies, and scope. |
| C3 transaction model | Implemented: `CampaignContinuityRunner` validates state inside one transaction, dry-validates effects, correlates events/audit, then commits. |
| Effects and rollback | Implemented: `EffectApplier` validates an entire derived batch and writes no partial state. |
| Audit/read evidence | Implemented: `IOperationLog.RecordAsync` retains summary, intent, procedures, subject, outcome, and root operation ID. |
| Q2.1 lifecycle surface | Verified: `procedure.quest.modify`, `IQuestLifecycleRunner`, `QuestLifecycleRunner`, quest commit union, DI registration, and `QuestFeature2Tests` own only offer/accept. |
| Overlap search | No competing owner: there is no quest mechanic, subscription, query kind, or new commit kind. |

An MCP-only implementation pass must first make the procedure/capability/world/history reads
required at runtime; repository planning evidence does not replace those reads.

## Recursive dependency analysis

```text
Q2 manual lifecycle [verified]
├─ Q1 graph, C3 context, effects, audit                         [implemented]
├─ closed procedure and quest payload union                     [Q2.1 verified]
│  └─ typed runner, registration, offer/accept activation       [implemented]
├─ objective membership/dependency transitions                  [Q2.2 verified]
└─ reconciliation and terminal correction                       [Q2.3 verified]
```

Q2.1 adds no migration, schema field, event type, subscription, or commit kind. Q2.2/Q2.3 are
blocked until the prior slice passes its exit gate.

## Ownership and shared contract

1. Existing quest/objective `status` fields are the only lifecycle state. Q2 stores no campaign,
   world, evidence, or aggregate copy.
2. Membership, prerequisites, and campaign/arc/chapter context remain relationships. The runner
   validates the stored graph every time; callers never supply effects, child IDs, or links.
3. Extend existing `quest` into a closed payload union. New permanent
   **`procedure.quest.modify`** owns lifecycle calls; `procedure.quest.create` stays creation-only.
4. Require a trimmed `reason` of 1–1000 characters. Record it exactly in the successful operation
   summary; keep tool-level `intent` as the caller's separate goal. Q3 may project it later.
5. Require actual expected state; an identical retry is stale, not idempotent. Use whole component
   replacements, canonical order, dry validation, one operation ID, one transaction, and one audit.

| Operation | Exact payload fields | Expected state | Result |
| --- | --- | --- | --- |
| `offer` | `operation, questId, expectedQuestStatus, reason` | draft | offered root |
| `accept` | same | offered | active root plus every eligible dormant objective |
| `set-objective` | `operation, questId, expectedQuestStatus, objectiveId, expectedObjectiveStatus, targetStatus, reason` | active root/owned active objective | target `completed`, `blocked`, or `failed`; completion activates eligible dependants |
| `unblock-objective` | `operation, questId, expectedQuestStatus, objectiveId, expectedObjectiveStatus, reason` | active root/owned blocked objective | active only if prerequisites completed |
| `reconcile` | `operation, questId, expectedQuestStatus, reason` | active root | failed or completed root only |
| `fail` | same | active root | failed root only |
| `reopen-objective` | objective shape | active root/owned completed or failed objective | active only with completed prerequisites and no completed dependant |
| `reopen-quest` | root shape | completed or failed root | active root only |
| `archive` | root shape | offered root | archived root only |

All identifier/status/operation strings are nonempty, trimmed, and lower-case; `reason` preserves
case but must be trimmed. No null or extra field is legal. The public handler returns
`INVALID_PAYLOAD` before the runner for malformed closed input and a literal callable quest commit
as recovery.

Before effects, prove one nondeleted quest root; one `in-campaign`, one `in-arc`, and one/two
`in-chapter` links; active C3 campaign/arc/world; chapters in that campaign/arc; exactly three owned
valid objectives; and a backward acyclic same-quest dependency graph. Invalid context/graph rejects
with `QUEST_CONTEXT_INVALID`/`QUEST_GRAPH_INVALID`.

Eligible means dormant with all direct prerequisites completed. Iterate by `displayOrder`, then ID.
Accept writes root first; objective completion writes the objective first, then each newly eligible
dependant. Reconciliation is always explicit: required failure wins; otherwise all required complete
finishes; otherwise `NO_RECONCILIATION_CHANGE` produces no effect.

Success fields: `status`, `questId`, `operationId`, `structuralEventCount`, `changedObjectiveIds`,
and empty `problems`. Rejections have no structural count/changed IDs. Runner codes are
`INVALID_LIFECYCLE_REQUEST`, `QUEST_NOT_FOUND`, `QUEST_CONTEXT_INVALID`, `QUEST_GRAPH_INVALID`,
`STALE_QUEST_STATUS`, `OBJECTIVE_NOT_IN_QUEST`, `STALE_OBJECTIVE_STATUS`,
`OBJECTIVE_PREREQUISITES_UNMET`, `ILLEGAL_OBJECTIVE_TARGET`,
`OBJECTIVE_HAS_COMPLETED_DEPENDANT`, `NO_RECONCILIATION_CHANGE`,
`QUEST_EFFECTS_REJECTED`, and `QUEST_LIFECYCLE_FAILED`.

## Slice order and stop gates

| Slice | Prerequisite | Exit gate |
| --- | --- | --- |
| Q2.1 — offer/accept | Approved procedure ID, payload union, reason audit, initial activation | **Verified** in [Q2.1 receipt](QUEST-FEATURE-02-SLICE-1-RECEIPT.md); stop completed. |
| Q2.2 — objective progression | Verified Q2.1 | **Verified** in [Q2.2 receipt](QUEST-FEATURE-02-SLICE-2-RECEIPT.md); stop completed. |
| Q2.3 — reconcile/terminal correction | Verified Q2.2 | **Verified** in [Q2.3 receipt](QUEST-FEATURE-02-SLICE-3-RECEIPT.md); Q2 accepted. |

## Q2.1 — closed offer and accept

### Runtime artifacts and governing contracts

Add `catalog/procedures/quest/procedure.quest.modify.md`; revise quest component descriptions and
`procedure.quest.create`. Replace the sketch with typed Q2 request/result/interface/
runner contracts; register `IQuestLifecycleRunner`; add thin `QuestTools` delegation; update
`CommitTool`/`VerbSurface` to accept only Q1 creation or Q2.1 offer/accept. Add focused
`QuestFeature2Tests` and affected DI/surface/protocol tests. No migration, fixture, schema,
relationship, mechanic, event, subscription, or kind.

Immediately before writing, re-read `procedure.system.create-feature`, `procedure.system.modify`,
`procedure.mcp.add-tool`, `procedure.quest.create`, `procedure.world.change`, and this plan.

### Algorithm and acceptance

Verified: `offer` requires draft and derives one root replacement/effect/event. `accept` requires offered,
then derives root followed by eligible objective replacements. Return changed IDs in canonical order.
All stale/invalid/guard/apply/cancellation/exception paths have stable failure, one failed audit,
and unchanged state/events.

Prove one-effect draft→offered; Q1-fixture offered→active yields two ordered effects/events and
only Trace active; replay/wrong expected state preserves bytes/ledger; invalid reason, extra/null/
malformed/case-wrong input and mixed create/lifecycle fields reject; missing/corrupt graph/context
and injected guard/apply failure roll back fully; audits contain procedure/reason/intent/root ID;
capability/dispatch agree; focused/catalog/guard/protocol/full/diff checks pass. Write Q2.1 receipt,
update status, and stop before Q2.2.

## Q2.2 — owned objective progression

Revise only `procedure.quest.modify`, Q2 contracts/runner/tool surface, and focused tests; re-read
Q2.1 receipt and its governing contracts. No schema, relationship, mechanic, event, subscription,
migration, or kind.

`set-objective` changes only an owned active objective. Completion checks prerequisites, writes it,
then activates eligible dormant dependants canonically; blocked/failed writes only it. Unblock
requires owned blocked state and completed prerequisites. Foreign IDs, corrupt/cyclic/forward graph,
wrong expected status, forbidden target, unmet prerequisites, and replay reject atomically.

Prove Q1 Trace completion gives exactly three ordered effects (Trace complete; Test/Read active), a
non-completing target gives one, optional completion does not reconcile root, and every invalid/
injected-failure case preserves state/events. Verify audit/readback and focused/catalog/guard/
protocol/full/diff checks; write Q2.2 receipt and stop.

## Q2.3 — reconciliation and terminal correction

Revise only `procedure.quest.modify`, Q2 contracts/runner/tool surface, and focused tests; re-read
Q2.2 evidence and Q2.1 contracts. Do not introduce Q3 history, notification, subscription,
campaign mutation, reward, or generic correction.

On active reconcile: required failure wins; otherwise all required complete; otherwise no change.
Fail replaces active root. Reopen quest replaces terminal root only. Reopen objective requires active
root, expected completed/failed owned objective, completed prerequisites, and no completed direct or
transitive dependant. Archive replaces offered root. Every legal operation is one replacement/effect/
event and never changes objectives/campaign/evidence implicitly.

Prove precedence, optional failure, all-and-only-required completion, no-change blocked/undecided
required, one-effect fail/reopen/archive, denied invalid terminal/reopen calls, stale/malformed/
replay/rollback integrity, audit/readback, and focused/catalog/guard/protocol/full/diff checks.
Record Q2 acceptance receipt; mark Q2 verified only when all slice evidence agrees; stop.

## Plan-quality audit and change rule

One owner, explicit exclusions, gated permanent ID/public surface, sequential missing leaves,
closed state/error/ordering/transaction semantics, and per-slice exits are present. Revise before
implementation if review changes the ID, union, reason policy, transition table, graph invariants,
transaction boundary, or needs a schema, migration, event/subscription, query kind, authorization,
or cross-owner write. Never activate the provisional runner to bypass review.

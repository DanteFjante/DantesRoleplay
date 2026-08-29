# Interaction orchestration Slice 13D implementation — bounded task agendas and fresh-state work batches

Status: **accepted 2026-08-25**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Slice 13D](INTERACTION-ORCHESTRATION-SLICE-13-DEPENDENCY-PLAN.md#dependency-tree)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**

## Outcome and boundary

For an actionable application-conversation turn, the selected local or remote outer AI may split
one player goal into a bounded ordered task agenda and bounded work-batch intent texts. The server
validates that untrusted agenda, assigns host identities, and asks the inner planner to resolve only
the next eligible batch against current authoritative state and contracts. One exact proposal is
shown for confirmation. After the player executes it, the server records the existing durable
resolution/execution receipts, advances process-local progress, and plans at most one next batch.
Every batch therefore gets fresh discovery, exact consent, independent execution/replay evidence,
and a deterministic stop before another mutation.

The application-conversation view exposes safe task/batch progress and receipt references, so the
outer AI and player can see what completed, what is awaiting confirmation, and why work stopped.
The agenda follows the existing ephemeral conversation lifetime; durable game state, operations,
and interaction receipts remain authoritative and are never reconstructed from agenda memory.

Exclusions: background execution; whole-goal mutation consent; unbounded or model-controlled loops;
cross-batch transactions/rollback; cross-batch result-value bindings; durable agenda recovery;
parallel execution; automatic continuation after failure; arbitrary task code/tools; model-supplied
contract IDs/effects/authority/success; recipe promotion/generalization; new MCP tools; game rules.

Allowed files/areas: interaction outer/task-agenda contracts and local/remote provider adapters;
interaction component registration; application conversation state/service/view and its existing
private web element/route request shapes; focused interaction/web/provider tests; concise component,
owner, dependency-plan, and receipt updates. Reuse existing planning, verification, execution,
consent, receipt, catalog, query, action, and learning owners. No migration is allowed in this slice.

Stop when one multi-task agenda with one multi-batch task advances through exact per-batch
confirmation and receipts, failure/replacement/bounds/provider parity pass, and the complete suite is
green. Do not begin Slice 13E or automatically promote a successful outer route.

## Confirmed decisions

The user confirmed this complete package on 2026-08-25:

1. Add the permanent ruleset-neutral outer-model task `system.interaction.task-agenda` and closed
   schema name `interaction_task_agenda_v1`. It is available through both explicitly selected outer
   providers, uses no tools, and cannot plan or execute contracts.
2. The schema contains exactly `tasks`; each task contains exactly `intentText`, `dependsOn`, and
   `batches`; each batch contains exactly `intentText`. Dependencies are distinct one-based ordinals
   naming earlier tasks only. The host assigns goal/task/batch IDs.
3. Bounds are at most 8 tasks, 4 batches per task, 16 batches total, 4 dependencies per task,
   2,000 UTF-8 bytes per intent, 32 KiB canonical agenda JSON, and nesting depth 8. Empty agendas,
   empty batch lists, cycles, future dependencies, duplicate dependencies, unknown properties, and
   control characters fail closed.
4. The agenda and its progress are process-local, principal/application/state-space scoped, and
   share the existing 128-conversation, 64-message, 64-KiB, two-hour idle limits. Restart/expiry
   discards only pending agenda memory. Existing resolution, execution, query, operation, and
   learning receipts remain durable and truthful; no workflow migration is added.
5. Every batch, including query-only work, follows the existing two-phase behavior: current-state
   plan first, then a separate exact `/execute` confirmation. A successful execute may perform at
   most one next inner-first resolution attempt and may leave one new inert proposal awaiting the
   next confirmation. No request executes more than its already-confirmed proposal.
6. Tasks and batches run sequentially in declared order. A task starts only after all dependencies
   completed. A successful batch deterministically advances to the next declared batch; a task
   completes when all its declared batches have successful execution receipts. No model command can
   assert task success.
7. `needs-input`, `unknown`, `unsupported`, `unavailable`, unsafe/stale planning, execution failure,
   or cancellation pauses the whole agenda. Dependent tasks become blocked and independent tasks do
   not continue automatically. Completed batches remain completed and are never rolled back.
8. Extend the existing conversation turn request with optional `replaceActiveAgenda`, default
   `false`. A verified player may set it to discard the remaining process-local agenda and pending
   proposal before starting the new turn; completed durable operations/receipts are untouched.
9. Extend the existing application-conversation view with nullable `activeAgenda`. It exposes only
   host IDs/ordinals, safe intent text already visible to that principal, closed lifecycle names,
   current task/batch, and resolution/execution receipt IDs. No raw binding-only query value,
   hidden evidence, host path, or model transcript is included. No new HTTP or MCP route is added.
10. Existing opt-in `learn` remains per confirmed batch and keeps its accepted 12G behavior. Slice
    13D does not automatically learn or promote outer fallback; that remains Slice 13E.

## Prerequisite evidence

- [Slice 13B receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-13B-RECEIPT.md) proves each batch can
  use inner-first resolution and one typed outer fallback through the common verifier.
- [Slice 13C receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-13C-RECEIPT.md) proves a proposal may
  combine read-only queries and actions with bounded result bindings, safe outputs, and equal replay.
- The existing application conversation is already principal/application/state-space scoped,
  serialized per conversation, explicitly confirmed through `/execute`, bounded to two hours and
  64 KiB, and process-local. It is the only owner revised for agenda progress.
- Resolution/execution receipts and action operation records are already durable, idempotent, and
  independently truthful. A task agenda must correlate those owners rather than duplicate them.
- Search found no accepted task-agenda/work-batch runtime owner or conflicting permanent IDs.

No Foundry or SRD review applies because this slice is generic orchestration and defines no rule.

## Runtime artifacts

| Artifact | Change | Authority |
| --- | --- | --- |
| `system.interaction.task-agenda` | New permanent no-tools outer-model task | Host configuration and selected outer adapter |
| `interaction_task_agenda_v1` | New permanent closed JSON schema | Interaction outer protocol parser |
| Task-agenda contracts/provider | New generic domain seam | Server validation; model output remains untrusted |
| Process-local agenda progress | New conversation-owned state | Current verified application conversation only |
| `activeAgenda` | New nullable conversation-view field | Host projection from process-local progress plus durable receipt IDs |
| `replaceActiveAgenda` | New optional turn-request field, default false | Verified caller request; host performs cancellation before a new turn |

Goal, task, and batch IDs are ephemeral host-created correlation identifiers, not application
catalog IDs. No schema migration, database table, application record, procedure, mechanic, component
state, action, or MCP kind/tool is added.

## Authoritative state and closed input

The outer task-agenda provider receives only the bounded player/outer intent text and decomposition
instructions. It may return intent-level wording and earlier-task dependencies. It may not return a
qualified contract/query/action ID, version, fingerprint, role/entity binding, input value, effect,
authorization, state revision, receipt, success claim, planner/provider selection, consent, or code.

The host validates/canonicalizes the complete agenda, assigns opaque correlation IDs, retains its
fingerprint, and chooses the next eligible ordinal. For every batch it creates a fresh existing
authorized intent envelope with deterministic goal/task/batch/attempt correlation and empty caller
role hints. The existing recipe resolver, trusted catalog discovery, common verifier, projection
registry, action owner, current application/state-space scope, and receipt store supply all trusted
execution facts.

No output value crosses a batch. A later batch that needs current information must discover and run
an authorized query again. Only safe receipt references and bounded prior completion summaries may
be given to the provider; raw binding-only results and application state are never added to the task
agenda prompt.

## Behavior, continuation, and transaction ownership

1. The selected outer provider first retains the accepted `respond` versus actionable decision. For
   an actionable decision it produces one closed task agenda; invalid/unavailable output stops with
   safe evidence and no inner planning.
2. The host validates the whole agenda before retaining it. It assigns a goal ID and task/batch
   ordinals, marks the first dependency-ready batch `planning`, and calls the existing gateway once.
3. Each batch always uses inner-first planning. The accepted single outer fallback remains available
   only for typed inner `unknown`, `unsupported`, or `unavailable`; it cannot recurse.
4. A resolved proposal is retained as the sole pending proposal and the batch becomes
   `awaiting-confirmation`. No other task/batch is planned while it is pending.
5. `/execute` confirms only that proposal's resolution receipt/fingerprint. Existing query/action
   execution and independent transactions run unchanged. Agenda memory advances only after the
   durable execution outcome returns.
6. On success, the batch stores safe resolution/execution receipt references and becomes
   `completed`. If it was the task's last batch, the task becomes `completed`. The host selects the
   next declared dependency-ready batch and performs at most one fresh planning attempt before
   returning control.
7. After the last batch receipt, the agenda becomes `completed`, clears pending state, and returns to
   ordinary conversation. Per-batch outer narration remains the only player-facing interpretation;
   it may not change lifecycle truth.
8. On a typed stop/failure, the active batch records only safe code/receipt references, the agenda
   becomes `needs-attention`, dependency descendants project as `blocked`, and no other batch starts.
9. `replaceActiveAgenda=true` marks unfinished process-local items `cancelled`, clears an inert
   pending proposal, and then handles the new player turn. It cannot cancel or compensate an action
   whose durable execution already completed.

Each proposal execution remains one root mutation transaction owned by its existing action/ECS
owner. The agenda is only a bounded coordinator and never spans transactions. Its process-local
update occurs after receipt persistence; a crash may lose continuation UI but cannot lose or invent
game-state/receipt truth. Retrying an already-submitted execute uses existing exact idempotent replay.

## Lifecycle projection

Closed agenda states: `planning`, `awaiting-confirmation`, `needs-attention`, `completed`,
`cancelled`. Closed task states: `pending`, `active`, `completed`, `blocked`, `cancelled`. Closed
batch states: `pending`, `planning`, `awaiting-confirmation`, `completed`, `unresolved`, `failed`,
`cancelled`.

These describe coordinator progress, not game success beyond the cited execution receipt. There is
no model-authored `complete`, `skip`, `retry`, or lifecycle command.

## Failure, replay, and no-change contract

| Condition | Result | No-change/evidence guarantee |
| --- | --- | --- |
| Malformed/oversized agenda or invalid dependency | `TASK_AGENDA_INVALID` | No agenda retained, inner plan, query, or mutation. |
| Selected outer agenda provider unavailable | `TASK_AGENDA_UNAVAILABLE` | No silent remote/local fallback and no inner plan. |
| Wrong principal/application/state space | Existing denial/not-found | No agenda disclosure or advancement. |
| New turn while agenda/pending plan exists without replacement | `INTERACTION_CONFIRMATION_REQUIRED` or `TASK_AGENDA_ACTIVE` | Pending state remains exact. |
| Planning needs input/is unknown/unsupported/unavailable/unsafe/stale | Agenda pauses `needs-attention` | No execution; durable resolution receipt remains truthful when one exists. |
| Execute does not match current receipt/fingerprint/idempotency | Existing conflict/denial | Agenda does not advance and no second work starts. |
| Query/action execution fails or partially completes | Agenda pauses; batch is `failed` | Existing step/operation/receipt truth wins; no cross-batch rollback or continuation. |
| Equal execute replay | Existing receipt is returned | Agenda transition is idempotent; query/action does not rerun. |
| Cancellation/request abort during planning | Agenda pauses or replacement can discard it | No claimed proposal/execution without its receipt. |
| Conversation expiry or process restart | Pending agenda disappears | Durable operations and receipts remain; no automatic reconstruction/resume. |
| Replacement after completed batches | Remaining memory cancelled; new turn starts | Completed effects/receipts remain immutable and visible through existing audit owners. |

## Implementation sequence for the implementing AI

1. After confirmation, mark this document active. Add strict pure task-agenda contracts, schema,
   parser, bounds, canonical fingerprint, lifecycle projections, and exhaustive contract tests.
2. Add the no-tools task-agenda method to the common selected outer-provider seam. Implement local
   and remote adapters using their separate accepted profiles/configuration; prove no tool access or
   automatic provider/network fallback.
3. Add process-local agenda state to the existing application conversation. Refactor current 13B
   inner-first/fallback planning into one reusable private batch-resolution path without changing
   single-intent behavior.
4. Integrate deterministic first/next batch selection, fresh envelope/idempotency/correlation,
   exact pending-proposal ownership, post-receipt advancement, failure pause, equal replay, and
   replacement. Never loop over multiple planning/execution batches in one request.
5. Add nullable `activeAgenda` and optional `replaceActiveAgenda` to existing web projections and
   update the reusable non-technical application-conversation element to show task/batch progress
   and request replacement explicitly. Add no route.
6. Add focused local/remote provider, service, web, replay, privacy, bounds, failure, expiry, and
   compatibility tests. Run catalog validation if component manifests change, build, full suite,
   and protocol walk because provider dependency composition/public result projections change.
7. Read back every artifact, write the receipt, update owner/dependency status once, request
   completed-feature acceptance, and stop before 13E.

Do not add a database workflow, background worker, timer, scheduler, generic expression evaluator,
cross-batch value cache, parallel task runner, new authorization capability, or game-specific C#.

## Acceptance matrix

| Area | Required proof |
| --- | --- |
| Positive | Local and remote outer providers each produce the same valid agenda; two dependent tasks including one two-batch task progress one confirmed proposal at a time to completion. |
| Fresh-state batches | Each batch independently performs recipe/current-catalog resolution after the preceding execution receipt; no proposal or binding value is reused across batches. |
| Inner/outer routing | Every batch is inner-first; eligible non-resolution gets exactly one correlated outer fallback; other statuses stop. |
| Explicit consent | No query/action executes from agenda creation or prior batch success; each next proposal requires a separate exact `/execute`. |
| Bounds | Exact task/batch/dependency/text/JSON/depth limits pass; limit plus one and all malformed dependency/property cases fail before planning. |
| Failure | Needs-input, unsafe/stale, partial action failure, cancellation, and provider unavailability pause the agenda and start no independent/dependent later work. |
| Replay | Repeating an equal execute neither reruns work nor advances twice; conflicts leave progress unchanged. |
| Replacement | Default new turn rejects while active; explicit replacement discards only pending process memory and preserves all completed receipt/effect truth. |
| Privacy | Active agenda and model prompts contain no binding-only query output, hidden contract evidence, path, raw provider transcript, or unauthorized receipt. |
| Expiry/restart | Process-local agenda is not reconstructed; durable receipts/operations remain queryable and no work resumes automatically. |
| Compatibility | Existing single-action conversations, 13B fallback, 13C query/action proposals, opt-in learning, auth, local/remote selection, and action-only fingerprints remain green. |
| Architecture | Generic C# contains no application ID/rule vocabulary; no database, catalog, scheduler, or game owner is duplicated. |
| Surface | Existing private web route gains only the confirmed nullable/optional fields; MCP kind/tool count and remote security boundary remain unchanged. |

## Verification commands

```powershell
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter "FullyQualifiedName~InteractionTaskAgenda|FullyQualifiedName~InteractionOuterProvider|FullyQualifiedName~InteractionPlanning"
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter "FullyQualifiedName~ApplicationConversation"
dotnet run --project DantesRoleplay.Tools --no-build -- validate catalog
dotnet build DantesRoleplay.slnx --no-restore
dotnet test DantesRoleplay.slnx --no-restore
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore -p:IncludeProtocolWalkTests=true --filter "FullyQualifiedName~ProtocolWalkTests"
```

## Completion receipt and exit gate

Write `platform/interaction-orchestration/receipts/INTERACTION-ORCHESTRATION-SLICE-13D-RECEIPT.md`
with the delivered boundary, provider/web/protocol evidence, focused/full counts, deliberate
exclusions, and confirmation reference. Mark 13D accepted only after the user confirms completed
feature acceptance. Stop before automatic recipe generalization/promotion or combined 13F work.

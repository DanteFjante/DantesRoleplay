# Story plan orchestration — Terra implementation plan

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Proposed; implementation begins only after the Slice 0 semantic confirmation**
Prepared: 2026-08-21

## Outcome

A remote story LLM may submit one small, linear plan describing what it needs the game backend to
do. The backend persists the plan, processes exactly one step at a time, retrieves and records the
procedure governing that step, uses existing typed services to perform the step, and returns one
bounded story handoff. The remote LLM uses that handoff to continue narration.

The first release is for the existing loopback development GM seat. It is not production remote
authentication and does not support an actor/player seat.

```text
remote story LLM
  -> commit(kind: "story-plan", operation: "start", semantic steps)
  -> durable pending plan
  -> backend worker claims one step
       -> retrieve current procedure contract
       -> campaign-context: bounded campaign resume
       -> knowledge: authorized knowledge answer
       -> action: intent route -> procedure-bound verification -> ActionRunner
       -> persist bounded receipt
  -> repeat serially until complete, blocked, failed, or cancelled
  -> query(kind: "story-plan", id: ...)
  -> compact story handoff
  -> remote LLM narrates through procedure.play.storytelling
```

## Current foundation and exact gaps

Already implemented:

- `ILocalRouteProposalCoordinator` searches active mechanics and active procedure summaries from an
  intent, lets configured Qwen select only supplied IDs, validates roles/input, and stops before a
  write.
- `IActionRunner` selects and runs one active mechanic, applies its effects atomically with events
  and audit evidence, and returns narration plus affected entity IDs.
- `IAuthorizedKnowledgeAnswerCoordinator` answers campaign-scoped knowledge for the configured GM
  or actor audience with validated internal citations.
- `ICampaignResumeReader` returns the fixed trusted-host campaign/chapter/arc resume view.
- `IProcedureStore` resolves append-only current procedure versions and source hashes.
- `procedure.play.storytelling` governs how verified state and mechanic output become narration.
- The MCP host has durable SQLite authority, the three verbs, an operation log, local Qwen
  completion, and background-worker infrastructure.

Not implemented:

- a story-plan request/result contract;
- durable plan and per-step state;
- a worker that executes one plan step at a time;
- full-procedure verification of an action proposal;
- atomic linkage between a committed action and its story-step receipt;
- a compact final handoff to the story model;
- `story-plan` query/commit kinds and their procedure contract.

## Boundary decisions — Terra must not reopen these

### This is not the registered workflow subsystem

`EXECUTABLE_WORKFLOW_PLAN.md` owns authored, versioned workflows whose registered semantic commands
run in one root transaction and roll back together. This feature is different:

- the remote model supplies a transient **story plan**, not a workflow definition;
- a step supplies a semantic intent, not a command ID;
- mechanics, procedures, and service routes are selected by the backend;
- every action retains its existing independent transaction and operation ID;
- completed actions are played history and are not rolled back when a later step stops;
- no workflow definition, workflow version, JSON binding language, or workflow catalog record is
  created.

Do not call these records workflows in code or public contracts. Use `StoryPlan` and
`StoryPlanStep` consistently.

### Remote versus local model ownership

The backend does not call a hosted story-model API. The remote story LLM is the existing MCP caller:
it submits the plan and later reads the handoff. The backend may call only the already configured
local structured-completion provider (normally Qwen 8B) for existing intent routing and the one
procedure-verification task defined below.

Use the current Ollama profiles without adding another client or model selector:

- `qwen3:8b` remains the configured structured-completion model for
  `knowledge.authorized-answer`, `routing.propose`, and new task
  `story-plan.verify-procedures`;
- `qwen3-embedding:4b` remains the configured derived knowledge-vector provider, but this feature
  does not call it directly or change retrieval authorization; the existing authorized knowledge
  path remains authoritative (and currently uses its pre-ranking audience-safe lexical path);
- if structured completion is disabled/unavailable, context still works, while knowledge/action
  produce the stable bounded outcomes below; the backend never substitutes the remote model for a
  failed local task.

The remote model decides the high-level objective, semantic step intents, and final narration. The
backend decides candidate retrieval, procedure versions, mechanic selection validation, execution,
persistence, and stop/failure behavior. Neither model can submit effects or choose a database path.

### First-generation step vocabulary

The only step kinds are:

| Kind | Meaning | Typed owner |
| --- | --- | --- |
| `campaign-context` | Read the current campaign, chapter, arc, references, and recent milestones as one bounded GM context summary. | `ICampaignResumeReader` |
| `knowledge` | Answer one question from campaign-scoped knowledge. | `IAuthorizedKnowledgeAnswerCoordinator` |
| `action` | Route and execute one mechanic-backed game action. | `ILocalRouteProposalCoordinator` then `IActionRunner` |

`campaign-context` is the only context read: it always reads the request's `CampaignId`; its intent
is a human-readable reason used only in the receipt and is not a query/filter. No generic read,
entity lookup, graph query, procedure lookup, raw effect, campaign transition, quest transition,
session transition, arbitrary tool call, nested plan, workflow call, or shell/SQL step exists. A
later typed step kind requires a plan amendment naming its semantic owner and tests.

### Linear and bounded only

- 1–6 ordered steps; at most 4 may be `action`.
- Steps execute strictly in array order with global worker concurrency 1 by default.
- No branches, loops, conditions, parallel steps, retries, recursion, dynamic steps, or output
  bindings.
- Later steps may receive bounded prior-step summaries as context for procedure verification, but
  cannot bind prior JSON into their role map or input.
- The first blocked or failed step stops the plan. All remaining pending steps become `skipped`.
- There is no resume operation in version 1. The remote model submits a new plan after it resolves
  missing information.

### Partial completion is authoritative

Each successful `action` step is its own normal atomic game action. If step 3 fails after steps 1
and 2 completed, their state, events, notifications, audit, and receipts remain committed. The plan
returns `blocked` or `failed` with `completedStepCount = 2`; it never claims the plan was all-or-
nothing and never compensates or rewinds played state.

This choice is deliberate. Cross-step rollback belongs to registered executable workflows, not to
an AI-authored story plan.

## Closed public contracts

### Start request

Add core records under `DantesRoleplay/Story/StoryPlans.cs`:

```csharp
public sealed record StoryPlanStartRequest(
    string Operation,          // exact "start"
    string RequestToken,       // caller-generated replay key
    string CampaignId,
    string Objective,
    IReadOnlyList<StoryPlanStepRequest> Steps);

public sealed record StoryPlanStepRequest(
    string Id,
    string Kind,               // campaign-context | knowledge | action
    string Intent,
    IReadOnlyDictionary<string, string>? RoleEntityIds = null,
    string Input = "{}");
```

The start payload has exactly `operation`, `requestToken`, `campaignId`, `objective`, and `steps`.
Each step has exactly required `id`, `kind`, and `intent`, with optional `roleEntityIds` and `input`.
Missing roles means empty; missing input means `{}`; explicit null is accepted only for
`roleEntityIds`. The cancel payload has exactly `operation`, `storyPlanId`, and `expectedRevision`.
Reject unknown or duplicate JSON properties at every object level before record deserialization;
do not rely on serializer last-property-wins behavior.

Validation is exact:

- `RequestToken`: exact regex `^[A-Za-z0-9][A-Za-z0-9.-]{7,99}$`.
- `CampaignId`: existing lowercase dotted ID rules, maximum 200 characters.
- `Objective`: trimmed, 1–1,000 characters.
- Serialized request: maximum 16,000 UTF-8 bytes.
- Step ID: unique within the plan, regex `^[a-z][a-z0-9-]{0,39}$`.
- Kind: exactly `campaign-context`, `knowledge`, or `action`.
- Intent: trimmed, 1–500 characters.
- `campaign-context`: roles must be null/empty and input must be exact `{}`; there may be at most
  one such step, and it must be the first step.
- `knowledge`: roles must be null/empty and input must be exact `{}`.
- `action`: at most 12 distinct role names; role names are trimmed 1–100 characters; values are
  canonical IDs up to 200 characters; input is a JSON object no larger than 4,000 UTF-8 bytes.
- The request has no principal, audience, role, actor, world, procedure ID, mechanic ID, command,
  tool, effect, seed, workflow ID, retry rule, model, prompt, or hidden-data option.

### Cancel request

```csharp
public sealed record StoryPlanCancelRequest(
    string Operation,          // exact "cancel"
    string StoryPlanId,
    int ExpectedRevision);
```

Cancellation is checked only between steps. A currently running action finishes or rolls back under
its existing atomic boundary; cancellation never interrupts after effects have committed but before
the step receipt is written.

### Query

`query(kind: "story-plan")` accepts `id` (required), optional `afterRevision` from 0 upward, and
optional `waitSeconds` from 0–20. It accepts no list/search mode in version 1. When
`afterRevision` equals the current revision and the plan is nonterminal, the query may wait until
the revision changes or the bounded wait expires. It returns the current projection either way;
there is no server push.

### Status vocabulary

Plan status is exactly `pending`, `running`, `completed`, `blocked`, `failed`, or `cancelled`.
Step status is exactly `pending`, `running`, `completed`, `blocked`, `failed`, or `skipped`.

`blocked` means valid external input or capability is missing and a new plan may resolve it.
`failed` means the selected backend operation was attempted and rejected or encountered an
unexpected failure. No automatic retry occurs in either case.

### Result and story handoff

```csharp
public sealed record StoryPlanResult(
    string StoryPlanId,
    string CampaignId,
    string Status,
    int Revision,
    string Objective,
    int CompletedStepCount,
    IReadOnlyList<StoryPlanStepResult> Steps,
    StoryHandoff? Handoff,
    string StopCode = "",
    string StopMessage = "");

public sealed record StoryPlanStepResult(
    string Id,
    string Kind,
    string Status,
    string Summary,
    IReadOnlyList<string> Findings,
    string Narration,
    IReadOnlyList<string> MissingInformation,
    IReadOnlyList<string> AffectedEntityIds,
    string OperationId = "");

public sealed record StoryHandoff(
    string Objective,
    string Outcome,
    IReadOnlyList<string> ContextSummaries,
    IReadOnlyList<string> FactsLearned,
    IReadOnlyList<string> ActionNarrations,
    IReadOnlyList<string> AffectedEntityIds,
    IReadOnlyList<string> Unresolved,
    IReadOnlyList<string> ProcedureIdsForNextTurn);
```

The handoff is created only for a terminal plan. `ProcedureIdsForNextTurn` is exactly
`["procedure.play.storytelling"]` in version 1. It tells the remote model what to retrieve before
narrating; the backend does not return the full storytelling contract in every result.

The public result omits raw effects, projections, procedure bodies, candidate lists, model prompts,
model JSON, policy revision, source hashes, hidden knowledge IDs, sensitivity, database lease data,
and stack traces. `Narration` comes only from an executed mechanic; the orchestration layer does not
invent mechanical outcomes or story prose.

Result mapping is fixed:

- a completed `campaign-context` step has summary `Campaign context loaded.`, empty narration and
  affected IDs, and bounded `Findings` ordered as campaign, chapter, arc, references, milestones;
- an answered `knowledge` step has summary `Knowledge answer completed.`, empty narration and
  affected IDs, and one `Findings` item per `AuthorizedKnowledgeStatement`, formatted exactly as
  `[<stance>/<presentationKind>] <text>`; `Unresolved` maps to `MissingInformation`;
- an authorized unknown/familiar knowledge result completes with summary `No definite knowledge
  was found.`, no findings, and its safe unresolved messages;
- a completed `action` step has summary from `ActionRunResult.Summary`, empty findings, narration
  from `ActionRunResult.Output.Narration`, affected IDs and operation ID copied unchanged;
- blocked/failed/skipped steps have no findings, narration, affected IDs, or operation ID.

`StoryHandoff.ContextSummaries` concatenates completed context findings,
`FactsLearned` concatenates completed knowledge findings, and `ActionNarrations` contains each
nonblank mechanic narration in step order. `AffectedEntityIds` is ordinal-distinct in first-seen
order. `Unresolved` contains the stopping step's missing information and then its public stop
message, ordinal-distinct. `Outcome` is one of `completed all steps`, `blocked after N of M steps`,
`failed after N of M steps`, or `cancelled after N of M steps`; the backend adds no prose beyond
these fixed strings. Each public string is at most 1,000 characters, each list at most 32 items,
and the complete serialized result at most 32,000 UTF-8 bytes. A handler must enforce its smaller
limits before persistence; impossible final aggregation fails with `STORY_INTERNAL_FAILURE` and
does not expose a partial oversized result.

Campaign findings use only these templates, omitting a whole optional suffix when its value is
null/blank:

```text
Campaign: {Title}. Premise: {Premise}
Goal: {PartyGoal}
Boundary: {ToneOrBoundary}
Chapter: {Title}. Party question: {PartyQuestion}. GM context: {GmContext}
Arc: {Title}. Party stake: {PartyStake}. GM context: {GmContext}
Reference ({Role}/{Audience}): {Name}. {Summary}
Milestone: {Title}. {ClosingSummary}
```

Preserve reader order for goals/boundaries, and the reader's already deterministic order for
references/milestones. Take the first 8 goals, first 8 boundaries, first 12 references, and all of
the reader's at-most-5 milestones. Do not include entity, event, chapter, or arc IDs in these
strings.

## Procedure-bound step preparation

Every step retrieves a current full procedure before executing:

- `campaign-context` always retrieves `procedure.campaign.chapter` and requires its active current
  version to govern `query(kind: "campaign-resume")`. It then calls
  `ICampaignResumeReader.GetAsync` with the plan's fixed campaign ID; there are no model-generated
  filters or IDs.
- `knowledge` always retrieves `procedure.game.core.world.knowledge` and requires its active current
  version to govern `query(kind: "knowledge-answer")`.
- `action` first calls `ILocalRouteProposalCoordinator`. Its proposal must be `proposed` and name
  1–3 procedure IDs from the coordinator's supplied active candidates. The backend loads those
  exact current versions and requires at least one `Governs` value containing the exact token
  `commit(kind: "action")`.
- A procedure that changed version/hash after routing causes `STORY_PROCEDURE_STALE`; the step is
  rerouted once from the beginning. A second change blocks the step. There is no broader retry.
- Combined selected full procedure content is capped at 12,000 characters. Exceeding the cap blocks
  with `STORY_PROCEDURE_CONTEXT_TOO_LARGE`; do not truncate instructions or constraints.

The fixed `campaign-context` and `knowledge` handlers validate their procedure ID/version/governs
value and then call their typed owner; they do not ask another model to interpret the procedure.
The existing authorized-knowledge Qwen task remains the no-tools, citation-checked implementation
inside its typed owner and receives no plan-supplied instructions. Only the more general `action`
handler feeds full procedure text to the procedure verifier described below. This is the exact
meaning of “procedure-bound” in version 1; do not fork or weaken the existing knowledge-answer
coordinator to inject procedure prose.

Typed calls are also fixed:

- context calls `ICampaignResumeReader.GetAsync(plan.CampaignId)`;
- knowledge calls `IAuthorizedKnowledgeAnswerCoordinator.AnswerAsync` with campaign ID from the
  plan, question from step `Intent`, null kinds/subject IDs/as-of minute, and candidate limit 12;
- action routing uses intent/roles/input from the step, null scope, and candidate limit 8;
- successful action execution copies the verified proposal into `ActionRequest`, uses null seed,
  and replaces `ProceduresUsed` with the exact verified procedure IDs.

Do not allow objective, prior summaries, model output, or context findings to mutate any of these
typed request fields.

For every selected contract, record an actual read through `IOperationLog.RecordAsync` with public
tool `query`, subject equal to the procedure ID, success true, and `consumesReadEvidence = false`.
Pass the same IDs unchanged as `ActionRequest.ProceduresUsed`.

Add task class `story-plan.verify-procedures` and `IProcedureBoundActionVerifier`. Its Qwen request
receives only objective/current intent, the unchanged proposed action, selected mechanic
ID/version/roles, 1–3 selected full procedures, and at most four prior safe summaries capped at
4,000 characters total. Its closed result is:

```json
{"status":"ready | blocked","reason":"bounded explanation","missingInformation":["bounded question"]}
```

The verifier cannot return or alter intent, mechanic ID, procedure ID, roles, input, seed, effects,
tools, commands, or steps. Malformed output, unavailable Qwen, changed model identity, or stale
mechanic/procedure blocks safely. Candidate/procedure text is untrusted data. The host rechecks
route ranking and all versions/hashes after verification.

## Durable runtime model

Add a migration only in Slice 3. Use SQLite tables, not world entities or catalog records.

### `StoryPlanRuns`

| Column | Meaning |
| --- | --- |
| `Id` | `story-plan.` plus lowercase GUID-N; primary key. |
| `RequestToken` | Unique replay key. |
| `CampaignId`, `Objective`, `PlanJson` | Exact validated request evidence. |
| `PrincipalId`, `PolicyRevision` | Private development-GM authorization evidence. |
| `Status`, `Revision` | Closed status; revision starts at 1 and increments on visible change. |
| `NextStepIndex`, `CompletedStepCount`, `CancelRequested` | Progress. |
| `StopCode`, `StopMessage`, `HandoffJson` | Bounded terminal evidence. |
| `LeaseOwner`, `LeaseUntilUtc` | Private worker claim. |
| `CreatedAtUtc`, `UpdatedAtUtc` | UTC timestamps. |

Indexes: unique `RequestToken`; ordinary `(Status, LeaseUntilUtc, UpdatedAtUtc)`.

### `StoryPlanStepRuns`

Composite primary key `(StoryPlanId, StepIndex)`, required foreign key to `StoryPlanRuns`, cascade
delete disabled. Columns: `StepId`, `Kind`, `Intent`, `RoleEntityIdsJson`, `InputJson`, `Status`,
`ProcedureEvidenceJson`, `MechanicId`, `MechanicVersion`, `ActionOperationId`, `ResultJson`,
`ErrorCode`, `ErrorMessage`, `StartedAtUtc`, and `CompletedAtUtc`.

Store no model prompts/raw replies, procedure bodies, knowledge candidates, projections, or effects.

For a `campaign-context` result, store only a bounded projection: title and premise (1,500
characters combined), at most 8 party goals/boundaries, current chapter title/question/GM context,
current arc title/stake/GM context, at most 12 reference summaries, and the 5 already-bounded recent
milestones. Cap every list item at 500 characters and the serialized result at 8,000 UTF-8 bytes;
overflow blocks with `STORY_CONTEXT_TOO_LARGE` instead of truncating individual facts. Omit
`TrustBoundary`, event IDs, component JSON, and reference visibility metadata from the public
result. Entity IDs from references are retained only in the private step receipt and are never
placed in `ContextSummaries`.

### Idempotency and leases

- New token creates run and steps atomically. Equivalent canonical JSON replay returns the run;
  different content returns `STORY_REQUEST_TOKEN_CONFLICT` unchanged.
- Random process lease owner; two-minute lease; conditional claim against expected revision/expiry.
- A claimed step has an eight-minute overall deadline. Renew its lease conditionally every 30
  seconds using a fresh store scope and immediately before any local-model call or action commit.
  Renewal requires the same owner, running step index, and nonterminal plan. Losing renewal cancels
  the in-flight call; the action participant also checks the lease condition inside its transaction,
  so a stale worker cannot commit game state.
- Bounded in-memory wake channel (256) is only an accelerator; SQLite is authoritative.
- On startup and every second while idle, scan pending and expired-running plans.
- One DI scope processes one step. Never hold a DbContext across steps or model calls.

## Action atomicity and crash safety

Eliminate the “action committed, story receipt absent” crash window. In Slice 6, extract an internal
`ActionExecutionCore.ExecuteAsync` from `ActionRunner`. It accepts a DataAccess-only
`IActionCommitParticipant`, never a caller callback or MCP field.

- Normal `IActionRunner.RunAsync` uses no participant and remains compatible.
- `StoryPlanActionExecutor` supplies a participant which updates the running step, stores bounded
  action result/operation ID, advances the plan, and finalizes the handoff if terminal after
  effects/events/success audit are staged but before commit.
- Participant failure rolls back effects, events, notifications, success audit, and receipt.
- Crash before commit leaves neither action nor receipt; expired lease safely retries.
- Crash after commit leaves both; the worker never reruns it.
- Failed actions roll back normally; persist the failed step afterward and stop the plan.

Cancellation uses optimistic revision ordering. The coordinator sets `CancelRequested` and
increments revision unless the plan is already terminal. The worker checks it before preparation
and again before entering `ActionExecutionCore`. If cancellation commits after action execution
starts but before its participant, the participant's expected-revision update fails and the entire
action rolls back; the worker then marks the plan cancelled. If the action/receipt commit wins,
that completed action remains history and cancellation applies before the next step. A cancellation
that arrives after the last step made the plan terminal is an idempotent no-op returning the
terminal plan. Thus no result can contain effects without a receipt or a receipt without effects.

Do not expose operation-ID reservation, commit participants, or workflow context through
`ActionRequest` or MCP.

## Authorization boundary

At start, resolve `IAuthenticatedCampaignAudiencePolicy` before campaign/plan reads. Version 1
requires a matching `gm` grant; actor grants deny generically.

Background execution is enabled only for `DevelopmentCampaignAudiencePolicy`. Store principal ID
and policy revision as private evidence and recheck the fixed policy before every step. A future
ambient claims policy cannot be replayed by a worker; production must add a provider-owned revocable
background authorization lease before this feature may be shared or published.

## Stable errors

| Code | Outcome |
| --- | --- |
| `INVALID_STORY_PLAN` | Start rejected; no run. |
| `STORY_AUDIENCE_DENIED` | Denied before plan data. |
| `STORY_REQUEST_TOKEN_CONFLICT` | Replay conflict; prior run unchanged. |
| `STORY_PLAN_NOT_FOUND` | Generic missing/denied query. |
| `STORY_CONTEXT_UNAVAILABLE` | Campaign resume missing or unreadable; context step blocked. |
| `STORY_CONTEXT_TOO_LARGE` | Bounded campaign context cannot be represented safely. |
| `STORY_ROUTE_NOT_FOUND` | Action blocked. |
| `STORY_ROUTE_NEEDS_INPUT` | Action blocked with bounded questions. |
| `STORY_PROCEDURE_NOT_FOUND` | Step blocked. |
| `STORY_PROCEDURE_STALE` | Blocked after one reroute. |
| `STORY_PROCEDURE_CONTEXT_TOO_LARGE` | Step blocked. |
| `STORY_PROCEDURE_REJECTED` | Verifier blocked. |
| `STORY_LOCAL_MODEL_UNAVAILABLE` | Blocked; no action. |
| `STORY_KNOWLEDGE_UNAVAILABLE` | Knowledge blocked. |
| `STORY_ACTION_FAILED` | Action failed; nested safe action message retained. |
| `STORY_CANCELLED` | Cancelled between steps. |
| `STORY_STEP_TIMEOUT` | Step blocked; no orphan lease or game-state change. |
| `STORY_INTERNAL_FAILURE` | Failed with generic public message. |

Authorized `KNOWLEDGE_NOT_FOUND` completes a knowledge step as unknown;
`KNOWLEDGE_UNAVAILABLE` blocks it.

## Terra delivery slices

Implement one slice at a time. Do not create later-slice artifacts early.

### Slice 0 — semantic confirmation and fixture (documents only)

Create `STORY_PLAN_ORCHESTRATION-SLICE-0-CONFIRMATION.md`. Confirm permanent procedure ID
`procedure.play.story-plan`; query/commit kind `story-plan`; three step kinds/limits; partial
completion; result/errors; development-GM-only; no resume; and one fixture: observatory knowledge
context, observatory knowledge, then the existing observatory-rumour confirmation action specified
under Acceptance fixture.

No C#, catalog, manifest, database, or live-state change.

Exit: one user approval authorizes Slices 1–8 unless an invariant proves impossible.

### Slice 1 — core models and pure validator

Add `DantesRoleplay/Story/StoryPlans.cs`, status constants, `IStoryPlanCoordinator` (`StartAsync`,
`CancelAsync`, `GetAsync`), `StoryPlanJsonParser`, and a data-access-free validator. The parser
enumerates object properties to reject duplicates/unknowns, checks JSON value kinds, applies only
the two documented optional defaults, and then constructs the records; both MCP and direct tests
use this one parser.

Test every boundary, unknown/additional transport fields, duplicate IDs, invalid kind/roles/input,
context position/count, action cap, byte cap, and forbidden request fields by reflection/schema
assertion.

No DI, DB, Ollama, MCP, catalog, or migration.

Exit: closed contracts compile and validation changes no state.

### Slice 2 — procedure-bound action preparation (read-only)

Add `IProcedureBoundActionVerifier`, DataAccess implementation, allowed Qwen task, and internal
`StoryActionStepPreparer` composing route proposal, full procedures, truthful audit reads,
verification, and freshness checks. Do not call `IActionRunner` or persist plans.

Test exact/ambiguous/no route, needs input, 0/1/3/4 procedures, inactive/wrong-governs, prompt
injection, invented output, unavailable/malformed Qwen, mechanic/procedure change, retry-once,
content cap, and zero world writes.

Exit: one intent yields one unchanged verified proposal/evidence or bounded blocked result.

### Slice 3 — durable store and migration

Add exact EF models/tables/indexes, one migration, `IStoryPlanStore`, and `StoryPlanStore`.

Test create/read, replay/conflict, revision conflict, ordering/status, lease expiry/reclaim, cancel,
terminal immutability, lease renewal/loss, bounded JSON, FK behavior, and fresh migration.

No worker, execution, MCP, catalog, or persistent import.

Exit: durable plans can be claimed but not executed.

### Slice 4 — start/query/cancel coordinator

Implement policy-first `StoryPlanCoordinator`, store composition, bounded revision waiting, and
wake queue. Register real service only for SQLite plus development policy; otherwise a safe
unavailable placeholder.

Test policy-before-store, GM, actor denial, wrong campaign, unavailable policy, replay/conflict,
cancel races and precedence, wait timeout/change, missing-vs-denied, and zero game-state writes.

No worker/MCP.

Exit: application services manage a durable inert plan.

### Slice 5 — serial worker and read steps

Add durable-scan `StoryPlanWorker` and channel. Process one `campaign-context` or `knowledge` step
per scope, retrieve/record its fixed governing procedure, persist safe bounded results, recheck
policy before read/persist, and re-enqueue. Action temporarily blocks internally; still no public
route.

Test serial order/no concurrent model calls, exact campaign ID, missing/oversized context, context
field/list caps, unknown knowledge completed, unavailable knowledge blocked, revocation,
cancellation, overall deadline, lease renewal/loss/recovery, restart scan, exact result mapping,
read-only handoff, and no hidden ID leakage.

Exit: context/knowledge-only plans complete one step at a time.

### Slice 6 — composable ActionRunner receipt

Extract the internal execution core/participant and add `StoryPlanActionExecutor`; worker does not
call it yet. All existing `ActionRunnerTests` must remain unchanged.

Add tests for participant success/failure rollback, crash-before-commit, effect/event/notification/
audit/receipt atomicity, normal parity, cancellation, guard rejection, and seeded replay.

Exit: an action and story receipt can commit atomically.

### Slice 7 — action steps and final handoff

Wire claim -> prepare -> cancellation check -> execute -> stop/continue -> terminal handoff.

Test context→knowledge→action, knowledge→action, action→action, action→knowledge,
block/failure at every index, preserved earlier state, skipped later steps, no duplicate after
lease/restart, bounded/distinct handoff, only storytelling procedure exposed, and no internal
payload leakage.

Exit: semantic service completes all three step kinds without a public route.

### Slice 8 — MCP/catalog integration and acceptance

After approved Slice 0 IDs:

- add `story-plan` to `VerbSurface.QueryKinds`/`CommitKinds` and exact dispatch;
- add thin `StoryPlanTools`;
- add `catalog/procedures/play/procedure.play.story-plan.md` plus manifest;
- update `procedure.system.use`, descriptions, DI/startup, and development guidance;
- keep exactly three MCP tools.

Transport behavior is fixed:

- add optional public `afterRevision` and `waitSeconds` arguments to `QueryTool`; only
  `kind: "story-plan"` accepts them, requires `id`, and rejects every unrelated query filter;
- `CommitTool` dispatches the exact parsed `start` or `cancel` operation and rejects `dryRun: true`;
- `proceduresUsed` remains the normal outer commit evidence and should contain
  `procedure.play.story-plan`; it is not copied into the stored plan or any step;
- use normal `ToolRunner` query/commit audit envelopes with subject equal to the story-plan ID;
  internal step procedure reads are separate truthful `query` audit rows;
- capabilities advertises both exact commit payloads, the fixed limits, and the one query shape;
- add coordinator dependencies as optional tail-injected services so a disabled/unavailable feature
  fails safely without changing the existing three-tool schema beyond the approved arguments/kinds.

Start returns a literal next call:

```text
query(kind: "story-plan", id: "story-plan....", afterRevision: 1, waitSeconds: 20)
```

Every nonterminal query returns the same next call using its returned revision. Every terminal query
returns `query(kind: "procedures", id: "procedure.play.storytelling")`; blocked/failed/cancelled
plans still have a terminal handoff describing completed history and unresolved work. `cancel`
returns the current result and the appropriate one of those two next calls.

Acceptance: fresh import; orient/read/start/wait/read-storytelling protocol walk; replay with no
second action; disabled audience denial; deterministic CI fakes plus opt-in live Qwen; catalog
validation/full suite; and `STORY_PLAN_ORCHESTRATION-RECEIPT.md`.

Exit: the remote story LLM can submit a bounded plan, let the backend process it serially, and
receive a compact result for narration.

## Acceptance fixture

Use the existing `CampaignFeature3Tests` sealed-observatory blueprint and continuity seed unchanged;
extract a shared test-fixture helper only if doing so does not change those tests. The fresh database
setup must import the catalog, create `campaign.test.sealed-observatory`, and initialize its current
chapter/arc before starting the story plan. The plan request is fixed:

```json
{
  "operation": "start",
  "requestToken": "story-plan.acceptance-01",
  "campaignId": "campaign.test.sealed-observatory",
  "objective": "Investigate the observatory signal and act on what is known.",
  "steps": [
    {
      "id": "campaign-context",
      "kind": "campaign-context",
      "intent": "Recall the current sealed-observatory campaign context."
    },
    {
      "id": "known-signal",
      "kind": "knowledge",
      "intent": "What is known about the observatory signal?"
    },
    {
      "id": "confirm-signal",
      "kind": "action",
      "intent": "confirm the observatory signal",
      "roleEntityIds": {
        "rumour": "rumour.feature-04.observatory-signal",
        "world": "world.feature-01.fixture"
      },
      "input": "{}"
    }
  ]
}
```

Expected action selection is
`mechanic.game.core.world.rumour.confirm` with
`procedure.game.core.world.knowledge`. Expected action narration is exactly
`The Observatory Signal is confirmed.` and the final rumour status is `confirmed`. Deterministic
acceptance fakes return those supplied IDs; the opt-in live-Qwen test must reach the same validated
selection but is not a CI gate.

Fixed semantic scenario:

1. Objective: “Investigate the observatory signal and act on what is known.”
2. Campaign-context step reads the current chapter/arc context for that objective.
3. Knowledge step asks what is known about the observatory signal.
4. Action step confirms the signal using the exact rumour/world role IDs above.
5. Backend retrieves procedures, processes serially, and returns context, facts, plus mechanic
   narration.
6. Remote retrieves `procedure.play.storytelling` and narrates only the handoff.

Do not substitute another campaign, mechanic, procedure, role ID, or intent. If this exact fixture
stops satisfying an existing invariant before implementation reaches Slice 8, stop at that semantic
boundary and amend/approve the plan rather than silently changing the acceptance behavior.

## Full test matrix

- all request/result limits and closed serialization;
- replay/revision/lease/restart behavior;
- GM-only policy and denial before reads;
- procedure governs/content/freshness/read evidence;
- route ambiguity/missing roles/Qwen failures;
- campaign context missing/oversized/bounded and knowledge unknown/hidden/unavailable;
- serial worker and channel-overflow DB fallback;
- action receipt atomicity across effects/events/notifications/audit;
- partial completion/no compensation/no duplicate action;
- bounded handoff and internal-data exclusions;
- disabling feature preserves ordinary knowledge/routing/action/MCP behavior;
- catalog, migration, full-suite, and protocol evidence.

## Non-goals and amendment triggers

Version 1 does not add remote-model credentials, production authentication, actor execution,
automatic plan generation, branches, loops, retries, resume, bindings, parallelism, generic reads,
raw effects, arbitrary commits, registered workflows, session/quest/campaign transitions, transcript
storage, or automatic narration persistence.

Reusable atomic sequences belong to `EXECUTABLE_WORKFLOW_PLAN.md`; procedure vectors/aliases to
`PROCEDURE_SEMANTIC_RETRIEVAL_PLAN.md`; player/public access to the real authorization owner;
session activity to S6; stored narrative to S7; remote collaboration to S9.

## Completion rule

The feature is complete only after Slice 8. Production authentication remains a separately named
deployment gap and must not be hidden by local-development acceptance.

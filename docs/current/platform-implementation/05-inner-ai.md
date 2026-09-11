# Focused INNER AI workers

Status: independent preparation and result-adapter implementation, 2026-09-11. Full worker execution remains unavailable pending accepted runtime, grant, context-profile and durable-lifecycle integration from plans 01–04. Initial external integration remains Codex and the website, with runtime JavaScript as another caller.

Prerequisite: implement [00 — Shared foundation](00-shared-foundation.md) first and have the coordinator supply its accepted foundation revision and contract baseline. This workstream consumes those shared contracts and does not redefine them independently.

## Implemented boundary and integration requirements

`SystemInnerWorkerPreparation` in `system-capabilities/hosting` resolves and rechecks the selected
active procedure through `IProcedureStore`, requests fresh context through the existing
`IInteractionTaskContextMaterializer`, and selects only explicitly required references from the
bounded version-2 packet. It rejects stale procedures, scope mismatch, missing context, expired or
exhausted operation budgets, atomic work, malformed packets, and output-contract mismatch. The
combined assignment/context JSON is capped at 64 KiB; the source context packet keeps its existing
32 KiB/64-item bound. Host profile identity and model configuration remain separate from assignment
data; procedure instructions replace prior profile instructions. Tool names are an explicit host
allowlist. These internal preparation types are not a new public profile/authority contract.

Preparation does not authorize standing grants, consume operation allowance, execute AI, resolve
dependencies, or persist evidence. Its `PromptBytes` measures the assignment/context message only;
it excludes the AI runner's system prompt, tool schemas and provider overhead. Existing per-request
tool-round/output limits are preserved, but are not an aggregate token or descendant budget.

`SystemInnerWorkerResultAdapter.MapStoredResult` is an internal readback projection for a terminal
AI response already persisted by the lifecycle owner. It validates the command identity and typed
output, requires an existing result-evidence reference, and returns compact computation data with
the task handle, procedure/output-contract identity and summary. Earlier authoritative commits stay
in the shared result envelope, using the coordinator's accepted additive result contract. Detailed
activity remains in the existing AI response/task record. Model text and tool-success activity are
never commit evidence. Unresolved operation identity produces a reconciliation-required failure;
this mapper neither checks leases nor proves that the supplied evidence was persisted.
The lifecycle owner separately verifies that supplied prior commits/recovery identity belong to
the current invocation. A foreign candidate result is rejected while those verified current
receipts remain visible; unresolved evidence ownership requires reconciliation without disclosure.

`AiService.SendAgentRequestAsync` restricts agent tools to the host-materialized instances, including
when a static tool has the same name. Direct requests retain their explicitly selected static tools.
Failed provider rounds, tool-round exhaustion and invalid structured output retain measured tokens
and earlier observed tool calls. These fields describe runner activity, not committed effects.

The additive `IAiService.SendAgentRequestAsync` overload requires an `IAiInvocationLifecycle` supplied
by the durable host. Existing callers retain the original overload; implementations without lifecycle
support fail unavailable on the new one. The runner admits every provider round and every validated
selected-tool invocation, then records its return, throw, cancellation or admitted-but-not-started
outcome in `finally` before proceeding. Dynamic callbacks and returned tool calls share the same
tool boundary. Provider settlement excludes separately reserved tool executions. Host-only descriptors
copy mutable payloads, clear provider executors, and reject JSON; concrete scope implementations must
also reject JSON and keep authority private. Plan 04 owns committed admission, fresh authority/fences,
and bounded outcome persistence independent of the cancelled worker token. No such adapter or ledger
is supplied by the runner.

`AiResponse.ToolResults` is populated only on the required-lifecycle path and retains actual returned
tool data even if outcome recording fails. It does not establish world-commit authority. The serialized
result collection shares the request's configured byte allowance. If a result exceeds that allowance,
the runner keeps a small `AI_TOOL_RESULT_UNAVAILABLE` diagnostic with bounded dispatch identity, marks
the actual output unavailable, and stops for reconciliation; it fabricates no receipt and does not
silently truncate the result. These fixed-size diagnostics are separate from the retained payload
allowance and remain bounded by the tool-call limit. Already admitted parallel calls may still return
evidence. Failed persistence stops new work and preserves evidence from actions that already returned;
late billing evidence never restores a stale worker's permission to publish a result or effect.

Both worker adapters remain internal and unregistered. `ISystemInnerWorkerService` still resolves
to the foundation's unavailable service. No worker can submit, self-confirm, or execute through
these helpers. Real provider execution, crash/reconnect recovery, linked cancellation, expired-lease
publication, dependency ordering, script/OUTER follow-up and cost comparisons are not established
by their deterministic adapter tests.

Coordinator proposals (not adopted shared contracts):

- Freeze the host-owned procedure profile alongside `SystemInnerWorkerRequest`: exact procedure
  revision/fingerprint, existing `AiAgentProfile` identity, allowed operation/tool references,
  required context references, output schema/fingerprint, and narrowed budgets. Plans 02/03 must
  resolve this from current authorized definitions; assignment JSON must not populate authority.
  Existing consumers are the runtime gateway, context materializer and future INNER executor.
- Add the plan-04 execution/readback binding using `SystemTaskAttemptIdentity`,
  `SystemTaskSelectedDefinition`, `SystemTaskDurableHandle` and `InteractionInvocationHost`.
  The lifecycle owner must reserve shared allowances across descendants/retries, reauthorize at
  execution and tool invocation, validate procedure freshness, and fence persistence/notification.
  Bind the result evidence to principal, application/state scope, grant revision, stable command,
  attempt/fencing counter, selected procedure/schema fingerprints and the actual provider response.
  `SystemInnerWorkerResultAdapter` checks only the command and output portion of this binding;
  it must not be exposed as an evidence-verification service.
  Persist AI input/evidence/results in those task records, without a second history. Current
  `InteractionInvocationBudget` has operations/deadline only; aggregate provider-token and tool-call
  accounting requires a coordinated budget extension before execution can be enabled.
- Extend the existing durable service for bounded list/wait readback, then expose submit/get/list/
  wait/cancel through `ISystemInnerWorkerService` or the common task gateway. Reuse its existing
  submit/get/cancel identities; list should be scoped to the authorized parent with at most 16
  handles per page. Wait must use named checkpoints and release execution resources. For example,
  a pending response retains `{ "taskId": "task.1", "commandId": "command.1" }`; a completed
  computation cites the existing terminal result record, never the provider conversation ID.
  Coordinator-owned transport and registration connect OUTER/JavaScript; plan 06 owns display.

These are additive integration requests, not new runtime IDs, migrations, wire fields or capability
availability. Adoption requires the coordinator's accepted symbols/revision before this workstream
can wire or test dependent scenarios.

### Concrete profile and AI accounting proposal

The additive proposal is in `system-capabilities/domain/SystemInnerWorkerProfileContracts.cs` and
`SystemInnerWorkerBudgetContracts.cs`, with matching contract fixtures under that owner's `tests/`.
It is not registered or connected to the internal preparation adapter. Coordinator review freezes
the shared contract; plan 04 owns any persistence additions and the coordinator owns migrations.

`SystemInnerWorkerResolvedProfile` wraps the existing `SystemInnerWorkerRequest` and `AiAgentProfile`.
It pins a profile revision, output-schema hash, up to 16 named tool bindings with exact capability
revisions, up to 64 required context references (1,024 characters each), the existing manual packet's
reference/fingerprint, and authority evidence/grant revision/fingerprint. Host scope comes only from
the wrapped invocation. Source-to-profile/tool mapping and current authority must still be verified
by the resolver; constructing the DTO is not permission. Both profile and invocation authority reject
JSON import/export. `IInteractionManualContextService` remains the manual source; no packet is copied.

Worker subjects are closed: `ProcedureWorkflow` retains its exact procedure and real state scope;
`ApplicationCandidateValidation` retains an exact host-only candidate reference, has no executable
procedure, and requires `InteractionInvocationHost.ForApplication` with the read-only profile and
absent state pair. The existing host/procedure constructor remains; the new constructor takes the
subject first. Validation profiles require separate Read and Validate provenance, no tools, a zero
tool-call budget, and the exact immutable `inner.application-candidate-reuse-review` version-1
definition and fields. Profile selection never grants authority or makes the reviewer executable.

The built-in reviewer consumes only plan 03's selected-material V2 input and output schema. Its
model-visible pins describe the authorized selection, not full candidate/base generations; actual
owner evidence must separately establish complete coverage. `SystemInnerWorkerValidationRequestBuilder`
checks input/schema/manual agreement and narrows tool calls and rounds to zero, response bytes to
at most 8,000, and elapsed time to the host deadline. It neither dispatches nor authorizes. Plan 04
must still bound the actual serialized provider descriptor to 64 KiB, including generated system
instructions and escaping, before invocation. These adapters remain unregistered pending the real
selection, authority, durable admission, accounting and result-publication integration.

`SystemInnerWorkerValidationInvoker` consumes the lifecycle callback supplied by plan 04's actual
lease/profile factory and uses only the required-lifecycle AI overload. Its ephemeral computation
retains the actual response, usage and activity even when the V2 parser rejects exact alternative
coverage after schema validation. Callers must check the typed judgment and failure code, not the
runner's success flag alone. The invoker does not retry, persist, authorize or attest; incomplete
owner selection still prevents production admission and provider dispatch.

`SystemInnerWorkerAiBudget` defaults to a measured-stop threshold of 32,768 total provider input/output
tokens including provider overhead, 8 tool dispatches and 2 concurrent provider requests per root. Host ceilings are configurable
up to 131,072 tokens, 16 tools and 4 concurrent requests. Child ceilings only narrow and preserve the
root's token mode. Existing shared
operations/deadline limits remain independent. `SystemInnerWorkerAiReservationRequest` supplies the
trusted host, current task/attempt/fence, stable reservation identity, selected ceiling and reserved
amounts. Its immutable reservation fingerprint includes the mode and concurrency limit. Plan 04
resolves root and every ancestor, checks remaining balances and applies all debits
atomically. A child cannot supply an ancestry list or replace a root balance. Unchanged reservation
redelivery must not charge twice; changed payload conflicts; retries consume new reservations.

Every provider dispatch must reserve a nonzero admission charge. In `hard-cap` mode, dispatch also
requires host-verified total-token upper-bound evidence; otherwise `ProviderBoundFailure()` returns
`INNER_AI_PROVIDER_BOUND_UNAVAILABLE`. A maximum-output setting or estimate is insufficient proof.
In the default `measured-stop` mode, the hold is bounded by the remaining threshold after known charges
and outstanding holds. It is an admission charge, not a guaranteed usage bound. Already admitted
in-flight requests may overrun; actual usage is retained without clamping. No new call is admitted
when known charges/holds exhaust the threshold, usage is unknown, or configured concurrency is full.
The pure `ProviderReservationAmount` calculation does not reserve anything: plan 04 must apply it
atomically across the root and every ancestor. Independent root concurrency remains host policy.
Tool-only reservations cannot authorize provider calls. Their zero-provider-token amount exempts only
provider-bound evidence, not common admission checks: every ancestor must remain below its provider
threshold after charged usage and outstanding holds, and the applicable independent tool allowance
must remain available, before any new provider or tool dispatch. Evidence recording, settlement,
valid terminal publication, cleanup, cancellation and readback are lifecycle actions rather than new
AI dispatches; ordinary authority and fencing still apply to them.

Local inspection of Codex CLI 0.153.4's generated app-server schema established the implemented usage
and terminal field names. The existing repository pin remains 0.149.1. `CodexAiClient` always starts a
fresh thread and retains the latest matching, monotonic cumulative usage snapshot; repeated snapshots
are not summed. Foreign usage is ignored. Missing, malformed or regressing evidence cannot establish
complete usage. Completion additionally requires matching terminal thread/turn identities. A failed,
cancelled or interrupted request retains credible partial usage with `IsComplete=false`; legacy zero
counters cannot establish known usage. The existing event stream ends at terminal: accounting evidence
arriving only afterward is unavailable. Live acceptance must verify provider event ordering and totals.

There is no maximum-output-token parameter in the inspected per-turn schema, and the adapter does not
claim to enforce remote output tokens or a hard total cap. Host request settings instead cap received
UTF-8 delta/reply bytes (262,144 default, 1,048,576 maximum), total tool callback attempts (8 default,
16 maximum) and cooperative elapsed time (10 minutes default/maximum, narrowed to the owning deadline
by integration). Received text is checked before retention, including repeated final text. The AI
runner applies its tool-attempt limit across provider rounds and concurrent callbacks; the Codex
client independently bounds direct callbacks. Exact duplicate calls remain separate requested
activities even when the compact call list deduplicates them. Authority and durable allowance checks
still belong at each actual tool boundary. Cancellation sends a bounded best-effort interrupt before
session disposal; it does not prove that remote billing stopped. These deterministic tests do not
establish live provider acceptance, persistent accounting or INNER runtime availability.

`SystemInnerWorkerAiReservationEvidence` and `SystemInnerWorkerAiUsageReport` are inert host/owner
data, serialized with `JsonSerializerDefaults.Web` for camelCase. `TotalTokens` preserves independent
provider totals including overhead; input/output components must not replace that total. Only explicit
`IsComplete` with a present total can settle known provider usage. Missing total or incomplete evidence
retains the reservation and any larger credible lower bound. Null token fields mean unknown,
including provider failures where zero usage cannot be proved. Their source must be verified against
the original provider response/host dispatch record. Public task readback must not expose lease tokens.
`SystemInnerWorkerAiUsageReconciliation.Calculate` supplies only bounded arithmetic: known usage
charges actual counts and releases the unused difference; unknown usage retains the entire reservation
or a larger observed lower bound, releases nothing and requires explicit reconciliation. Actual
overruns are retained without clamping. `INNER_AI_USAGE_UNKNOWN` retains the admission hold and blocks
automatic dispatch until reviewed reconciliation. A complete `INNER_AI_RESERVATION_EXCEEDED` outcome
settles actual usage and releases in-flight ownership; it does not itself require evidence
reconciliation or prevent an otherwise valid terminal result, cleanup, cancellation or readback.
Remaining ancestor balances govern further dispatch. Neither outcome implies rollback of a committed
tool action, and terminal publication still requires current ownership and authoritative evidence.

`ISystemInnerWorkerAiBudgetAccounting` is an unimplemented proposal for plan 04's existing persisted
task owner. It must settle once across every ancestor, handle conflicting reports, preserve unknown
debits after cancellation, and account for late usage without granting stale workers publication
rights. Contract fixtures prove shapes and arithmetic only, not persistence, concurrent sibling
reservations, crash recovery, provider guarantees or live-grant enforcement. Consumers are the
host profile resolver, plan 03's manual service, plan 05's eventual invoker, plan 04's lifecycle and
accounting implementation, and plan 01/coordinator gateway integration. No runtime activation follows
from these DTOs or their fixtures.

## Outcome and existing owners

OUTER Codex coordinates intent and outcomes while focused INNER workers perform bounded repetitive work with only the procedure, context, and tools needed for their assignment. JavaScript can submit the same worker requests. Several independent assignments can run concurrently; dependent assignments wait for accepted prerequisite results.

Reuse [AiService](../../../DantesRoleplay.LocalAI/Services/AiService.cs), [CodexAiProvider](../../../DantesRoleplay.LocalAI/Providers/CodexAiProvider.cs), [SystemAiAgentService](../../../src/system/system-capabilities/hosting/SystemAiAgentService.cs), [interaction context](../../../src/system/interaction-orchestration/hosting/InteractionTaskContextMaterializer.cs), and [system tasks](../../../src/system/system-task-orchestration/domain/SystemTaskContracts.cs). The current AI runner already supports host profiles, selected tools, response schemas, and call/token bounds. Interaction receipts already carry parent delegation identity. Web inner/outer profiles and continued-subtask requests do not yet provide the desired child-worker lifecycle.

Use these existing owners rather than another provider implementation, parallel agent framework, or replacement project. Existing providers remain compatible; this work adds no new provider or external service integration.

## Proposed contract and storage

A worker submission carries the shared principal/scope/grant reference, selected procedure revision/fingerprint, operation and parent identities, budgets, intent, bounded input/context references, result schema, and dependency handles. The host selects and validates the procedure, narrows tools to its permitted needs, and materializes fresh authorized context. OUTER supplies the assignment, not a self-authorized system prompt or expanded grant.

Return a durable task handle immediately. Final results contain outcome, structured data, commit evidence, and the task handle, plus a concise summary and unresolved inputs where applicable. Detailed tool activity remains in the child record and is available through bounded readback. Results may reference existing artifacts instead of copying large payloads.

Plan 04 owns the task lifecycle, parent/dependency links, leases, waiting, cancellation propagation, cycle/fan-out checks, and retries. This workstream attaches AI-specific procedure/context evidence, output-contract identity, prerequisite-result mappings, and result references to those existing task/interaction records. It must not implement parallel task-state or dependency storage. Do not create a second execution history. The coordinator reserves shared schemas, registration, wiring, and migrations.

## Deliverable slices

1. **Define worker procedures and context selection.** With plans 02 and 03, define the runtime-owned procedure profile: purpose, allowed operations, required context, output contract, and budgets. Reuse capability discovery, exact contracts, and plan 01's declared object/query reads. Start with explicitly selected procedures and progressive expansion when context is missing. Acceptance: equivalent assignments receive bounded relevant context without the entire catalog or parent transcript; missing inputs become explicit outcomes. Recovery: retain the selected procedure and context references so an unsuccessful run can be explained or resubmitted deliberately.

2. **Build the focused invocation adapter.** Adapt the procedure into the existing AI profile/request and authorized tool list. Route execution through SystemAiAgentService and the existing Codex provider/bridge. Enforce output validation and aggregate budgets across tool rounds, delegated descendants, and retries. Current approval-gate restrictions must be reconciled with plan 02's standing permissions at the ordinary invocation boundary; workers cannot confirm themselves. Acceptance: a permitted assignment reads and acts through existing tools, while undeclared tools, broader scope, or unsupported output fail explicitly. Provider failures remain recorded outcomes.

3. **Connect durable submission and result readback.** Use plan 04's queue and task handles. The coordinator integrates MCP adapters for OUTER Codex, plan 01 exposes JavaScript calls, and plan 03 supplies discoverable operating instructions. Persist accepted input before execution and terminal structured results before notifying the parent. Parent readback returns compact summaries by default and expands particular evidence on request. Acceptance: a parent can disconnect, reconnect, inspect the same child, and recover a completed result without repeating its committed action. Unknown completion triggers receipt reconciliation, not optimistic success or automatic duplicate submission.

4. **Connect bounded coordination.** Submit explicit dependencies to plan 04's lifecycle service, which stores links, rejects cycles/excess fan-out, and propagates linked cancellation. Independent children may run concurrently within its shared limits; dependent children start only after the required outcomes exist. This workstream binds structured prerequisite values through declared mappings, then obtains fresh authority and state before AI/action execution. Default to linked children while preserving completed commits. Acceptance: parallel independent work completes without shared-context leakage; failed dependencies block dependent execution; an expired worker cannot publish a result. Recovery and cancellation use the same fenced job ownership as plan 04.

5. **Connect OUTER and the website.** Expose a small submit/get/list/wait/cancel interface through existing direct capability tools and the common gateway. Waiting releases execution resources and resumes from durable state. Plan 06 shows parent/child progress, compact outcomes, and expandable activity. OUTER decides how to combine results or request follow-up work; INNER handles local detail. Acceptance: OUTER submits multiple small assignments, checks progress, consumes a typed result, and requests a follow-up; a script exercises the same path without a browser-specific implementation.

## Authority, cost, and recovery

Worker reports are not state authority. A claimed mutation succeeds only when the owning operation has commit evidence. Check permissions at each sensitive invocation and selected definitions at execution; revocation or incompatible changes produce a blocked/stale outcome.

No AI request or wait may occur while holding the ECS writer transaction. Distinguish retrying provider computation from retrying a potentially committed tool call. Propagate the original operation identity for reconciliation and retain already committed steps after cancellation.

Do not promise that delegation saves tokens or latency. Record parent/child context bytes, provider tokens, tool rounds, elapsed time, retries, and successful outcomes; compare a direct execution with the focused path. Keep model/provider selection configurable under host policy.

## Dependencies and rollout

Plans 01-03 establish script calls, authoring/grants, and context/manual delivery; plan 04 supplies durable execution. The coordinator owns shared gateway wiring and MCP transport integration. Context/profile work can begin independently against those agreed contracts. Enable the INNER capability separately and preserve current direct interactions during rollout. Rollback disables new worker submissions while retaining task readback and existing commit evidence.

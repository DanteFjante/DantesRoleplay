# Focused INNER AI workers

Status: concrete implementation plan, 2026-09-11. Documentation only; implementation requires its assigned execution task. Initial integration is the existing Codex provider and website, with runtime JavaScript as another caller.

Prerequisite: implement [00 — Shared foundation](00-shared-foundation.md) first and have the coordinator supply its accepted foundation revision and contract baseline. This workstream consumes those shared contracts and does not redefine them independently.

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

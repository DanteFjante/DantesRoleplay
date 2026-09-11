# Durable JavaScript jobs, schedules, and observers

Status: concrete implementation plan, 2026-09-11. This document authorizes no runtime changes. Initial callers are the website, Codex, and runtime JavaScript; additional external integrations remain future extension seams.

Prerequisite: implement [00 — Shared foundation](00-shared-foundation.md) first and have the coordinator supply its accepted foundation revision and contract baseline. This workstream consumes those shared contracts and does not redefine them independently.

## Outcome and existing owners

A stored script can start durable work, schedule an action, register an observer, inspect progress, and request cancellation within configured permissions. Long computation, AI requests, and waits run outside the SQLite writer transaction.

Reuse [trigger scheduling](../../../src/system/trigger-scheduling/domain/TriggerSchedulingContracts.cs), [administration](../../../src/system/trigger-scheduling/domain/TriggerSchedulingAdministrationContracts.cs), [leased scheduled work](../../../src/system/trigger-scheduling/persistence/SqliteScheduledAiTaskWorkStore.cs), and [effect application](../../../DantesRoleplay.DataAccess/Ecs/ApplicationEcsEffectApplier.cs). Current trigger targets produce notifications; scheduled AI has separate leased execution. These are useful foundations, not an existing general script-job service. Extending their owners preserves established scope, transaction, and recovery behavior without introducing a replacement project.

## Proposed contract and storage

Use the shared invocation envelope from plan 01: principal and scope, grant reference, selected definition revision/fingerprint, root operation and parent identities, and budgets. A job adds its durable handle, input, checkpoint state, deadline, retry policy, and optional dependency handles. Capture the selected definition at submission; running jobs retain it. Recurring schedules may resolve a later active revision only under an explicitly configured compatibility/activation policy.

Results use the shared outcome, data, commit evidence, and task-handle shape. Distinguish committed results from prepared proposals and computation output. Extend the existing [system-task lifecycle owner](../../../src/system/system-task-orchestration/domain/SystemTaskContracts.cs) for logical handles/readback and reuse the leased-work patterns for execution attempts. Proposed durable records cover job state, ordered steps/checkpoints, host-call identities and receipts, dependency edges, and observer/schedule bindings. This workstream solely owns generic parent/dependency links, cycle and fan-out enforcement, lease ownership, and cancellation propagation. Plan 05 supplies AI-specific metadata and prerequisite-result mappings against this lifecycle. Reuse existing operation and activation references instead of duplicating their payloads. The coordinator owns schema naming, shared wiring, and migrations.

Do not serialize a live Jint heap. Plan 01 must provide explicit resumable steps/checkpoints with bounded JSON state. Completed host calls are journaled under stable identities; recovery resumes a declared checkpoint and reconciles pending calls before invoking them again.

## Deliverable slices

1. **Agree on the durable execution boundary.** With plans 01 and 02, specify job submission, readback, cancellation, checkpointing, and permission evaluation. Define queued, running, waiting, terminal, and indeterminate outcomes; cancellation is a durable request that workers acknowledge. Select limits for runtime, queued work, checkpoint bytes, host calls, descendants, and retained evidence. Acceptance: one generic script submits bounded work through the same contract from each supported caller. Rejected submissions leave no executable job.

2. **Implement storage and the leased runner.** Extend the scheduling/work ownership with transactional enqueue, claim/renewal, expired-lease recovery, and fencing tokens. Computation happens after claim transactions close. Record a checkpoint before releasing a job into a durable wait; do not keep a worker thread or database transaction occupied. Acceptance: restart during computation or a wait resumes recorded work, and a superseded worker cannot publish completion. Recovery: retain the last valid checkpoint and expose failed/indeterminate attempts with their original operation identities.

3. **Add action calls and schedule targets.** Use plan 01's common invocation and script service interfaces. Add a job/action target alongside current notification targets, preserving existing schedules. Scheduling writes require configured grants from plan 02. Each firing obtains a stable invocation identity from schedule and occurrence, selected revision, and scope. Acceptance: duplicate delivery produces one committed action, revoked grants block execution, and changed definitions follow the recorded version policy. Recovery: reconcile uncertain commits through their receipts; retrying execution must not manufacture a new operation identity.

4. **Add runtime observer registration.** Register a versioned observer with declared event/component/relationship inputs, a bounded pure predicate, scope, target action/job, and causal budgets. A JavaScript predicate receives admitted data only; it cannot recursively perform service calls while matching. Use existing event and conditional-trigger owners to stage durable matching work, with bounded fan-out and explicit coalescing policy. Acceptance: an authorized state change starts the declared action without application-specific host code, while unrelated changes do not execute it. Observer replacement/disable preserves audit evidence and governs future matches; queued jobs retain their own revision and permission checks.

5. **Close cancellation, retries, and operator recovery.** Expose lifecycle readback through this workstream's services and the coordinator's MCP adapters; plan 03 supplies their discoverable contracts and plan 06 presents progress and cancellation. Stop new steps after cancellation, propagate cancellation to linked child work where declared, and preserve already committed effects. Retry only classified transient failures within limits. A stale input result requires fresh reads and a new planned attempt; an uncertain commit requires receipt reconciliation. Acceptance: dependency failure, cancellation, exhausted retries, and restart yield inspectable outcomes with bounded evidence and no silent duplicate writes.

## Implemented lifecycle boundary

The lifecycle store owns bounded parent/dependency graphs, shared ancestor operation allowances,
fenced attempts, checkpoints and durable waits, classified retries, cancellation propagation, and
host-call journals. Internal staging methods join the caller's SQLite writer transaction; the
caller commits before dispatch. Public submission owns its commit boundary and rechecks current
scope and grants. Submission retains the resolver's exact activation origin; read and cancellation
resolve that retained origin under current authority. Legacy rows without provenance can resolve
only their exact current selection, with no inferred historical origin or lookup fallback.
Origin-bearing workflow rows retain the original bounded canonical admission payload; readback
verifies its hash and immutable identity before using the origin. Legacy admission fingerprints
remain unchanged and cannot acquire historical authority by adding provenance columns later.
Readback exposes bounded diagnostics without returning retained authority,
lease tokens, raw input, or checkpoint state.

The persistence model distinguishes procedure workflows from application-candidate validation.
Validation has an exact candidate reference and no state or executable-definition identity;
its optional causation reference restricts deletion of the existing operation record. AI ceilings
reference the task and its purpose together. These schema boundaries do not enable validation
admission or execution; those require the shared application host, current Read/Validate authority,
candidate receipt rehydration when causation is supplied, and the INNER subject contract.

The internal validation core now retains and verifies its own original admission commitment,
including candidate, reviewer/schema/context, both grant provenance references, and AI budget.
Equivalent command replay precedes a fresh-root allowance transfer; durable children use the
existing persisted ancestor ledger. Validation enrollment must match that original commitment.
Workflow admission and enrollment fingerprints remain unchanged. Readback requires current
application Read; cancellation requires current Read and Validate for every affected candidate.
Neither operation borrows state-workflow permissions or exposes a semantic validation result.

The internal AI lifecycle factory opens a fresh service scope and short transaction for each
admission or observation. It rehydrates the actual task, original proof, enrollment, fenced attempt,
current candidate, and current Read/Validate authority before reserving and recording dispatch.
Dispatch commits before the provider receives its scope. Late accounting uses an independent
bounded scope and cannot restore execution authority. Hard-cap mode remains unavailable without
an owner-supplied total-token bound. The factory and validation service have no production registration.

Production validation submission deliberately returns unavailable before allowance transfer,
enqueue, Pending, or provider dispatch: the actual candidate reader's broader dependency coverage
is incomplete. A narrower pure-runtime closure cannot satisfy this requirement. Even complete
coverage also needs the actual selected-source/manual-context/reviewer binding; a caller-created
profile or V2 DTO is not that proof. Internal staging fixtures establish lifecycle mechanics only,
and tests against the real application/grant owners establish denial and unavailable paths only.

AI accounting uses the same task/attempt history. Host-resolved enrollment and dispatch
reservations debit every persisted ancestor; provider and tool observations are separate,
bounded evidence. Enrolled and per-reservation deadlines are retained independently and cannot
widen. Charged tokens plus outstanding holds gate every new provider/tool admission at each
ancestor. Unknown usage retains its hold and blocks automatic execution. Complete usage
settles actual, unclamped charges and releases provider concurrency, including an overrun; it
does not prevent an otherwise valid terminal result, cancellation, or readback. Late observations
can settle original usage without restoring an expired lease or permitting stale publication.
Dispatch and usage hashes are checked when rehydrated; admission and result readback verify
settlement counters against retained observations before treating them as accounted usage.

These internal owners do not establish a production runtime, provider, schedule, or observer
route. Coordinator registration/migrations and real runtime/grant integration remain separate
acceptance boundaries. Host-call JSON is inert; public completion remains unavailable when
authoritative commit-receipt reconciliation is required.

## Atomic effects versus orchestration

Keep the atomic root/child effect set and bounded event reactions under the owning effect transaction. Current child mechanics are evaluated before that commit; their proposed effects join the root batch. Adding asynchronous capabilities must not let transactional reactions call AI, wait on time, or hold network operations under the writer lock. Instead, stage a job enqueue in the same transaction as the triggering state change; execute it after commit.

A durable script may call multiple independently committed actions. Failure later in the script does not roll back earlier commits. Explain this through per-step commit evidence, and support explicitly authored compensating actions only where meaningful.

## Dependencies and release boundary

Plan 01 supplies resumable script/service contracts; plan 02 supplies registration and standing grants; plan 03 supplies discovery/manual context for these operations. This workstream owns lifecycle/readback services; the coordinator integrates MCP adapters. Plan 05 consumes the job lifecycle for INNER work; plan 06 supplies the website presentation. Coordinate shared changes centrally. Preserve existing notifications and schedules during rollout; enable general job targets separately. Acceptance covers website and Codex workflows only. No phone bridge, new provider, arbitrary external API adapter, or storage-engine replacement is included.

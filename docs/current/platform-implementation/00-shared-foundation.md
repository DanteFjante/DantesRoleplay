# Shared executable foundation

Status: precursor implementation plan. Implement this document before assigning plans 01–06. It
does not create runtime IDs, provider configuration, worker chats, migrations, or a claim that a
durable workflow exists. The product boundary remains the operator-run website and Codex through
the current MCP surface; additional APIs are later work.

## Deliverable and source base

The deliverable is an executable shared foundation in the current owners, not an interfaces-only
success. It supplies normalized invocation and result contracts, default production adapters,
deterministic conformance fixtures, and registration wiring. A supported operation must either
produce a verified read, a committed receipt, a visible proposal, or a truthful `unavailable`/
denial result. Optional services that are not yet implemented return `unavailable` in production;
only test projects may substitute doubles. No adapter may report provider success merely because a
request was accepted locally, and no path may silently commit a proposal.

The coordinator first inventories `git status`, identifies each dirty path's owner and whether it
belongs in the platform base, and identifies live data and configuration without moving or changing
them. Data and secrets must not enter source commits or handoff messages. They must not wholesale
commit the existing checkout. Relevant, reviewable source changes are isolated on an integration
branch; unrelated WIP remains untouched in its original checkout. Establish the selected-source
base, then implement the foundation in reviewable commits. The final accepted foundation SHA,
including the plans and required source changes, is the common starting point for all six worktrees.
Record that SHA, selected/excluded source paths and safe disposable-storage instructions in the
task. If a relevant dirty change cannot safely be separated,
worktree launch is blocked until it can; starting from `HEAD` and silently omitting it is not an
alternative. A worktree gets its own disposable database, blobs/derived-data directory, test
output directory, and ports. It never writes the live database or a shared cache.

## Existing owners

| Concern | Existing contract and implementation owner | Foundation responsibility |
| --- | --- | --- |
| Invocation authority and result | [Interaction authority](../../../src/system/interaction-orchestration/domain/InteractionAuthorityContracts.cs), [action contracts](../../../src/system/application-execution/domain/ApplicationActionExecutionContracts.cs), [action runner](../../../src/system/application-execution/persistence/ApplicationActionRunner.cs), and [effect transaction owner](../../../DantesRoleplay.DataAccess/Ecs/ApplicationEcsEffectApplier.cs) | Normalize trusted invocation input and tagged outcomes around existing execution, effects, receipts, and audit records. |
| Activation and definition change | [Activation contracts](../../../src/system/application-activation/domain/ApplicationActivationContracts.cs) and [activation service](../../../src/system/application-activation/persistence/ApplicationActivationService.cs) | Preserve exact candidate evidence, validate prepared executables before activation, then emit canonical definition-change evidence. Keep it distinct from ordinary object/state-change events. |
| Discovery and manual | [Retrieval contracts](../../../src/system/interaction-orchestration/domain/InteractionFeatureRetrievalContracts.cs), [procedure store](../../../src/system/procedures/persistence/ProcedureStore.cs), and [orientation](../../../DantesRoleplay.MCPServer/Mcp/OrientMcpTool.cs) | Expose activated catalog/manual evidence to discovery. Plan 03 owns context selection and manual semantics, not a runtime gateway. |
| Durable jobs and AI | [System tasks](../../../src/system/system-task-orchestration/domain/SystemTaskContracts.cs), [leased work](../../../src/system/trigger-scheduling/persistence/SqliteScheduledAiTaskWorkStore.cs), and [AI contracts](../../../DantesRoleplay.LocalAI/Contracts/AiContracts.cs) | Define task-handle and `unavailable` behavior. Plan 04 alone owns lifecycle, parents, dependencies, cancellation, attempts, and leases; plan 05 adapts AI context/results. |
| Web | [Publication](../../../DantesRoleplay.Web/Pages/WebPagePublicationService.cs), [content store contract](../../../DantesRoleplay.Web/Storage/IWebPageStore.cs), and [page/stream endpoints](../../../DantesRoleplay.Web/Http/WebInterfaceEndpoints.cs) | Consume normalized reads/results for presentation. Plan 06 owns pages and bindings; it does not become a second execution authority. |

Proposed CLR work stays in these owner families. Extend their existing request/result records and
service interfaces where needed; do not introduce a parallel “platform runtime” type hierarchy or
assign new application, mechanic, task-kind, provider, or runtime IDs in this plan.

## Contract matrix

| Contract | Required semantics | Conformance cases |
| --- | --- | --- |
| Invocation | Host supplies trusted caller principal, application and state scope, `grantRef`, `operationId`, optional `parentId`, immutable selected version, input JSON, execution profile, cancellation/deadline, and a shared budget. Authored input cannot set or widen these fields. | Spoofed principal/scope/grant fails; selected revision and canonical input are retained; child budget cannot exceed parent. |
| Command identity | A stable command ID identifies retry equivalence. Attempt ID and lease identify a particular execution. Reusing the command ID with changed canonical payload is a conflict. | Same payload replay is identifiable; changed payload is conflict; a new attempt is not a new command. |
| Result | Every result has one tag: `completed`, `proposed`, `committed`, `pending`, `failed`, `cancelled`, or `unavailable`; typed data plus receipt references, task handle where applicable, and safe code/message. `completed` covers successful reads/computation/AI output without claiming a new mutation. | Completed read returns data and read evidence; proposed work has no receipt claiming its own commit; committed requires an authoritative receipt; pending requires a durable handle; unavailable has no false provider result. |
| Read and effects | Database reads expose a committed snapshot and tracked versions. Atomic children return explicit proposals and the root makes one commit. Workflow child calls commit independently; later workflow reads are fresh after a commit or wait. | Snapshot cannot see an uncommitted proposal; atomic child cannot commit; workflow receipt remains visible after its child returns. |
| Durable wait | Define a serializable named checkpoint and a correlated completion handler. Do not persist a JavaScript heap or replay arbitrary async continuation/side effects. Plan 04 implements persistence, recovery and lease fencing. | Checkpoint/command/attempt/lease examples satisfy the shared contract; unsupported durable calls return unavailable. Actual crash recovery and duplicate-delivery execution must pass plan 04's real implementation checks. |
| Activation/change | Retain exact candidate bytes and version references; validate a prepared executable before active state changes. On failure the prior active version remains. Emit target/revision/fingerprint, discovery metadata, dependencies and source operation. Derived caches/embeddings are rebuildable, neither authority nor a publication gate. | Invalid candidate preserves active manifest; byte/fingerprint mismatch fails; unavailable embedding does not prevent activation. |

`read-only`, `atomic`, and `workflow` are distinct profiles. `read-only` produces no effects;
`atomic` gathers child proposals and commits only at its root; `workflow` journals independently
committed child calls. This boundary prevents a “pending” task from being mistaken for completed
work and prevents a read-after-write promise from hiding transactional scope.

Failure or cancellation may include explicitly labelled receipts from earlier committed workflow
steps; it cannot claim those changes rolled back. Successful model output is evidenced by the
validated AI-runner result and recorded task outcome, not a fabricated provider receipt. Mutations
performed by model tools still require the owning platform's commit evidence.

Freeze actual CLR symbols, JSON field casing, required/optional/null behavior, error codes, size
limits, and capability availability in the implemented contracts and fixtures. Include success,
denial, conflict, pending and unavailable examples. Missing standing-grant functionality must not
be replaced with an allow-all policy: adapters retain current authorization and reject operations
that require an authority mechanism which is not implemented yet.

## Foundation slices

1. **Freeze the usable base.** Output: classified source inventory, selected-source base commit,
   isolated worktree instructions, and a reproducible disposable-storage launch recipe. Block
   acceptance until the base SHA builds in a clean worktree and no test launch can resolve the
   live database/cache paths.
2. **Add common shapes and fixtures.** Output: extensions in the existing contract owners, a
   reused or minimally extended canonical JSON/identity functions in their existing owner, and table
   driven fixtures for every row above. Block acceptance until adapters pass the shared cases
   applicable to their declared capabilities and rejected trust fields cannot enter through JSON.
   A test double passing a success case does not prove that capability exists in production.
3. **Make the first real invocation adapters.** Output: a runtime-facing normalized read and
   execute adapter over current interaction/action/effect services, with audit/receipt mapping.
   Block acceptance until it executes one supported read and one authorized action against a
   disposable SQLite database, exposes tracked versions, and returns denial/unavailable without
   mutation. This is plan 01's gateway seam.
4. **Harden activation and change propagation.** Output: candidate-byte/version retention,
   prepared-executable validation, prior-active preservation, and a canonical definition-change
   adapter using current activation/object-change/event owners. Block acceptance until failed
   activation leaves the active manifest unchanged and derived-index unavailability is visible
   but non-blocking. Limit this to the existing activation boundary and shared change contract;
   full authoring lifecycle and standing grants remain plan 02. No new unimplemented authoring
   operation may be advertised as available. This is plan 02's handoff seam.
5. **Install truthful durable and AI boundaries.** Output: result/task-handle mapping plus
   production unavailable adapters where the checkpoint service is absent, with test doubles
   restricted to test registration. Block acceptance until no in-memory await is represented as
   durable, command/attempt/lease shape fixtures pass, and no adapter claims AI execution or tool
   mutation without the appropriate runner/commit evidence. Do not require live model calls to
   prove an unavailable boundary. This establishes plan 04/05 seams; it does not implement their
   services or prove their later recovery behavior.
6. **Compose and prove the base.** Output: coordinator-owned dependency registration and transport
   mapping, fixture execution through MCP and web-facing consumers where changed, and an
   concise integration result in the task. Block acceptance until real adapters complete the read
   and committed-action path from slice 3 and expose truthful unavailable/denied outcomes. Run
   focused checks, the required full acceptance checks, and the protocol walk for changed MCP or
   dependency registration. Publish the final accepted foundation commit only after these pass.

Only migrations required by a completed foundation slice are permitted. The coordinator is the
sole migration integration owner: one branch at a time, one generated snapshot update, verified
upgrade from the selected disposable baseline. Workstreams propose schema needs; they do not each
edit snapshots or create competing migrations.

## Launch-ready workstreams

| Plan | May begin independently against foundation contracts | Must wait for integration |
| --- | --- | --- |
| 01 runtime | JavaScript service calls using the shared read/execute adapters, profiles, prepared-code reuse. | Final transport registration and durable/AI calls. |
| 02 authoring | Candidate/grant validation, activation evidence, change signal tests. | Coordinator migration and 06 publication connection. |
| 03 intent/manual | Discovery packets, manual sections, retrieval freshness and fixtures. | Activated-change refresh from 02; it is never the runtime gateway. |
| 04 jobs/events | Lifecycle implementation over task contracts and checkpoint protocol. | Runtime invocation/authority integration; it alone resolves parent/dependency/cancel/lease conflicts. |
| 05 inner AI | AI-specific context/result adapter and compact result mapping with truthful unavailable dependency behavior. | 04 lifecycle and 03 context packet; it never owns task state. |
| 06 website | Page composition/read bindings and truthful pending/unavailable display with test doubles. | 01–05 real adapters and coordinator-owned endpoint/publication wiring. |

Each chat owns its declared files and proposes edits to shared contracts, registrations,
transports, migrations, and snapshots rather than editing them concurrently. Rebase onto the
coordinator's accepted integration commit before handoff. The coordinator serializes shared-file
merges, migration generation, public MCP/HTTP compatibility decisions, and full acceptance.
The foundation chat can retain this integration responsibility; six workstream leads do not
become six competing shared-contract owners. A change request identifies the exact contract,
reason, affected consumers and compatibility impact. The coordinator lands an additive change or
an explicit versioned replacement, supplies a new integration SHA, and affected workstreams adopt
it before relying on it. A worktree isolates file edits, not incompatible API decisions.

## Handoff and staffing

The foundation launch packet contains its accepted SHA, concrete shared symbol/file locations,
fixture paths and commands, capability availability, storage isolation instructions, and an exact
plan/file assignment for each chat. It supplies the implemented agreement, not this conversation
or a request that each chat design the contracts again. Inspect only assigned interfaces and
fixtures; no worker needs all six plans.

Every slice handoff contains: base SHA; exact files changed; contract fixture names and outcomes;
focused commands/results; migration status; production-unavailable paths; dependencies; and a
short blocker decision request. It contains summaries, not raw logs. It must identify commits and
receipts rather than claiming “done.”

The foundation lead is Astra-led: Astra assigns bounded work, fixes contract decisions, reviews
authority/durability/transaction boundaries, and resolves blockers before accepting a handoff.
That is active delivery ownership, beyond passive review. Use Terra at medium reasoning for
routine bounded implementation, Luna at low/medium for narrow searches, documentation and check
summaries, and Sol for difficult transactional/runtime slices. Give each a short task context and
explicit model; do not inherit Astra history. Use at most one helper per lead by default when
capacity permits, in addition to a bounded implementation worker; helpers run focused checks and
return summaries. This does not promise six fully
staffed simultaneous sessions or an exact saving. Product inner AI remains separate from coding
agents.

Copyable foundation kickoff:

> Implement `docs/current/platform-implementation/00-shared-foundation.md` in the existing project.
> Act as foundation and integration lead. Establish the source baseline as the plan describes;
> preserve unrelated work and live data. Use Astra for contract decisions and critical reviews,
> delegating bounded implementation to Terra, Luna or Sol with explicit models and short context.
> Deliver the shared code, real adapters, conformance fixtures and required verification. Keep
> unfinished services explicitly unavailable. Do not implement the six downstream workstreams or
> launch their chats yet. Return the accepted foundation SHA and the six compact launch packets.

Copyable generic six-chat kickoff:

> Lead the assigned plan from its accepted foundation SHA and launch packet. Read that plan and
> its exact owners. Implement independent production behavior against the shared contracts; use
> production `unavailable` adapters for absent dependencies and test doubles only in tests. Astra
> assigns/reviews/resolves blockers; delegate bounded implementation to explicitly selected Terra,
> Luna or Sol agents with short context, and use at most one additional independent helper.
> Do not change shared wiring, transports, migrations, or snapshots directly—propose them to the
> coordinator. Use a disposable database/output/ports and hand back the specified concise package.

# Interaction orchestration Slice 12F implementation — explicit execution and reusable application conversation

Status: **accepted 2026-08-24**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Interaction orchestration Slice 12F](INTERACTION-ORCHESTRATION-DEPENDENCY-PLAN.md#lowest-ready-leaf)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Expose trusted feature discovery and inert planning through the existing three public
verbs, execute an exact confirmed application proposal with current-authority and at-most-once
checks, and let an ordinary application page host a bounded outer conversation without inheriting
control-center authority.  
Exclusions: Recipe persistence/review/promotion; durable player conversations; arbitrary query
contract execution; application event-chain integration; catalog or game content; model-authored
effects; distributed rollback; public remote `/mcp`; changes to the private operator Codex bridge;
and any live-database mutation during development or acceptance.  
Allowed files/areas after confirmation: `src/system/interaction-orchestration`, the smallest exact
application-action additions under `src/system/application-execution` and `src/system/ecs-effects`,
their component manifests/tests, the generic `DantesRoleplay.MCPServer` three-verb adapters and
host composition, the reusable `src/system/web-interface` application surface and tests, minimal
project/guard/protocol-walk fixtures, this document, its receipt, and concise owner-status links.  
Stop point: Stop when the four confirmed `system.*` kinds and the confirmed application web
surface share one authorization/planning/execution service, replay and partial progress are
proved, and a non-control-center fixture hosts the outer element. Do not begin Slice 12G.

## Confirmation package

Confirmed by the user on 2026-08-24. The following decisions are active as one coherent package.

1. **Retain exactly three MCP verbs.** Add these permanent public kinds without adding a tool:
   - `query(kind: "system.feature-search")`
   - `query(kind: "system.interaction-plan")`
   - `query(kind: "system.interaction-receipt")`
   - `commit(kind: "system.interaction-execute")`
   They are `system.*` because discovery, planning, evidence, and execution coordination are
   platform services. Every request still names one registered non-system application. Existing
   kinds and direct `commit(kind: "action")` compatibility remain unchanged.
2. **Closed public request shapes.** `system.feature-search` reads `applicationId`, mutually
   exclusive `query` or exact `id`, and `limit`. `system.interaction-plan` reads `applicationId`
   plus one bounded JSON `request` with operation `resolve` or `submit`, exact state/session
   references, the already accepted closed intent, and an inert proposal draft only for `submit`.
   `system.interaction-receipt` reads `applicationId`, `stateSpaceId`, and exact receipt `id`.
   `system.interaction-execute` accepts the closed payload described below. Unknown properties,
   kinds, applications, cross-application references, and conflicting modes fail closed.
3. **Two phases are mandatory.** Planning may append audit/receipt evidence but never changes
   application state and never implies consent. Execution requires a new request with the exact
   resolution receipt ID, proposal fingerprint, full returned inert proposal body, current
   application/state-space scope, and a distinct idempotency key. Neither a conversation message,
   model response, proposal body, nor receipt fingerprint alone authorizes execution.
4. **A submitted remote proposal uses the common verifier.** An external ChatGPT or other MCP
   client may use feature search/exact inspection and submit its own draft through
   `system.interaction-plan`. The server rehydrates every claimed record into the same inspected
   representation used by Slice 12E and invokes `IInteractionProposalVerifier`; the caller cannot
   submit authorization, effects, results, revisions, source code, or verification truth.
5. **No proposal persistence migration.** A resolved response returns the complete bounded inert
   proposal and fingerprint. Execution resubmits that body. The receipt store exposes a new
   internal execution-authority projection containing only the stored envelope fingerprint,
   principal/application/state-space/revision evidence, status, and proposal fingerprint. The
   executor reconstructs and fingerprints the submitted plan against that evidence and current
   authority. The database still does not store proposal JSON, intent text, prompts, or messages.
6. **Exact application action owner.** Do not call the legacy `IActionRunner`, which searches by
   intent and writes legacy world state. Extend the existing ruleset-neutral
   `application-execution` component with `IApplicationActionRunner`. It composes exact active
   catalog mechanic evaluation, deterministic effective component/relationship mapping, current
   application ECS reads, and the existing atomic `IApplicationEcsEffectApplier`. Orchestration
   selects no mechanic and interprets no effect.
7. **Deterministic mapping and effect conversion.** The application action owner resolves each
   mechanic-local component ID against the current application then its ordered bases, using exact
   latest registered type references and rejecting ambiguity or absence. Already-qualified
   component/relationship IDs must belong to that allowed owner set. It translates only the
   existing generic effect vocabulary, obtains expected entity/component/edge revisions from the
   current state space, and submits one atomic ECS effect batch. Unknown fields/types, missing
   mappings, emitted event records, or changed revisions fail before mutation. Event integration
   remains a separately confirmed later capability.
8. **At-most-once action root.** Add an optional host-owned execution identity to
   `ApplicationEcsEffectBatch`: a deterministic 32-lowercase-hex operation ID and uppercase
   request fingerprint. Direct existing callers omit it and behave unchanged. When present, the
   ECS effect owner records that fingerprint in bounded audit subject evidence in the same
   transaction as the effects. An exact existing operation is an action replay; a mismatched one
   is a conflict. This closes the crash window between a committed action and the later
   interaction receipt without adding a migration or letting orchestration own the ECS
   transaction.
9. **Sequential partial progress.** The executor validates the complete plan first, then runs
   ready action steps in declared order. Each step is an independent existing action/ECS
   transaction unless one authoritative mechanic composition owns the entire atomic change.
   `stopOnFailure` is fixed `true` in this delivery. A failed/stale/cancelled step leaves earlier
   committed steps intact and marks later steps skipped. Add `Replayed` to the internal execution
   step disposition; it is success-equivalent for dependencies and links the original operation.
   Receipts never claim a distributed rollback.
10. **Action-only initial execution.** Public discovery operations are queries, but execution-plan
    `Query` steps remain `unsupported` because the repository has no authoritative application
    query-contract executor. Slice 12F must not pretend that procedure prose is executable. The
    seam may be represented by a fail-closed adapter, but the positive execution path accepts only
    exact active mechanic/action steps. Existing catalog queries remain available before planning.
11. **Basic private-host authorization.** Generic interaction composition remains deny-by-default.
    The application host installs a simple policy that allows `Plan`, `Execute`, and `ReadReceipt`
    only for a freshly verified loopback or signed Tailscale principal whose requested state space
    is currently bound to the requested application. MCP adapters translate the existing private
    operator `Read`/`Modify` decisions into the same opaque principal context. Only opaque hashes
    and authentication method labels enter receipts; no login or personal detail is stored.
12. **Ephemeral application conversations.** Player conversations are deliberately process-local
    for the first delivery: bounded by principal, application, state space, session context, role,
    turn count, byte count, idle lifetime, and total capacity. Restart/expiry returns a typed
    unavailable result. The browser cannot submit an inner role, provider, model, effort, prompt,
    tools, approvals, authorization, or hidden transcript. Durable conversation storage is a
    future separately migrated feature.
13. **Exact web surface.** Confirm these application-scoped routes:
    - `POST /api/applications/{applicationId}/conversations`
    - `GET /api/applications/{applicationId}/conversations/{conversationId}`
    - `POST /api/applications/{applicationId}/conversations/{conversationId}/turns`
    - `POST /api/applications/{applicationId}/conversations/{conversationId}/execute`
    - `GET /components/application-conversation.js`
    Confirm reusable custom element `<application-conversation>`. Its containing page supplies
    only `application-id`, `state-space-id`, and `session-context-id`; the server derives identity,
    scope, role, revisions, provider profile, and consent. The element emits ordinary DOM events
    for proposal, receipt, progress, and error and never calls MCP or control-center routes.
14. **Outer turn and delegation.** A fixed no-tools outer-turn adapter uses the accepted outer
    Luna High profile and a strict schema to choose only `respond`, `delegate`, or `direct-plan`.
    Only the server may create a bounded child delegation. A delegation calls the same Slice 12E
    planner with an inner host context and parent ID; a direct plan calls it with the outer host
    context. Both return through the same verifier and safe receipt projection. Local/provider
    unavailability or `unknown` never executes and may be returned to the outer turn for a bounded
    direct-plan decision.
15. **Narrator-safe result.** After explicit conversation execution, a separate fixed no-tools
    narration step receives only the player message, mechanic-authored safe narration, closed
    execution status, safe step summaries, and authorized receipt references. It never receives
    projections, effects, hidden state, prompts, reasoning, source, paths, credentials, or raw
    operation rows. If narration is unavailable, the server returns a deterministic safe summary
    rather than hiding the execution result.
16. **Permanent internal identifiers.** Confirm task classes
    `system.interaction.outer-turn` and `system.interaction.narration`, schema names
    `interaction_outer_turn_v1` and `interaction_narration_v1`, and fingerprint domains
    `dantes-roleplay/interaction-execution-request/v1` and
    `dantes-roleplay/interaction-execution-step/v1`. They are fixed host contracts, not additional
    MCP kinds.
17. **No recipe/admin surface yet.** Successful execution provides the complete evidence needed by
    Slice 12G, but 12F adds no recipe table, candidate, promotion, learning flag, or administrative
    review route.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Mechanic selection | No D&D rule is selected by C#. | Current application catalog and common proposal verifier | The executor accepts one exact verified mechanic reference only. |
| Roles and input | Requirements and calculation inputs are authoritative contract data. | Catalog mechanic JSON, application projection, sandbox | The action owner resolves declared generic roles and object input; it adds no game fields. |
| Outcomes | JavaScript produces narration, data, effects, and optional events. | Mechanics/Jint | C# may validate and map generic effects; it never recalculates an outcome. Unsupported event output fails safely in this slice. |
| Mutation | Application state is arbitrary JSON under registered schemas. | Application ECS and ECS effects | One existing atomic effect batch owns each committed action. |

No SRD 5.2.1 locator or Foundry dnd5e inspection applies because this slice implements generic
transport, authorization, execution plumbing, and UI. Application-specific persona/rules remain in
application-owned catalog data rather than generic prompts or C#.

## Prerequisite evidence and corrected dependency

- [Slice 12B receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12B-RECEIPT.md) accepts the closed
  authority envelope, profiles, inert proposal, statuses, and explicit consent reference.
- [Slice 12C receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12C-RECEIPT.md) accepts current trusted
  exact/lexical/optional-vector feature retrieval.
- [Slice 12D receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12D-RECEIPT.md) accepts append-only
  resolution/execution receipts, replay identity, and fresh redacted receipt reads.
- [Slice 12E receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12E-RECEIPT.md) accepts the bounded
  local/remote planner and common current-authority proposal verifier, but explicitly states that a
  receipt fingerprint cannot reconstruct or authorize execution.
- [Application-kernel Slice 11J receipt](../application-kernel/receipts/APPLICATION-KERNEL-SLICE-11J-RECEIPT.md)
  accepts exact read-only application mechanic evaluation and atomic application ECS effect
  batches, while explicitly deferring dynamic application writes.
- Current code proves the dependency-tree phrase “existing `IActionRunner` delegation” is
  insufficient: `IActionRunner` performs intent matching against legacy mechanics/world state,
  while `IApplicationMechanicEvaluator` is exact/application-scoped but deliberately read-only.
  The smallest safe correction is the generic application action owner in this document.

## Authoritative state and public input

The host derives the application revision/base order, activation/effective-set fingerprint,
state-space binding revision/fingerprint, principal, role/profile, limits, authorization evidence,
contract content/version/hash, component type references, entity/component/edge revisions, seed,
operation IDs, receipt IDs, and all validation truth.

The caller may supply only the confirmed bounded public fields: application/state/session
references, intent, opaque role-to-entity bindings, object input JSON, a planner preference from the
existing closed set, the inert proposal returned or independently drafted, receipt/proposal
references, and execution idempotency key. The caller may never supply effects, mappings, seeds,
expected ECS revisions, model/profile/tool settings, authorization, operation IDs, result status,
safe-redaction decisions, or recipe/learning instructions.

## Execution algorithm and transaction ownership

1. Parse a closed request and derive a verified principal from the current transport. Re-evaluate
   `Execute` authorization and compare exact principal/application/state-space scope.
2. Load the internal resolution-authority projection. Require a resolved status, exact principal,
   application, state space, stored proposal fingerprint, and distinct execution idempotency key.
3. Rebuild the submitted proposal fingerprint from its stored envelope fingerprint. Rehydrate the
   current application revision, base order, activation, catalog, contract records, component
   types, and state-space binding. Any drift returns `stale` before a step runs.
4. Validate the entire DAG and every exact action contract before mutation. Reject query steps,
   cross-application/system references, uninspected/unknown contracts, event mechanics, invalid
   roles/input, unsupported effects, and ambiguous mappings.
5. For each ready step, derive its deterministic execution fingerprint and operation ID. The
   application action owner evaluates the exact mechanic with a host-generated seed, translates
   generic output using current mapping/revisions, and calls one atomic ECS effect batch.
6. An existing equal operation is `replayed`; a conflict or stale revision fails the step. On the
   first failure, retain committed earlier steps, mark later steps skipped, and stop.
7. Append one execution receipt with succeeded/replayed/failed/skipped steps and operation links.
   Use a non-cancelled bounded cleanup token after any committed step. Equal receipt replay returns
   the original; disagreement is a conflict and never reruns an action.
8. Return only authorized safe summaries and receipt projections. The web conversation may then
   request narrator-safe prose; MCP callers may narrate the same receipt themselves.

The application action owner and ECS effect applier own each action transaction and audit row.
Interaction orchestration owns ordering and its later append-only interaction receipt, not the ECS
transaction. A receipt-write failure after a committed action is recoverable because replay checks
the deterministic operation identity before any second mutation.

## Conversation behavior

1. Create binds a server-generated conversation ID to the verified principal, application,
   state-space binding, session context, and immutable outer role.
2. A player turn appends bounded safe message state, calls the fixed outer-turn adapter, and either
   returns a normal response, creates one correlated inner delegation, or starts a direct outer
   planning attempt. No branch executes.
3. A resolved plan is retained only in the bounded ephemeral conversation and returned to the
   browser with its resolution receipt/fingerprint. The UI shows an explicit confirmation affordance.
4. Execute accepts only that exact pending proposal plus a new execution idempotency key, calls the
   same execution coordinator used by MCP, records the safe receipt result, and clears the pending
   consent after terminal success/failure/replay.
5. Narration uses only authorized safe output. Typed `needs-input`, `unknown`, `unsupported`,
   `unavailable`, `unsafe`, and `stale` remain visible rather than being converted into invented
   success prose.
6. The custom element uses normal fetch calls and DOM events, preserves the containing page, and
   owns no navigation, game state, authentication, model setting, or durable transcript.

## Failure, replay, and no-change contract

| Failure | Result | No-change/recovery evidence |
| --- | --- | --- |
| Unverified/denied/mismatched principal, app, state space, or session | `unsafe`/403 | No planning, evaluation, or state write; safe receipt when an envelope exists. |
| Unknown/stale receipt or proposal mismatch | `stale`/409 | No action call; current receipt/application evidence returned safely. |
| Forged proposal/contract/version/hash/role/input | `unsafe` or `stale` | Common verifier/current hydration rejects before evaluation. |
| Unsupported query/event mechanic/effect/mapping | `unsupported` | No ECS batch; no model-authored fallback. |
| Evaluation/projection/schema/sandbox failure | failed step | No ECS mutation; action audit and execution receipt link failure safely. |
| ECS optimistic revision failure | stale/failed step | The atomic batch rolls back completely. |
| Equal action retry after receipt-write crash | replayed step | Existing deterministic operation prevents a second mutation; receipt can be reconstructed. |
| Conflicting action or execution idempotency reuse | conflict | No mutation and no replacement receipt. |
| Step 2 fails after step 1 commits | partial | Step 1 operation retained; step 2 failed; remaining steps skipped; no false rollback. |
| Cancellation/timeout before any commit | cancelled/timed out | No mutation; terminal receipt attempt. |
| Cancellation after a commit | partial | Cleanup receipt uses non-cancelled bounded token; replay remains at-most-once. |
| Outer/local/remote/narration provider unavailable | unavailable or deterministic summary | Never implies consent and never hides an already committed receipt. |
| Expired/restarted ephemeral conversation | unavailable/404 | Application state and receipts remain authoritative; no transcript resurrection. |

## Implementation sequence for the coding AI

1. Read `AGENTS.md`, the required reading protocol, the interaction-orchestration agent guide, this
   document, only the prerequisite receipts above, and the named owner contracts/tests. Preserve
   all unrelated dirty files and never open or migrate the normal local database.
2. Confirm this document is `active`. Restate the ruleset-neutral boundary, public/internal IDs,
   cross-owner application-action correction, allowed files, forbidden work, acceptance commands,
   and stop point. If it is not active, perform planning only.
3. Write pure failing tests for public DTO parsing/fingerprints, consent, current rehydration,
   authorization, action replay identity, complete preflight, partial progress, and safe results.
4. Implement `IApplicationActionRunner` under `application-execution`, not orchestration. Extend the
   ECS effect batch with optional trusted replay identity while preserving all existing callers.
   Test exact mapping, every supported effect conversion, optimistic revisions, atomic rollback,
   equal replay, conflict, and unsupported events before wiring the coordinator.
5. Implement the execution-authority read projection and coordinator. Do not store proposal JSON or
   add a migration. Reuse the common proposal verifier/current snapshot owners and reconstruct all
   authoritative references server-side.
6. Add the four public kinds to the single generic verb catalog, dispatcher switches,
   descriptions, examples, tool parameters, authorization adapters, and protocol guards together.
   Keep exactly three verbs and old calls byte/behavior compatible where asserted.
7. Add the bounded ephemeral conversation coordinator and fake providers first. Then add the fixed
   no-tools outer-turn/narration Responses schemas by reusing the isolated transport policy without
   exposing planner or execution callbacks to the model.
8. Add the five confirmed web routes and `<application-conversation>` JavaScript resource. Test it
   from a minimal non-control-center fixture page and prove it cannot call control/operator/MCP
   surfaces or select an inner profile/model/tool policy.
9. Run focused tests while iterating, then the complete interaction/application execution/ECS
   effect/protocol/web/authorization/guard suites, the full shared suite, standalone local-AI suite,
   isolated-output build, protocol walk, architecture searches, and `git diff --check`.
10. Inspect every authored artifact, write the short Slice 12F receipt, update the owner status
    once, and stop. Do not add Slice 12G recipe code even if successful receipts make it convenient.

## Acceptance matrix

| Concern | Required evidence |
| --- | --- |
| Feature discovery | Remote exact/lexical search and inspection return only current trusted records for one app/base set with vectors/local AI disabled. |
| Server planning | Authorized local and remote resolve requests return the same closed proposal/receipt surface. |
| Remote submission | A caller-drafted proposal is rehydrated and passes the same verifier; forged/uninspected/cross-app drafts fail. |
| Explicit consent | Planning changes no application state; only a distinct exact execute request can call an action owner. |
| Exact action | A generic fixture mechanic is evaluated by exact qualified ID/hash and changes only its bound application state space. |
| Mapping/effects | Effective component/base mapping and every supported generic effect translate deterministically; ambiguity, unknown mappings, events, and stale revisions reject. |
| Replay/crash recovery | Equal execute and simulated crash-after-action-before-receipt never repeat effects; conflicting reuse fails. |
| Partial progress | Step 1 commit plus step 2 failure produces committed/failed/skipped evidence and no rollback claim. |
| Atomicity | One mechanic effect batch is atomic; an all-or-nothing requirement is never split across unrelated steps. |
| Authorization/redaction | Fresh plan/execute/read decisions bind principal/app/state; receipts and narration leak no hidden context. |
| Outer delegation | Only an authorized outer conversation creates an inner child with stable parent/app/state/profile limits and safe return summary. |
| Outer direct path | Direct outer planning uses the same search/inspect/verifier/executor/receipt path as delegation. |
| Ephemeral lifecycle | Capacity, bytes, turns, idle expiry, restart/unavailable, resume profile, and pending-consent invalidation are bounded and deterministic. |
| Reusable element | A non-control-center fixture hosts `<application-conversation>` while preserving its shell/context and lacking operator settings, filesystem, raw Codex, direct-inner, or MCP authority. |
| Provider isolation | Browser fields cannot change role/model/effort/prompt/tools/approval; outer-turn and narration requests remain strict no-tools Responses calls. |
| Compatibility | Existing three verbs, direct action, catalog, operator control-center assistant, private Tailscale access, remote `/mcp` denial, and disabled-orchestration hosts still pass. |
| Surface | Capability catalog, both dispatchers, descriptions, examples, guards, and protocol walk agree on exactly four new kinds and still exactly three verbs. |

## Verification commands

- Focused interaction execution, application-action, ECS-effect replay, receipt, authorization,
  public DTO/dispatcher, outer-conversation, web-component, and architecture/guard tests.
- Full shared test suite and standalone `DantesRoleplay.LocalAI.Tests` suite.
- Isolated-output solution build and `git diff --check`.
- Protocol walk because MCP kinds, dispatch, descriptions, examples, and dependency composition
  change.
- Static searches proving no game-specific literal/formula in generic additions, no local-AI
  reverse dependency, no model adapter reference to mutation ports, no raw prompt/reasoning/effects
  in receipts/transcripts, and no browser access to operator/MCP/model configuration.
- No `roleplay validate catalog` unless an accidental catalog change is discovered; this slice may
  not intentionally change catalog artifacts.
- No real provider/network call and no normal live-database initialization or migration.

## Completion receipt and exit gate

Verification is recorded in
[the Slice 12F receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12F-RECEIPT.md), including the delivered public kinds,
application-action ownership, consent/replay/partial-progress evidence, application conversation
isolation, exact test/build/protocol results, and deliberate exclusions. Mark this document and the
12F dependency row accepted only when every applicable acceptance row passes. Slice 12G remains the
next separate Sol-reviewed learning-policy slice.

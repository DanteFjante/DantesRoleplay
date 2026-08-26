# Application-aware workspace Slice E implementation — confirmed system task orchestration

Status: **accepted**  
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 4  
Dependency tree/leaf: [Application-aware workspace](WEB-APPLICATION-AWARE-WORKSPACE-DEPENDENCY-PLAN.md), Slice E  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: let the private operator or an authorized outer AI resolve or submit a bounded system task,
run discovery in read-only planning rounds, inspect an inert exact write plan, explicitly confirm it,
execute each confirmed step through its existing system owner, and receive durable per-step and
aggregate receipts.  
Exclusions: application ECS/game actions, application conversations, arbitrary MCP/tool/HTTP/SQL/
filesystem execution, raw paths, secrets, public hosting, page/settings/Codex/trigger writes,
unregistered capability strings, model-supplied authorization/current fingerprints/request tokens/
effects, automatic confirmation, global rollback claims, system task recipes as executable
authority, vector indexing, normal-database migration, and live page activation.  
Allowed files/areas: one new `src/system/system-task-orchestration` component; generic write seams
and focused read-handler expansion in `system-capabilities`; typed adapters over the existing
registry, source, component-type, preview, activation, dependency, state-space, and legacy-adoption
owners; focused entities/migration and generic host registration; private web adapters and the
existing system-workspace component; governing system procedure, Feature 4 documents, and focused
tests.  
Stop point: disposable migration, focused planning/confirmation/execution/replay/security tests,
catalog validation, protocol walk, build, receipt, and acceptance request complete; stop before
Slice F.

## Decisions awaiting confirmation

The user confirmed this exact boundary on 2026-08-26. The decisions below are therefore active
implementation constraints.

The parent plan already confirms the system-write goal, inert proposals, explicit confirmation,
current-authority revalidation, idempotency, per-step receipts, the initial owner allowlist, and the
permanent `<system-chat>` element. Implementation requires confirmation of these exact artifacts:

- component owner `system-task-orchestration`;
- migration name `SystemTaskOrchestration` and durable task, planning-round, step, confirmation,
  execution, and execution-step records;
- private routes:
  - `GET/POST /api/control/system/conversations/{conversationId}/tasks`;
  - `GET /api/control/system/tasks/{taskId}`;
  - `POST /api/control/system/tasks/{taskId}/confirmations`; and
  - `POST /api/control/system/tasks/{taskId}/executions`;
- task preparation operations `resolve` and `submit`;
- a maximum of 3 model/read planning rounds, 12 total steps, and 8 write steps;
- the existing `modify` authorization capability for confirmation and execution, with
  `control.ai.message` for model planning and `control.read` for retrieval;
- separate five-minute confirmation expiry and sequential per-owner commit boundaries, with no
  cross-step rollback claim;
- registration of the already permanent read IDs `system.sources`,
  `system.application-preview`, and `system.dependencies` in the common capability catalog;
- registration of the already permanent write IDs `system.application.register`,
  `system.source.register`, `system.component-type.register`, `system.application.activate`,
  `system.state-space.create`, `system.state-space.upgrade`, and
  `system.state-space.adopt-legacy` as version-1 write descriptors; and
- extending `<system-chat>` with an explicit **Plan task** path and an exact **Confirm and run**
  action. Ordinary **Ask** remains read-only.

No new `system.*` capability ID or private-operator authorization capability is proposed. The
source-registration descriptor obtains configured allowed-root **IDs only** from a new internal
read-only host seam; canonical paths never enter a descriptor, model prompt, plan, route, or
receipt.

## Prerequisite evidence and owners

- [Slice C receipt](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-C-RECEIPT.md) proves deterministic,
  authorization-first system-capability discovery, exact schema validation, and the accepted
  `system.applications` read descriptor.
- [Slice D receipt](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-D-RECEIPT.md) proves immutable system
  conversation scope, bounded provenance-bearing context, local structured completion, private
  conversation routes, and advisory/application isolation.
- `system-capabilities` owns descriptor compilation and exact dispatch. It does not own the state
  changed by a capability.
- `registry-administration` owns application/source preview, immutable registration, replay, and
  its operation transaction.
- `component-type-administration` owns schema compilation/version registration, dry-run evidence,
  replay, and its operation transaction.
- `application-preview` owns allowed-root-relative scanning and disposable candidate evidence;
  `application-activation` owns exact-preview activation and its transaction.
- `state-space-administration` owns empty state-space creation/upgrade and exact binding history;
  `legacy-state-adoption` owns complete explicit legacy graph adoption. Each retains its own root
  transaction.
- `projection-materialization` owns declared dependency impact. Dependency reads may inform a plan
  but never authorize a schema change.
- `assistant-conversations` and `system-conversations` own conversation identity and read-only
  questions. They do not persist or execute system plans.
- `local-ai` remains a schema-bound string provider without database, web, file, application, or
  capability access.
- `authorization` owns verified principal evidence and current private-host decisions.
- `operations` remains the authoritative audit written by each existing owner.
- Governing catalog procedure `procedure.system.use` owns the dry-run, exact expectation,
  query-back, and system administration operating contract.

## Runtime artifacts

### Capability descriptors and handler seams

The common catalog continues to expose one deterministic descriptor list. Read and write handlers
have distinct interfaces. A read handler retains `ReadAsync`. A write handler declares a semantic
planning input schema, safe output schema, affected owner, required `modify` authority,
confirmation/idempotency requirements, a read-only preflight, and an execute method available only
to the task coordinator and existing trusted host adapters.

Write planning schemas never contain `requestToken`, `expectedFingerprint`,
`expectedSchemaHash`, `expectedActiveFingerprint`, `activeFingerprint`,
`expectedBindingFingerprint`, authorization evidence, dry-run truth, operation IDs, effects, or
receipts. Existing owners or trusted host adapters derive those values from current state. The
registered semantic inputs are:

| Capability | Model/caller semantic input | Existing owner-derived authority |
| --- | --- | --- |
| `system.application.register` | application ID, display name, description, base application IDs | current absence/immutable fingerprint and request token |
| `system.source.register` | application/source/root IDs, relative path or glob, trust, precedence, logical identity | configured root-ID validity, current source fingerprint, request token |
| `system.component-type.register` | application ID, qualified type ID, raw JSON Schema | current schema hash, compiled profile/hash/version, request token |
| `system.application.activate` | application ID | current registration/activation, disposable preview fingerprint, request token |
| `system.state-space.create` | application ID and new state-space ID | current activation fingerprint, expected absence, request token |
| `system.state-space.upgrade` | application ID and state-space ID | current binding/activation fingerprints and empty-state evidence, request token |
| `system.state-space.adopt-legacy` | application/state-space IDs and complete explicit component/relationship mappings | current activation, source-graph evidence, request token |

The already public MCP kinds and payloads remain compatible. Their adapters and the task handlers
invoke the same typed owners; MCP callers retain their existing exact request-token/expectation
contract. There is no generic browser endpoint that executes an arbitrary capability.

`system.sources`, `system.application-preview`, and `system.dependencies` gain catalog read
handlers over their existing owners so the local planner can discover prerequisites in bounded
rounds. `system.application-preview` may scan only already registered allowed-root-relative source
specifications. The local model receives safe returned metadata, never a filesystem path or file
contents.

### Durable task records

Migration `SystemTaskOrchestration` adds generic bounded records for:

- one task request keyed uniquely by principal, system conversation, and planning idempotency key;
- zero to three immutable planning rounds with exact context/model identity, response fingerprint,
  and verified context references;
- ordered canonical task steps assigned by the host as `step-001` through `step-012`;
- an explicit confirmation bound to principal, task ID, and exact plan fingerprint;
- one resumable execution claim bound to the confirmation and execution idempotency key; and
- ordered execution-step receipts containing disposition, owner operation ID when one exists,
  safe typed output and fingerprint, read-back fingerprint, and bounded safe failure evidence.

IDs use host-generated opaque prefixes `system-task.`, `system-task-confirmation.`, and
`system-task-receipt.` followed by 32 lowercase hexadecimal characters. Plan and receipt
fingerprints are uppercase SHA-256 values over normalized closed envelopes. Raw prompts, model
reasoning, credentials, canonical root paths, authorization headers, and unrestricted exception
text are never persisted.

A prepared plan is immutable. Confirmation is a separate append-only record and expires five
minutes after creation. It cannot be refreshed; a later confirmation creates a new record after
current authorization and exact task/fingerprint validation. A confirmation can authorize only
one execution identity. Equal retries replay; conflicting reuse fails inertly.

### Local planning rounds and outer submission

`resolve` calls the configured local structured-completion provider with task class
`control.system.plan-task`, the bounded system context, prior safe read results for this task, and a
closed response schema. One response is either:

- `continue` with one or more exact read-capability steps;
- `prepared` with one or more exact write-capability steps;
- `completed` when the request is fully satisfied by reads; or
- `needs-input`, `unknown`, `unsupported`, or `unavailable` with no executable steps.

The host validates every capability ID, current descriptor fingerprint, semantic input schema,
sensitivity, and evidence reference. For `continue`, it runs only registered read handlers, stores
their bounded outputs and fingerprints, and sends those results into the next round. Writes are
forbidden in a continuation round. After three rounds or twelve aggregate steps, unresolved work
ends safely as `needs-input`; the host never keeps an unbounded agent loop alive.

`submit` accepts a caller-built ordered agenda from an authenticated outer AI or operator. Each
item contains only `capabilityId` and semantic `input`; the host assigns step IDs and applies the
same descriptors, schemas, preflights, bounds, and confirmation gate. All read steps must precede
all write steps. Submitted request tokens, fingerprints, authority, effects, results, receipts,
methods, URLs, paths, SQL, or executable code are rejected.

At most 12 steps, 8 writes, 96 KiB per normalized semantic input, 512 KiB aggregate input, and 1
MiB aggregate retained safe read output are accepted. The local provider's configured response
limit remains authoritative and may be lower. A submitted outer agenda can carry a larger valid
component schema than the local provider can conveniently generate, but it remains within the
existing 64 KiB bounded schema profile.

Fully successful executions become bounded, provenance-bearing guidance examples for later local
planning: at most six token-relevant examples and 16 KiB total may enter a future task prompt. They
are hints only. The current descriptor, schema, authorization, preflight, confirmation, and owner
checks always run again. Failed, partial, stale, denied, and unconfirmed tasks never become
guidance. This provides outer-AI-to-inner-AI learning without making a stored plan executable
authority or introducing a parallel recipe store.

### Private web surface and `<system-chat>`

The nested task route requires an exact accessible `system` conversation. Creation accepts only:

```json
{
  "operation": "resolve | submit",
  "intent": "bounded visible task request",
  "agenda": [{ "capabilityId": "system.*", "input": {} }],
  "idempotencyKey": "bounded opaque key"
}
```

`agenda` is required only for `submit` and forbidden for `resolve`. Confirmation accepts only
`planFingerprint` and an idempotency key. Execution accepts only `confirmationId`, the same exact
`planFingerprint`, and a distinct idempotency key. Task GET/list routes use bounded opaque cursors
and return only the verified principal's system tasks.

`<system-chat>` keeps ordinary **Ask** requests on the accepted read-only conversation routes. A
separate **Plan task** action uses the nested task route, renders the exact safe plan, owner and
capability fingerprints, read evidence, step order, and an explicit warning that earlier steps
remain committed if a later step fails. It never auto-confirms. **Confirm and run** is enabled only
for a prepared write plan; the click creates a server confirmation and then submits its returned
confirmation ID to the execution route. The element renders per-step and aggregate receipts and
emits `system-proposal`, `system-receipt`, `system-progress`, and `system-error` events.

Browser code cannot provide trusted confirmation truth, expected current state, request tokens,
owner operation IDs, provider configuration, or authorization. Disconnect aborts the request but
does not imply rollback; reloading the task returns durable truth.

## Authoritative state and closed input

The trusted host supplies principal, authentication method, private-host scope, correlation IDs,
current authorization evidence, local provider/model identity, time, opaque IDs, owner request
tokens, expected/current fingerprints, descriptor versions/fingerprints, procedure references,
and confirmation expiry. The database supplies conversation scope/ownership, idempotency claims,
planning rounds, immutable plan steps, confirmation, execution progress, and receipts. Each
existing owner supplies its current authoritative state and typed preview/commit/read-back result.

The operator supplies only visible intent and idempotency keys. An outer submitter may additionally
supply the closed semantic agenda. Neither a model nor caller supplies derived current state,
authority, effects, executable handlers, transaction policy, confirmation, or receipt truth.

## Behavior, result, and transaction ownership

### Resolve or submit

1. Authenticate and authorize `control.ai.message`, then verify the exact principal-owned system
   conversation before parsing or storing an agenda.
2. Claim the planning idempotency key and immutable request fingerprint in the task-store
   transaction. Equal terminal replay returns the stored task; conflicting reuse is inert.
3. For `resolve`, materialize current bounded system context and run at most three local planning
   rounds. For `submit`, parse the closed agenda without calling a model.
4. Resolve every exact descriptor and validate current fingerprint, mode, schema, sensitivity,
   ordering, and bounds. Run read steps only and retain their safe typed result fingerprints.
5. Ask each write handler for a read-only preflight. A step is `ready`, `deferred` behind named
   earlier steps, or blocked with a safe code. Preflight never calls an owner commit method.
6. Atomically finalize the immutable plan and fingerprint. A read-only task becomes `completed`;
   a valid write task becomes `prepared`; incomplete or unsupported work has no confirmable plan.

The task store owns planning/round/plan persistence. Capability reads and preflights are read-only.
No registry, activation, component-type, state-space, legacy, page, setting, application ECS, or
external state changes during preparation.

### Confirm

1. Reauthorize current `modify` authority before task lookup.
2. Require exact principal, system scope, `prepared` disposition, plan fingerprint, no conflicting
   terminal execution, and a bounded confirmation idempotency key.
3. Append an immutable confirmation expiring in five minutes. It changes no owner state.

### Execute

1. Reauthorize current `modify` authority before task/confirmation/receipt lookup.
2. Claim or replay the exact execution identity. Require the same principal, unexpired
   confirmation, plan fingerprint, and current descriptor versions/fingerprints.
3. Process writes serially. Before each step, reauthorize its descriptor capability and rerun the
   owner-specific preflight. A `ready` precondition must still match the plan; a deferred
   precondition must be satisfied by earlier successful step receipts.
4. Derive the step request token deterministically from the execution receipt and ordinal. Invoke
   the existing owner's exact dry run, then its commit with the identical derived request. The
   owner retains its own transaction and operation audit.
5. Query/read back through the typed owner reader. Append the step receipt in a separate task-store
   transaction. If the process stops after owner commit but before this receipt, equal execution
   retry reuses the deterministic owner token, obtains the owner's replay, and safely repairs the
   missing step receipt.
6. Stop at the first non-success. Earlier owner commits remain committed and visible as `partial`;
   later steps are `skipped`. Complete the aggregate execution receipt as `succeeded`, `partial`,
   `failed`, `stale`, `unauthorized`, `cancelled`, `timed-out`, or `indeterminate`.

The coordinator owns workflow order and aggregate truth, not a global database transaction. Each
write owner remains the only root transaction owner for its state. The task store owns only task,
confirmation, and receipt transactions. No receipt may claim rollback of a completed earlier step.

## Failure, replay, and rollback contract

| Failure | Required behavior |
| --- | --- |
| Unauthenticated/wrong scope | Deny before conversation/task lookup, model, capability, or owner access. |
| Advisory/application conversation ID | Return not found without revealing the other scope. |
| Unknown/unregistered capability | Reject the plan; never fall back to MCP strings or owner lookup. |
| Extra/injected agenda field | Reject before descriptor/preflight/model/owner access as applicable. |
| Secret capability or output | Exclude from planning and fail closed. |
| Model asks for a write in `continue` | Reject the round; no write or automatic promotion. |
| Round/step/size bound reached | Return `needs-input`; retain only safe task evidence. |
| Equal planning retry | Return/resume the same task; terminal replay makes no second model/read call. |
| Planning idempotency conflict | Return conflict; no model, read, confirmation, or owner mutation. |
| Preflight unavailable/blocked | Task is not confirmable; no owner mutation. |
| Missing/expired/wrong-principal confirmation | Deny execution before owner access. |
| Descriptor or ready-precondition drift | Record `stale`, stop, and require a new plan/confirmation. |
| Deferred prerequisite missing | Stop before that owner call; earlier receipts remain truthful. |
| Owner dry-run failure | Record failed/partial step; never call that owner's commit. |
| Owner commit failure | Rely on the owner's rollback and record safe failure; no later step runs. |
| Cancellation/timeout before a commit | Record cancelled/timed-out; no later step runs. |
| Cancellation after commit before step receipt | Equal retry replays the deterministic owner operation and repairs the receipt. |
| Output/read-back mismatch after commit | Record `indeterminate`, retain operation ID, stop, and never retry blindly. |
| Equal execution retry | Return or resume the same exact execution without double-writing. |
| Conflicting execution key/confirmation reuse | Return conflict before owner access. |

Failed planning may change only task/round records. Confirmation may add only its append-only
record. Execution may additionally change the exact confirmed owners and their existing operation
audit, plus task execution receipts. No application ECS/game action, page, setting, Codex approval,
trigger schedule, catalog source file, source registration path target, or external service changes.

## Implementation sequence

1. Confirm the exact component, migration, routes, descriptor set, bounds, confirmation expiry,
   and partial-commit behavior above.
2. Mark this document active and update the parent dependency leaf.
3. Extend system-capability contracts/catalog for distinct write registration, read-only preflight,
   trusted execution, safe output validation, and duplicate/mode checks without weakening Slice C.
4. Add read handlers for the three existing discovery IDs and write handlers for the seven existing
   administration IDs. Reuse typed owners; do not call MCP adapters from system code.
5. Add the task records/migration and implement authorization-first plan, round, confirmation,
   resumable execution, deterministic owner-token, receipt, and guidance retrieval stores.
6. Add the local resolve coordinator and outer `submit` verifier over one common plan finalizer.
7. Add the private routes and extend `<system-chat>` with separate ask/plan/confirm/receipt states.
8. Update `procedure.system.use` only where necessary to describe task resolve/submit/confirmation
   without changing existing MCP kinds or payload meanings.
9. Add focused positive and negative tests, run disposable migration/catalog/protocol/build checks,
   review the scoped diff, and write the Slice E receipt.

## Acceptance matrix

| Area | Required evidence |
| --- | --- |
| Migration | Fresh and previous-schema databases upgrade atomically; all IDs, dispositions, hashes, sizes, relationships, and uniqueness checks are enforced. |
| Discovery | Existing reads and seven writes compile deterministic current descriptors; duplicates, invalid modes/schemas, secret exposure, and missing handlers fail startup/closed. |
| Resolve rounds | Local planner can request bounded reads, receive only typed safe results, and produce one verified inert write plan without tool/file/database access. |
| Outer submit | A caller-built valid agenda reaches the same verifier; derived authority/injected fields and unknown capabilities are rejected. |
| Learning | Only fully successful exact receipts become bounded future hints; hints never bypass current verification or execution gates. |
| Authorization | Denial precedes conversation/task existence, model/read/preflight/confirmation/owner calls at every phase. |
| Confirmation | Only an exact prepared fingerprint is confirmable; it is principal-bound, separate, append-only, expiring, and non-mutating. |
| Execution | Current descriptors/authority/preconditions are revalidated; every owner receives dry-run then identical commit and read-back. |
| Slicing | Steps run serially as distinct receipted tasks; the first failure stops later work and reports earlier committed work as partial. |
| Replay/recovery | Planning, confirmation, execution, and owner tokens replay exactly; conflict and crash windows cannot double-write or overclaim. |
| Isolation | No application ECS/game action, advisory/application chat, raw path/file content, secret, arbitrary tool, or unregistered `system.*` execution is reachable. |
| Browser | Ask remains read-only; plan is inert; only an explicit confirm click can obtain server confirmation and execute; durable reload matches receipts. |
| Compatibility | Existing MCP administrative kinds/payloads, application orchestration, Slice C reads, Slice D system questions, and local-AI schema-only behavior retain passing contracts. |

## Verification commands

- Focused new `SystemTaskOrchestrationTests` and expanded `SystemCapabilityCatalogTests`.
- Existing registry, component-type, preview, activation, dependency, state-space, and legacy
  adoption owner tests.
- Focused assistant/system/application conversation and web route/DOM/security tests.
- Local-AI structured-completion tests.
- Disposable migration upgrade, EF pending-model, migration-drift, guard, and catalog-coverage
  tests.
- `roleplay validate catalog` after the governing procedure change.
- MCP protocol walk because shared host dependency registration and administrative parity are
  touched, even though existing MCP kinds remain unchanged.
- `dotnet build DantesRoleplay.slnx --no-restore`.
- `git diff --check` over Slice E files.

Normal database migration, external Ollama execution, live page activation, and combined full-suite
browser/live acceptance remain Slice H.

## Completion receipt and exit gate

Write `WEB-APPLICATION-AWARE-WORKSPACE-SLICE-E-RECEIPT.md` with exact descriptors, migration,
authorization order, planning rounds, outer submission, confirmation, partial execution,
replay/recovery, isolation, route/DOM, compatibility, and verification evidence. Mark Slice E
implemented and awaiting user acceptance, then stop before Slice F.

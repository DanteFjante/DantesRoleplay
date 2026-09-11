# Product direction and alignment plan

Updated: 2026-09-11. Status: product discussion and draft plan; no runtime changes authorized by
this document. The user requested that differences and a plan be recorded as the discussion develops.

[PLATFORM-REQUIREMENTS.md](PLATFORM-REQUIREMENTS.md) describes the current implementation.
This document records the desired product, differences from that baseline, and proposed work.
[PLATFORM-IMPLEMENTATION.md](PLATFORM-IMPLEMENTATION.md) coordinates the six separate implementation
plans, their shared contracts, and agent ownership.
Current restrictions are evidence of today's behavior, not automatically requirements for the
desired product. Application-specific functionality and test inventories remain outside scope.

## 1. Direction stated by the user

The product is a runtime-extensible software platform. After the host is compiled, its application
functionality should evolve through stored data, JavaScript, and web content. Humans, AI, and the
system's own background work should be able to use and extend that functionality.

The following points capture the user's direction, rather than newly proposed features:

- The first implementation targets one installation controlled by the user, with invited users
  and permissioned AI. A hosted service for unrelated customers authoring their own applications
  is outside the initial deployment scope.
- The initial client integrations are the website and Codex. Keep the option to connect other
  APIs through generic extension points, but do not implement a specific external device/service
  connector as part of the initial delivery. Inner AI and embeddings remain platform features.
- New application functionality and changes to existing functionality should not require host
  recompilation. Application-specific rules must not be implemented in the compiled kernel.
- The system needs interfaces for reading information, changing information and definitions, and
  invoking actions. The human, AI, and background interfaces should reach the same underlying system.
- AI should receive relevant slices of system context. It should be able to read, write, run
  actions, and author actions and information without first studying the entire runtime catalog.
- An intent such as a description of the desired outcome should let the system find and explain
  how to accomplish it, reducing model input and avoiding unnecessary discovery work.
- The system should also execute actions, read/store information, and add actions itself.
  JavaScript is the preferred executable format because it works well with AI authoring and
  dynamically shaped objects translated from stored information.
- JavaScript mechanics should be able to use system services during execution: dynamically read
  stored data, invoke other actions, integrate with schedules and event/state observers, and
  communicate with or delegate work to the inner AI. Initial snapshots alone are insufficient for
  the intended authoring experience.
- Humans need a website at the root/index and additional served pages. Pages should be composable
  from reusable components, including server composition, and obtain data through an API.
- Application API functionality must be runtime-defined. Fixed generic query, commit, and action
  dispatch endpoints are acceptable; a distinct dynamically registered HTTP route for every
  capability is not a prerequisite.
- Embeddings should help the system retrieve relevant capabilities and information by meaning.
  They support the broader context-selection goal rather than defining the entire solution.
- The system should explain its own use. `orient` is the initial entry point for capabilities and
  operating guidance; procedure contracts form its manual. Intent and embeddings should retrieve
  the relevant instructions as well as the right information or action.
- A lightweight inner AI harness should receive custom context and relevant procedures from the
  system, understand work delegated by the interfacing outer AI, and perform that operation. This
  should spare the outer AI repetitive execution details and unnecessary context.
- The outer AI should be able to delegate multiple smaller tasks to inner workers and coordinate
  their efforts and results.
- Multiple intents should be associable with the same existing capability or reusable task, so
  another useful way of discovering it does not require another implementation.
- Dynamic JavaScript should execute efficiently across repeated calls and changes. Loading active
  definitions once with hot reload, or caching actions, are proposed means to that goal; their cost
  and the best execution strategy have not yet been measured.
- Authoring needs protection against creating multiple implementations because an existing
  mechanic could not be found.
- Background execution should support substantial deterministic logic such as path tracking,
  inventory management, and trading algorithms, without requiring the AI to perform every step.
- An event system should connect changes and activity to further work. Triggers should include
  time schedules and changes in application state such as arrival, location classification, or
  item acquisition. External observations such as an integrated phone receiving a call illustrate
  future connectivity; a phone integration is not an initial requirement.

**Confirmed autonomy decision:** AI and background work may activate newly created or changed
functionality automatically within permissions configured by the user. A human approval for every
change is not the desired default inside those permissions.

This is approval of a product policy. It does not change current runtime grants, authorize external
actions, or authorize implementation of the proposed changes in this discussion.

## 2. Proposed foundation

These are assistant recommendations for realizing the direction; they remain design proposals.

### A small compiled kernel with a broad runtime authoring surface

Keep identity, persistence, transactions, validation, sandbox execution, authorization, job
dispatch, and protocol handling in the kernel. Make application objects, actions, queries, event
handlers, workflows, page compositions, and their component content runtime records.

The practical interpretation of no recompilation is that ordinary application development needs
no new host code. Kernel maintenance and genuinely new native/protocol capabilities can still
require a host release. A phone integration also needs a real device/service bridge; a runtime
rule cannot create access to a device that the host has never been connected to.

Prefer generic integration ingress and permitted service operations so that adding application
behavior over an existing connection does not require a new C# adapter for each condition or action.
The extent of dynamically authored integrations remains to be discussed.

### One action contract with several callers

An action should retain its identity, inputs, outputs, required information, permitted effects,
version, and description whether invoked from a web control, AI tool, another workflow, an event,
or a schedule. Each invocation carries its actual principal and scope.

Use existing mechanic, object, capability-descriptor, and activation owners where possible.
Unification means consistent behavior and discovery; it does not require a new registry that
duplicates every existing record.

### Discover, reuse, compose, then author

The system should turn an intent into a compact, sufficient context packet containing candidate
capabilities, known input bindings, relevant information, and what remains unresolved. Expand exact
contracts and state only as needed. Keep source identities and revisions available to the host
without forcing the model to reason about every internal fingerprint.

Before creating functionality, consider whether an existing action can be called, parameterized,
revised, or composed. Record the nearest existing alternatives and why they do not satisfy the
need. Combine exact/lexical/semantic discovery with structural evidence such as input/output
contracts, role requirements, effects, and dependencies.

An unsuccessful search is not proof that a capability does not exist. Embedding similarity cannot
prove that two programs are equivalent. Demonstrable duplicates can be rejected; uncertain
overlap must remain explicit rather than blocking all legitimate variation.

### A manual supplied by the system, selected by intent

Keep `orient` a small bootstrap explaining the system, the caller's scope, and how to request
relevant instructions. The system should then supply the applicable procedure sections, action
contracts, prerequisites, constraints, examples, and missing inputs for the caller's intent.
Required operating constraints must accompany a selected action even if their wording is not
semantically similar to the request. Expand detail on demand rather than sending the whole manual.

Use existing procedure records as the manual's authority. Index relevant sections from both the
global system manual and application procedures, retaining source and revision references. Bind
the instructions to the executable contract they describe and refresh affected context when that
contract changes. The embedding index selects candidates; the original current contracts determine
how the operation can actually run. Historical instructions remain useful for inspecting past work
but should not silently instruct new work against a changed action.

### Several ways to discover one implementation

Use aliases and match phrases for alternate wording. For a different use of the same behavior,
allow an intent association to describe when it applies, relevant scope, and optional input
bindings or a reference to an existing reusable workflow. For example, route finding and delivery
route planning may reuse the same path action when their actual constraints match.

Several descriptions can point to one canonical action, query, procedure, or reusable task
template. One intent can also have several contextual candidates; preserve that ambiguity until
inputs and contracts resolve it. A task template is distinct from an individual task execution.
Search can index the different uses separately and group results by canonical target, while
preserving which use matched. Another phrase should not create another action body or recipe.

Record where associations came from, maintain them through the authorized authoring path, and
invalidate or revise bindings when the target contract changes. Reuse existing match and recipe
evidence owners before adding a richer association representation. A learned association improves
discovery; it does not prove equivalence or grant permission to execute its target.

### A focused inner worker, coordinated by the outer AI

The outer AI retains the wider conversation and coordinates goals. It can submit a bounded task
with its desired outcome, relevant inputs, and expected result. The system selects the applicable
procedure revisions, current information, permitted tools, and execution budget for an inner
worker. That worker can request further relevant context when needed, within its assigned scope.
It should not need the entire outer conversation or the full runtime catalog.

Reuse the existing AI runner and interaction authority/context pipeline. The separation is one
of responsibility and context; it does not require another model-provider implementation or a
separate operating-system process. Model selection can be configured independently of the role.

Submission should return a durable task identity. The outer AI can submit several tasks, inspect
or await progress/results, cancel work, and supply prerequisites for dependent tasks. Independent
tasks may run concurrently within configured limits. Dependent tasks obtain fresh state after
their prerequisites complete; concurrent writes still use the normal conflict and commit rules.
Reuse existing delegation references and receipts, extending them with the missing child-task
lifecycle, dependencies, cancellation, and aggregate budgets.

Return concise status, structured results, unresolved questions, and references to committed
operations or artifacts. Keep detailed tool exchanges available in task history for optional
inspection. A worker's claim of success is not a substitute for an authoritative operation result.
Authoring and activation by a worker use the configured standing permissions already agreed above.

This aims to reduce the outer AI's repeated context and execution burden. Total tokens and latency
can increase when delegation adds model calls; measure them separately from outer-context savings.
When repetitive work already has a deterministic action or validated reusable workflow, execute
that directly where possible rather than requiring a model to reason through every repetition.

### Configured authority, enforced on every invocation

Represent standing permissions in trusted host policy. Distinguish permission to execute actions,
author definitions, and activate definitions. Scope these permissions to the relevant application,
namespace, records, effects, and supported integration operations as needed.

An authorized workflow can complete without requesting approval at each step. It still validates
contracts and preconditions and records its result. Changed code or changed effects must be checked
against the grant; reusing an allowed action ID must not authorize newly expanded powers. AI and
authored JavaScript cannot broaden their own grants. Permissions should be inspectable and revocable.

### Deterministic execution without a model in every loop

Stored code can execute an already defined algorithm and can generate definitions from templates
or transformation rules. Inventing unspecified new behavior requires an author or an AI authoring
step. Keep that distinction visible so that ordinary scheduled computations need no model call.

Use short transactional actions for bounded changes. For longer work, use a durable job that
computes outside the SQLite writer transaction, retains progress as necessary, then submits
validated effects through the normal commit boundary. An event/trigger transaction should enqueue
such work durably instead of keeping its transaction open while a model or long algorithm runs.

### JavaScript access to system services

The desired service access is now confirmed: mechanics should read data dynamically, call actions,
use schedules and observers, and communicate with the inner AI. The particular API and execution
semantics below remain proposals. Live progress/output is also under discussion. Today's mechanic
runner takes JSON snapshots, executes synchronously, and returns logs and declared effects/events
at completion. Its cancellation constraint can stop execution, but it exposes no live progress
callback, host read operation, incoming event subscription, or AI delegation call.

Provide a small JavaScript-facing system interface over the existing host service owners. Calls
should use the same authorization, runtime definitions, and operation history as human/AI callers;
they need not make a loopback HTTP request. Its procedure contracts should be discoverable through
the system-supplied manual. Application-specific queries, handlers, and workflows remain runtime
definitions, so adding another use of these primitives does not require a new compiled adapter.
Execution profiles expose the permitted subset: read-only query/rendering code stays read-only,
while action/workflow profiles receive their granted operations. Each nested call retains the
initiating identity and may narrow its authority; it cannot expand it by changing callers.

| Service | Proposed JavaScript authoring experience |
| --- | --- |
| Data | Request objects or bounded structured queries over runtime-defined shapes when needed, receiving plain JavaScript values. The host tracks scope and state versions required for later writes. |
| Actions | Invoke a known action with inputs and obtain structured output, or discover an action by intent before selecting it. Preserve the canonical action identity and ordinary validation/commit boundary. |
| Schedules | Create, inspect, change, and cancel one-time or recurring work referencing a stored action and serializable inputs. Preserve explicit time-zone and recurrence policies. |
| Observers | Register, inspect, change, and remove event/state subscriptions with bounded conditions and stored JavaScript handlers. The host retains and dispatches them after the registering invocation ends. |
| Inner AI | Submit a bounded intent/task with relevant inputs, inspect status, obtain structured results, or cancel it. Reuse the same focused worker and task lifecycle available to the outer AI. |

Observers here mean event/state subscriptions and reactions. They do not mean the authorized
viewer/observer identity used to decide which information a caller may see. Queries use the host's
data access boundary; arbitrary SQL or raw database connections are not needed for dynamic data.
An AI delegation carries the calling action's scope, procedure context, parent task, and budget.
The caller can use a returned result or continue through a later task-completion handler.

Distinguish short atomic work from durable orchestration within this authoring surface. A bounded
action can compute and compose child results into one proposed change set. A longer workflow can
read fresh information, delegate to AI, wait for events, and invoke actions as separately recorded
steps. Its waits and model calls run outside database writer transactions. Child results in an
atomic composition remain provisional until the root commits; committed workflow steps retain
their effects if a later step fails. Exact API names and atomic/read-after-write semantics remain
to be specified, rather than hidden behind an ambiguous general-purpose call.

Schedule/observer registration is a persistent mutation. Validate it through the normal write
boundary, retain ownership and handler-version policy, and make retried registration reuse its
logical identity. A failed atomic action must not leave a live subscription or scheduled job.
Use stored handler references and serializable inputs instead of relying on a captured JavaScript
closure to survive engine disposal. Delegation and action calls share bounded child-work budgets
so a JavaScript-to-AI-to-JavaScript chain remains accountable to its initiating operation.
Existing short deterministic event reactions can retain their transactional execution. A reaction
that needs AI, a long computation, or a later event should stage durable work in that transaction
and let a worker execute after commit.

### Progress and incoming messages

A narrow host interface could accept progress messages or output chunks and forward them to a
C# channel, stream, or existing client delivery path. The host owns the stream and validates/bounds
messages; the script need not receive a raw .NET stream or unrestricted CLR access. Progress is
provisional execution information, distinct from committed state changes and domain events. Slow
consumers must not create an unbounded buffer or indefinitely block the interpreter.

For incoming information, the host can invoke a JavaScript handler between execution turns, or a
script can request fresh authorized data through a host function. With Jint 4.15's opt-in
`ExperimentalFeature.TaskInterop`, an asynchronous host function can supply a .NET Task that
JavaScript awaits as a Promise; Jint's async execution APIs process the continuation when that
Task completes. This bridge is marked experimental and is not enabled by today's runner.
A host event can therefore complete a pending message request, without an external event thread
directly entering the engine. See
[Jint's interop examples](https://github.com/sebastienros/jint/blob/v4.15.0/README.md#examples) and
[versioned async execution implementation](https://github.com/sebastienros/jint/blob/v4.15.0/Jint/Engine.Async.cs).

One engine must have serialized ownership, including asynchronous continuations. Queue external
events and deliver them through the host-controlled execution path; do not concurrently mutate
globals or invoke callbacks while the engine is executing. A busy synchronous loop does not become
responsive merely because another thread has an event. Longer computations need cooperative
checkpoints or bounded steps, with interpreter limits/cancellation as a separate control.

Prefer durable event subscriptions and small handler invocations for long waits. Keeping an async
script alive may be useful for a bounded job, but its suspended JavaScript stack does not survive
a host restart. The host must own durable waiting, checkpoints, event delivery, and cleanup.
Dynamic subscriptions can reference stored JavaScript handlers over already supported event
sources, without requiring a new compiled handler for each application behavior. Fresh reads
still require authorization and tracked state versions; final writes use the ordinary commit path.

### Warm definitions with fresh invocation state

Cache prepared JavaScript and dependency plans by immutable source/contract identity. Warm active,
frequently used definitions at startup or activation and prepare others lazily within memory
limits. Loading the dynamic system should mean reusable definitions and plans; it need not load
every stored object or historical action version into one engine.

For hot reload, prepare and validate the new definition before switching the active reference.
New calls use the new generation; in-flight work retains its selected version under the agreed
completion/cancellation policy. Unchanged prepared definitions remain reusable. Cache validity
must account for source, harness/parser settings, engine version, and resolved dependencies.

Initially retain a fresh engine and fresh authorized inputs for each invocation, sharing prepared
programs rather than mutable JavaScript objects, globals, action results, or permission decisions.
Jint 4.15 supports reusable prepared programs across engines, and its engines are not thread-safe.
Its global snapshot/restore feature does not provide isolation, so engine pooling would require a
separate measured design. Preparation saves parsing/static-analysis work; it is not native JIT
compilation. See [Jint's versioned embedding guidance](https://github.com/sebastienros/jint/blob/v4.15.0/README.md#embedding-performance).

## 3. Differences from the implementation baseline

The assessments below describe inspected source, not a deployed feature acceptance report.

| Desired behavior | Current position | Alignment work |
| --- | --- | --- |
| New application logic without compilation | JavaScript mechanics and versioned definitions exist; generic host primitives remain compiled. | Demonstrate a complete runtime authoring-to-use path without manual repository edits/builds. Identify any record kinds still requiring those steps. |
| Native-feeling dynamic objects in JavaScript | Declared object snapshots already expose parsed values; lower-level component paths can expose serialized values. | Establish a consistent authoring interface over existing objects and effects, including the requested authorized reads during execution. Specify read consistency and commit dependencies. |
| Shared runtime API for reads, writes, and actions | Fixed generic paths and capability/query/action dispatch already cover much of this. | Complete coverage over runtime-defined capabilities; use existing authorization and execution owners. Add custom route aliases only for a demonstrated need. |
| Root website and runtime page publication | Versioned HTML/assets and publication/navigation exist. | Preserve these owners while adding missing composition behavior. |
| Server-composed reusable pages/components | The inspected server returns active HTML; shipped browser components do client composition. | Add a bounded runtime page/component composition declaration and server rendering/binding path. |
| Intent produces enough relevant AI context | Feature search, optional embeddings, declared projections, and tool aggregation exist as separate mechanisms. | Join them into progressive intent-based context selection, including inputs, useful information, and explicit missing context. |
| The system supplies its operating manual by intent | `orient` points to the system-use procedure. Procedures are versioned; active application procedure content participates in semantic feature retrieval. Global procedure search is a separate lexical path. | Unify relevant global/application manual discovery; retrieve sections with required constraints and contracts; bind manual revisions to the executable revisions they describe. |
| Several intents find the same capability or reusable task | Feature aliases/match phrases already map to one feature; successful intents for the same canonical recipe add evidence to that recipe. | Complete association authoring and refresh paths. Add contextual usage/binding links where phrases alone are insufficient, retaining one canonical implementation. |
| A focused inner AI executes work for the outer AI | AI requests support custom instructions, selected tools, structured output, and budgets; interaction scope and delegation references exist. Web inner/outer profiles share the same service path. | Connect system-selected manual/context to a bounded delegated worker; expose compact, evidenced results without forwarding its entire conversation. |
| The outer AI coordinates multiple inner tasks | Durable system tasks, receipts, and separately leased/concurrent scheduled AI exist. Continued-subtask requests resume a conversation. | Complete durable parent/child task submission, status/wait/results, cancellation, dependencies, and bounded parallel execution through existing owners. |
| Repeated JavaScript runs reuse preparation and support hot reload | The trusted harness is prepared once, but each invocation creates an engine and parses action source through `new Function`. The existing elapsed metric combines several costs. | Measure those costs; cache prepared action programs and versioned dependency plans with fresh invocation state. Add bounded warmup and activation invalidation before considering engine pooling. |
| JavaScript uses data, actions, schedules, observers, and inner AI | The runner exposes initial snapshots/final output. Host services and declared child-mechanic composition exist, but there is no general script-callable service interface. | Connect the existing owners through authorized runtime reads, action composition/orchestration, persistent registrations, and delegated task operations. Distinguish atomic changes from committed workflow steps. |
| Live JavaScript progress and incoming messages (under discussion) | Jint supports host calls and asynchronous continuations; the current runner supplies host cancellation but no live message interface. | Separate provisional progress from commits, serialize engine access, and keep durable event waiting in the job/trigger owners. |
| Existing behavior is found before another version is invented | Search, recipes, and advisory overlap detection exist. | Apply reuse/overlap review consistently to authoring, with structural as well as textual evidence. |
| AI/system can publish functionality within standing permissions | Authoring/validation/activation building blocks exist; approval and publication paths are fragmented. | Create a recoverable authoring-to-activation workflow through those owners and enforce configured author/activate permissions. |
| Scheduled AI may perform authorized writes | Current scheduled AI has no write-approval gates and can only read or prepare inert work. | Replace the blanket unattended restriction with trusted standing-permission evaluation for the relevant path. |
| Triggers invoke runtime actions | Generic trigger targets currently produce notifications; scheduled AI is a separate work layer. | Add durable action-job targets and a runner using the ordinary action boundary. |
| Long background algorithms | Bounded mechanics and leased scheduling infrastructure exist. | Provide explicit job execution, cancellation/progress, retry, and stale-input semantics suitable for longer computations. |
| Runtime-authored state/external conditions | Observation and conditional triggers exist, but matching adapters are reviewed host code. | Add a bounded declarative or JavaScript condition path over admitted observations/state, with permitted operations and declared dependencies. |
| Option to connect other APIs later | Authenticated observation/device ingress and scoped phone credentials exist; actual telephone/OS event capture is not established by that infrastructure. | Preserve generic admission and service-extension contracts. No new device/service connector is part of the website-and-Codex delivery. |
| Relevant retrieval remains current after authoring | Feature vectors use a disposable generation tied to content; automatic warmup is at startup. | Refresh derived discovery/index generations as part of publication/change handling, preserving lexical fallback. |

Existing owners to extend:
[mechanic evaluation](../../src/system/application-execution/persistence/ApplicationMechanicEvaluator.cs),
[object snapshots](../../src/system/application-execution/persistence/ApplicationMechanicObjectProjectionResolver.cs),
[query contracts](../../src/system/catalog-navigation/domain/ApplicationQueryContracts.cs),
[web dispatch](../../DantesRoleplay.Web/Http/WebInterfaceApplicationEndpoints.cs),
[page serving](../../DantesRoleplay.Web/Http/WebInterfaceEndpoints.cs),
[feature retrieval](../../src/system/interaction-orchestration/persistence/InteractionFeatureRetriever.cs),
[AI tool composition](../../src/system/system-capabilities/hosting/SystemAiAgentService.cs),
[overlap detection](../../src/system/interaction-orchestration/hosting/InteractionMechanicOpportunityLearner.cs),
[write approval](../../src/system/system-capabilities/hosting/SystemCapabilityAiTools.cs),
[scheduled AI](../../src/system/trigger-scheduling/hosting/ScheduledAiTaskTools.cs),
[phone credentials](../../src/system/trigger-scheduling/persistence/SqlitePhoneCompanionAuthentication.cs),
[trigger contracts](../../src/system/trigger-scheduling/domain/TriggerSchedulingContracts.cs).

Owners for the manual, delegation, and execution additions:
[orientation](../../DantesRoleplay.MCPServer/Mcp/OrientMcpTool.cs),
[procedure records](../../src/system/procedures/domain/ProcedureModels.cs),
[procedure search and writes](../../src/system/procedures/persistence/ProcedureStore.cs),
[procedure protocol handler](../../DantesRoleplay.MCPServer/Handlers/ProcedureHandler.cs),
[application procedure materialization](../../src/system/catalog-navigation/persistence/ActivatedApplicationCatalogProvider.cs),
[embedding text](../../src/system/interaction-orchestration/domain/InteractionFeatureRetrievalContracts.cs),
[recipe evidence](../../src/system/interaction-orchestration/persistence/InteractionRecipeStore.cs),
[capability contracts](../../src/system/system-capabilities/domain/CapabilityContractContracts.cs),
[AI request contracts](../../DantesRoleplay.LocalAI/Contracts/AiContracts.cs),
[interaction authority](../../src/system/interaction-orchestration/domain/InteractionAuthorityContracts.cs),
[interaction receipts](../../src/system/interaction-orchestration/persistence/InteractionReceiptStore.cs),
[system task contracts](../../src/system/system-task-orchestration/domain/SystemTaskContracts.cs),
[JavaScript execution](../../DantesRoleplay.DataAccess/Mechanics/JintMechanicEngine.cs).

Existing owners for script-callable services:
[application reads](../../src/system/interaction-orchestration/hosting/ApplicationReadModelService.cs),
[action execution](../../src/system/application-execution/persistence/ApplicationActionRunner.cs),
[event subscriptions](../../src/system/events-and-notifications/domain/ISubscriptionStore.cs),
[subscription authoring](../../DantesRoleplay.MCPServer/Handlers/SubscriptionHandler.cs),
[JavaScript event reactions](../../src/system/events-and-notifications/persistence/ApplicationEcsReactionRouter.cs),
[schedule administration](../../src/system/trigger-scheduling/domain/TriggerSchedulingAdministrationContracts.cs).

Focused implementation details relevant to those gaps:

- JavaScript event reactions already exist through versioned subscriptions and execute within
  the root ECS transaction with bounded chaining. Declared child mechanics also already compose
  into root effect proposals. The missing service interface should expose and extend those paths,
  while adding separate durable orchestration for waits, model work, and independently committed
  actions. It must not treat all existing event/action integration as absent.
- Application feature embedding text includes procedure content, but only the first 8,000
  characters of the combined record are embedded. This is whole-record retrieval rather than
  manual-section retrieval. Global procedure search searches summary metadata and match phrases,
  not full instructions/constraints, and needs an explicit current-manual lifecycle policy.
- Procedure records support match phrases, but the inspected protocol write handler does not
  expose them and procedure write hashing omits them. Association authoring must update the
  appropriate record and invalidate retrieval when its discovery meaning changes.
- Capability descriptors reference procedure IDs, but this alone does not bind an exact manual
  revision to the executable revision it describes.
- Existing inner/outer roles, delegation identity, and continued-subtask request kinds do not
  establish a complete concurrent child-worker lifecycle. Extend those foundations rather than
  introducing competing task histories, grant formats, or provider integrations.

## 4. Draft alignment plan

This order is proposed, not an accepted implementation schedule. Each slice should reuse existing
owners. The working defaults below provide enough direction to refine it into a concrete core
implementation plan; no further technical preference questionnaire is required. No permanent
runtime IDs or public contracts are allocated by this document.

1. **Define the runtime authoring and permission boundary.** Specify which information, actions,
   queries, page components, handlers, and workflows are runtime-authorable; define standing
   execute/author/activate grants. Specify the requested JavaScript interface to data, actions,
   schedules, observers, and inner tasks, including atomic versus workflow execution and read
   consistency. Resolve schema-evolution questions. Outcome: ordinary application development has
   a clear path that needs no host build.
2. **Prove one complete authoring-to-activation workflow.** Reuse existing record stores, schema
   validation, preview, and activation. Include discovery of existing alternatives, a recoverable
   candidate, effect/dependency validation, a permission decision, publication, and read-back.
   Outcome: an authorized caller can create or revise a simple generic action and use it immediately.
3. **Complete generic invocation, intent discovery, and the operating manual.** Connect the existing
   HTTP/MCP/AI surfaces to the same runtime action/query definitions. Extend `orient` and existing
   procedure retrieval with relevant manual sections, prerequisite context, and executable-version
   binding. Complete alternate-intent authoring, reuse/overlap review, and index refresh after
   changes. Outcome: an AI can describe its goal and receive enough instructions and state to reuse
   existing behavior without browsing the whole catalog or creating duplicate implementations.
4. **Connect events and schedules to durable action jobs.** Extend trigger targets through queued
   work, not nested inline action transactions. Carry original scope/grant, exact action version,
   inputs, causation, and an idempotent identity. Expose script-callable registration, inspection,
   update, and cancellation through existing owners; retain subscription ownership and handler
   lifecycle. Outcome: schedules and observers can be authored in JavaScript, survive the creating
   invocation, and run the same authorized actions as interactive callers.
5. **Support substantial background work and runtime conditions.** Define compute budgets,
   checkpoints/cancellation, stale-result handling, retry semantics, and bounded condition evaluation.
   Connect authorized fresh reads and action invocation through the host service interface.
   Consider bounded progress/output delivery and queued incoming messages with explicit waiting
   and invocation lifetimes.
   Retain authenticated observation/service extension points for future APIs. Outcome: deterministic
   computations and internal state/event triggers operate without a model supervising each step or
   a long-held database write transaction; no new external connector is needed for acceptance.
6. **Connect focused inner workers and outer coordination.** Build on the AI runner, interaction
   context/grants/receipts, and durable work owners. Start with one delegated task and compact
   structured result; extend to parent/child lifecycle, status/wait, cancellation, dependencies,
   concurrency, and aggregate budgets. Expose that same lifecycle to JavaScript workflows without
   retaining a database writer transaction while waiting for AI. Outcome: both outer AI and stored
   JavaScript can delegate bounded work and use evidenced results while each worker receives its
   relevant operating context. Compare outer context, total model cost, latency, and completion
   quality separately.
7. **Reuse JavaScript preparation and support definition hot reload.** Separate measurements of
   engine creation, context materialization/serialization, source preparation, and execution. Cache
   prepared action programs with current function-body semantics and exact-version dependency
   plans; warm selected active definitions and switch generations during activation. Outcome:
   repeated calls avoid repeated source preparation while context, limits, permissions, and effects
   remain invocation-specific. Consider engine pooling only if remaining measured cost warrants it.
8. **Add runtime server page composition.** Reuse page/content revisions, authored assets, registered
   object/query bindings, and generic action dispatch. Start with one composition profile and a
   generic page assembled from reusable components. Outcome: a new human-facing workflow can be
   published without adding host routes or compiled application widgets.
9. **Expose understanding and recovery to the operator.** Show effective capabilities, context
   sources, standing permissions, pending/active changes, jobs, and causal history. Surface failures
   with concrete recovery choices. Outcome: the operator can explain what changed, why an action
   ran, and which version or permission governed it.

This plan does not assume a storage-engine replacement. Measure actual context cost, request
latency, job throughput, and operational complexity before introducing another execution/storage
engine. Routine verification belongs with each implementation slice; a separate test inventory
is not part of this product discussion.

The manual/context/intent work is a direct foundation for focused inner workers. Their lifecycle
can reuse the durable-job work. JavaScript preparation measurements and caching can proceed as a
separate bounded improvement; they do not require redesigning the AI workflow or storage first.

## 5. Additional features proposed for discussion

- **Preview and recoverable activation.** Evaluate candidate behavior against bounded sample/snapshot
  inputs, show affected consumers, and retain the previous active definition. Restoring code does
  not automatically undo data changes or external effects already performed.
- **Dependency and impact inspection.** Show which pages, workflows, triggers, and actions depend
  on a schema or action before changing it. This supports safe automatic changes and better AI context.
- **Capability explanation.** Explain why an action is available, what input is missing, why a
  permission denied it, and which existing behavior was considered before a new action was created.
- **Schema evolution alongside code evolution.** Runtime code changes are only part of updating
  functionality. Data-preserving conversion, compatibility, and version selection need an equally
  usable path when stored object shape changes.
- **Loop and workload controls.** Detect or bound repeated event chains, retries, job fan-out, and
  self-modification loops. Coalesce replaceable work and retain failure history so autonomous behavior
  remains explainable and does not repeatedly perform the same external action.
- **Version-aware recurring work.** Keep a running job bound to its selected definition while
  allowing future runs to adopt new compatible definitions under the configured policy. Distinguish
  recomputing after stale input from retrying an uncertain commit, and fence out expired workers.
  External side effects require their own idempotency/reconciliation; database rollback cannot
  undo an external service call.

These suggestions are not confirmed product commitments. Existing preview, versioning, dependency,
audit, and execution-budget mechanisms should be reused when they satisfy the intended experience.

## 6. Planning readiness and working defaults

The user trusts the assistant's recommendations and has confirmed the initial deployment model.
No unanswered product decision or technical preference blocks planning the core platform.
Dynamic service access and activation within configured permissions are already decided. The
following are explicit working defaults for planning, not claims about implemented behavior or
new authorization to change the runtime.

| Design area | Working default |
| --- | --- |
| Existing system | Extend the current owners incrementally. Preserve live data and make compatibility changes explicit; include reviewed migration/recovery steps where needed. Do not replace storage or the JavaScript engine without measured justification. |
| Action execution | Keep short atomic composition and durable workflow execution distinct in their contracts. Atomic child results remain provisional until the root commits. Workflow action steps commit independently; waits and AI run outside writer transactions. |
| Reads and waiting | Track the state/revisions used by host queries and check write preconditions. Keep provisional child outputs explicit; obtain fresh authorized state after workflow waits or committed steps. Persist long waits/checkpoints rather than assuming a suspended JavaScript stack survives restart. |
| Standing permissions | Configure execution, authoring, and activation by scope/effects. Carry the initiating authority through scripts, tasks, schedules, and observers and recheck it at execution. An allowed action ID cannot expand its own grant. |
| Ambiguous intent | Continue bounded discovery automatically. Ask for missing meaning when alternatives imply materially different actions or effects and context cannot resolve them; return a structured unresolved result for unattended work. Similarity alone cannot choose an uncertain write. |
| Page composition | Start with runtime-authored declarative components/templates and data/action bindings. Keep existing dynamic browser assets. Additional server rendering hooks can be scoped when the first composition requires them. |
| Version selection | Pin each running invocation to its resolved definition. Future runs normally use the active compatible definition with fresh validation and permissions; support an explicit pin where reproducibility is required. Changed or incompatible contracts require refreshed resolution. |
| Inner workers | Outer AI and JavaScript workflows can create several bounded child tasks. An inner worker does not receive recursive worker-creation capability by default. Keep total task depth, fan-out, and budgets bounded across AI/action call chains. |
| Learned intent associations | Retain observed evidence first; publish an association automatically when its target/bindings validate and the standing author/activate grant allows it. Uncertain matches remain candidates and do not create another implementation. |

### Confirmed initial deployment scope

The user selected one installation they control, with invited users and permissioned AI. Plan
the core platform for that scope, with website and Codex as its initial integrations. It still
requires user/application permissions and the existing
protections for arbitrary AI-authored JavaScript; this is not permission to weaken them.

A hosted service where unrelated customers author and run their own applications would require
a separate tenancy and workload/isolation design. Do not imply that existing application scopes
already provide that guarantee or add that deployment project to the initial plan.

### Decisions that can wait for their implementation phase

No concrete external device/service is needed for the initial plan. Preserve the option to add
APIs later; choosing and implementing a particular bridge is separate future work. Do not invent
a phone platform, provider, credential, or external action.
Workload measurements can establish cache and concurrency budgets; a strict real-time or unusually
large deployment would need explicit targets before making performance guarantees.

Use a generic end-to-end platform workflow as the initial integration milestone: runtime-author an
object/query/action, connect it to an event or schedule, execute a durable JavaScript workflow that
can delegate a bounded inner task, record its authoritative result, and display it through runtime
page composition. This prioritizes the requested foundation without depending on application rules.

The concrete plan should map each slice to existing owners, contract/data changes, dependencies,
recovery/migration boundaries, and observable acceptance outcomes. Verification belongs to each
slice; a separate inventory of individual tests is outside this product discussion.

As discussion continues, update the confirmed direction, gaps, defaults, and plan in place. Keep
remaining product assumptions visible without turning routine engineering choices into user gates.

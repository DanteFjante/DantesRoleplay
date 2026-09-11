# 01 — Runtime services and JavaScript execution

This proposed workstream turns existing JavaScript evaluation and typed actions into a reusable service runtime for the website and Codex integration. It implements no device or provider bridges. Other APIs remain future extension points. Names below describe contracts; this plan allocates no runtime identifiers. [Platform requirements](../PLATFORM-REQUIREMENTS.md) remains the current-state baseline.

Prerequisite: implement [00 — Shared foundation](00-shared-foundation.md) first and have the coordinator supply its accepted foundation revision and contract baseline. This workstream consumes those shared contracts and does not redefine them independently.

## Existing owners to retain

- [JintMechanicEngine](../../../DantesRoleplay.DataAccess/Mechanics/JintMechanicEngine.cs) creates a constrained engine for each invocation. It reuses immutable prepared mechanic programs and the trusted harness. Current inputs and outputs are JSON; logging is buffered.
- [ApplicationMechanicEvaluator](../../../src/system/application-execution/persistence/ApplicationMechanicEvaluator.cs) resolves exact definitions, materializes projections and evaluates bounded child composition. Its authorized/object/graph snapshot paths currently reject mutation proposals.
- [ApplicationActionRunner](../../../src/system/application-execution/persistence/ApplicationActionRunner.cs) and [ApplicationEcsEffectBatchBuilder](../../../src/system/application-execution/persistence/ApplicationEcsEffectBatchBuilder.cs) connect exact mechanic evaluation to checked typed effects.
- [ApplicationEcsEffectApplier](../../../DantesRoleplay.DataAccess/Ecs/ApplicationEcsEffectApplier.cs) owns operation replay, optimistic expectations, atomic effects, reactions and audit. It requires its own writer transaction.
- [SqliteApplicationScopedEcsStore](../../../DantesRoleplay.DataAccess/Ecs/SqliteApplicationScopedEcsStore.cs) and [SqliteComponentTypeRegistry](../../../DantesRoleplay.DataAccess/Ecs/SqliteComponentTypeRegistry.cs) retain scoped persistence, schema versions and validation. [PrivateOperatorAuthorization](../../../src/system/authorization/domain/PrivateOperatorAuthorization.cs) supplies existing trusted-principal and authorization contracts.

## Prepared execution and invocation pinning

The mechanic engine validates the source body independently before preparing its executable
wrapper. The mechanic function and trusted harness have separate lexical scopes. Its process-local
cache retains at most 256 programs, 16 MiB of UTF-8 source, and one million combined token/node
work units; these bounds do not measure total interpreter memory. Source is limited to 256 KiB
before cache indexing. Token, lexical nesting, recursive-expression, syntax-tree size and depth
checks bound cold preparation. Constant folding is disabled for untrusted preparation so literal
expressions cannot allocate cached values before interpreter limits apply. The key includes exact
source, wrapper text and parser/runtime configuration. No engine, JavaScript value, input or random
state is shared between calls. Interpreter allocation checks are not an OS-enforced process memory
limit: an individual JavaScript operation can allocate between checks.

Set the AppContext switch `DantesRoleplay.Mechanics.DisablePreparedProgramCache` before constructing
the engine to use fresh preparation for recovery. The existing singleton registration controls the
cache lifetime. The `DantesRoleplay.Mechanics.JintMechanicEngine` meter reports separate
`dantesroleplay.mechanic.preparation.duration`,
`dantesroleplay.mechanic.context_construction.duration` and
`dantesroleplay.mechanic.execution.duration` histograms in milliseconds. Preparation includes cache
lookup/wait time. Preparation runs outside the cache lock, with at most two active preparations
and 32 admitted callers. Same-source waiters share preparation and observe their own cancellation
and deadline. A leader's personal cancellation or timeout lets healthy waiters retry under their
own original budgets. Tokenization and source parsing check cancellation/deadline; the final synchronous
Jint preparation is structurally bounded and checked before and after. Preparation and context time
reduce the remaining execution timeout. Diagnostics cannot replace the invocation result.

The evaluator retains one immutable catalog navigator through its entire parent/child invocation
tree. A later root invocation resolves its navigator independently; unavailable or stale selected
content never falls back to an earlier executable. This pinning does not itself refresh the active
catalog provider, authorize old-generation commits, or implement activation and rollback.

The internal `JintMechanicEngine.PrepareMechanicProgram` method is the exact executable preparation
seam for coordinated activation integration. Existing activation preparation remains separately
owned until that integration is accepted. The registered read-only service adapter consumes the
standing-grant read adapter and owner-resolved grant targets, with no legacy permission fallback.
Its registered workflow profile exposes only exact actions declared by the retained
`requirements.service.actions` contract. Each action derives a deterministic child command,
preserves the parent command, shares the root deadline and operation ledger, rechecks current
state-scoped Execute authority for the exact action and every actual typed root/reaction effect
inside the effect applier's writer transaction, and commits through the existing action, effect and
operation-log owners. A denied commit guard rolls back staged effects without writing an action
operation. Successful child receipts remain attached to later workflow completion or failure. The
public root atomic adapter still rejects parented requests, and read-only engine instances still
return the canonical unavailable result for `ctx.services.action`. Atomic service execution,
workflow handoff, wait, job and AI callbacks remain unavailable until their real dependencies and
shared contracts are integrated.

The internal candidate runtime validator consumes the activation owner's retained pure-mechanic
closure evidence. That evidence covers every changed Markdown/JavaScript pair through the existing
normalized record and requirements owners. The initial profile permits only empty requirements or
an input schema; broader dependency completeness remains false. Every selected mechanic needs a
retained input/expected-data sample, with at most four per definition and sixteen per request. The
entire request and report each obey the 64 KiB/depth 32 bound.

Runtime checks require current application Read and Validate authority in the authoring owner's
existing transaction. Each engine attempt consumes one caller operation and shares its deadline;
the validator creates no transaction, budget, service capability or effect dispatcher. Reports keep
original sample ordinals, actual attempt status and bounded data fingerprints, including partial
progress when cancellation or exhaustion stops execution. The selection pin is the pure closure's
evidence fingerprint; the policy pin includes the actual parser/runtime, harness, schema profile and
execution limits. Known non-data outputs are rejected. Completed means only that these sample
checks passed, never global candidate validity or publication approval. This internal validator
remains unregistered pending coordinator integration with retained validation reports and the
remaining preparation/reuse/publication gates.

The frozen read-only service contract bounds each JSON exchange to 64 KiB/depth 32 and the root
exchange total to 1 MiB. The root consumes one shared operation; each registered read consumes one
through its existing adapter, including authorization denial, up to 16 overall. After a terminal
read failure, further read callbacks return that failure without dispatch or another operation debit;
the root statement and deadline limits still bound those attempts. `ComputationLimits` controls the root mechanic.
Child reads retain the existing host-owned `ExecutionLimits.ReadModel` caps and share the invocation
deadline; callback waits also observe the root's remaining wall time. Root memory, statement and
recursion limits do not describe aggregate resource use across child interpreters. Progress is a
transient channel of eight frames, at most 32 attempts and 16 KiB total, with 2 KiB per serialized
frame; full and closed attempts count, and only accepted frames receive consecutive sequence numbers.
Reads, actions and progress execute synchronously on the sole engine thread through captured JSON functions;
CLR capability objects never enter JavaScript. The host resolves and validates the exact retained
`requirements.service` declaration, pins the exact root target and grant identity, rechecks those
identities and current authority, and validates output before emitting
process-local computation evidence. A workflow root uses Execute authority, and every separately
committed action consumes another operation and returns its independently replayable receipt. That
evidence is not a durable task or checkpoint.

Trusted application-scoped invocations use `InteractionInvocationHost.ForApplication` with both
state identity fields absent. The existing constructor, planner context and envelope factory remain
state-required; state read, action and service adapters reject an application-only host before
accessing authority or storage. `TryTransferOperations` atomically debits the current budget and
its ancestors, returning an independent bounded ledger at the same deadline. Downstream spending
does not debit the original ledger again, and unused or failed work receives no refund. A transfer
does not establish authority, admit expired work or create durable task history.

## Proposed execution contract

A service definition declares an immutable definition revision, input/output shape, permitted data reads, callable actions and dependency revisions. The host constructs an invocation envelope containing invocation principal/scope/grant ref, definition revision, operation/parent identity, budgets and cancellation. Script input cannot manufacture authority. The same contract serves website, Codex and inner-AI callers; adapters establish identity and presentation.

The canonical result contains outcome, data, committed-operation evidence and an optional task handle. Evidence distinguishes proposed work, committed work, replay and pending work. An orchestration failure after a successful child action must preserve that child's receipt; it cannot imply everything rolled back. Errors expose bounded safe details with a correlation identity.

Provide three explicit execution boundaries:

1. **Data:** declared, authorized projections with bounded input/output and freshness evidence. Reads do not expose a database context, live entity object or general CLR access. Existing read-only snapshot evaluation remains read-only until an explicit writable contract preserves its observations and constraints.
2. **Atomic action:** bounded computation produces one typed proposal, which the existing action/effect owners validate and commit. Computation occurs outside the writer transaction; observations and authority are checked before commit. Short existing transactional reactions retain their bounded contract. An action cannot await AI, user input or network work inside its transaction.
3. **Service orchestration:** JavaScript may request authorized data, invoke separately atomic actions or hand work to workstream 04. Budgets cover child calls, data volume, output and computation as well as individual engine limits. Host-supplied callbacks are narrow capabilities, not unrestricted storage access.

Process-local progress and asynchronous replies can use bounded host channels and serialized engine execution. C# event producers enqueue messages or complete awaited host operations; they never invoke an executing engine concurrently. Busy JavaScript must yield or be cancelled. Jint's [Task/Promise interop](https://github.com/sebastienros/jint/blob/v4.15.0/README.md#taskvaluetask-to-promise-interop-experimental) is opt-in and experimental; ordinary `await` provides no restart durability.

Durable handoff persists a step/checkpoint and a correlated completion handler through 04, then returns a task handle. It does not persist the JavaScript stack. AI calls and waits complete through that boundary. A host-call journal may retain idempotent request/result evidence; arbitrary script replay requires capturing all relevant nondeterminism and calls and is outside the initial slice.

## Numbered deliverable slices

1. **Agree contracts and ownership.** With 02, specify revision/dependency and activation inputs; with 04, specify checkpoint, completion and task-handle semantics; with 05, specify AI call/result envelopes. The coordinator owns shared registrations, wiring and migrations. Acceptance: both website and Codex adapters can express identical authorized invocations and canonical results; missing authority or unsupported modes fail explicitly. Recovery: retain existing action routes until their adapter is accepted.

2. **Prepare immutable programs.** Extend the mechanic-engine owner with a bounded prepared-program cache keyed by exact source content, wrapper semantics and parser/runtime configuration. Cache the executable wrapper rather than leaving mechanic parsing inside `new Function`. Separately pin resolved dependencies. Preserve fresh engines, limits, random state and inputs per call. Jint [prepared scripts](https://github.com/sebastienros/jint/blob/v4.15.0/README.md#embedding-performance) reuse parsing/static analysis, not native JIT output. Acceptance: repeated calls reuse preparation while concurrent calls and hostile global mutations remain isolated; measure preparation, context construction and execution separately. Recovery: disable the cache and use fresh preparation.

3. **Expose bounded services.** Add the host-controlled data, action and progress interfaces around existing evaluators and runners. Define cancellation, backpressure and output limits before adding process-local asynchronous callbacks. Acceptance: denied reads produce no data; writes still pass schema, scope and stale-state checks; progress is observable without becoming a success receipt. Recovery: gate the service adapter and preserve the existing pure evaluation path.

4. **Join durable execution.** Integrate 04's dispatch/checkpoint contract and 05's AI operation boundary. Preserve operation/parent identity and commit evidence across retries; recheck current grants at consequential boundaries. Acceptance: uncertain commits replay safely, restarts resume explicit persisted steps, revocation prevents subsequent unauthorized work, and earlier successful child commits remain visible after later failure. Recovery: stop dispatch and retain durable records for controlled continuation.

5. **Activate and roll back generations.** Prepare and validate candidate definitions during 02's pre-publication validation. After its authoritative activation commits, switch the process-local generation consistently to those exact retained definitions. A cache miss resolves/prepares that exact revision; it must not substitute an old revision or mix dependencies. New calls use the activated generation; in-flight calls remain revision-pinned and encounter normal commit freshness checks. Bound old-generation retention. Acceptance: invalid candidate preparation leaves the prior generation active; a restart rebuilds the selected generation from retained content. Rollback changes future selection without rewriting state or claiming to reverse completed operations. Recovery: select a validated prior compatible generation; schema rollback remains coordinator-owned.

## Reuse decision

The runtime/storage gap is integration: service capabilities, preparation caching, durable coordination and standing-grant propagation. Existing isolation, versioned schemas, scoped reads, typed commits and replay are reusable foundations; no inspected requirement inherently demands a new project. A parallel replacement would also need persistence compatibility and cutover work. Prefer incremental integration, extracting an assembly only where a demonstrated dependency boundary helps. Timing requires measurements and slice estimates, not a presumed rewrite advantage.

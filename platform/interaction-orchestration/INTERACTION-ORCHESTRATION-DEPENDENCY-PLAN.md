# Interaction orchestration dependency tree — auditable local/remote intent execution and learning

Status: **planning only; semantic and public-surface decisions awaiting confirmation**  
Ruleset alignment: **ruleset-neutral**  
Source: **not applicable**  
Owner: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Related owner: [Local intent routing](../../LOCAL_INTENT_ROUTING_PLAN.md)  
Prerequisite owner: [Generic application kernel](../application-kernel/APPLICATION-KERNEL-DEPENDENCY-PLAN.md)  
Implementation guide: [Agent implementation guide](INTERACTION-ORCHESTRATION-AGENT-GUIDE.md)

## Outcome and non-goals

Deliver a server-controlled interaction workflow in which:

1. a remote conversational model submits a bounded semantic player intent;
2. the server resolves authorized current context and searches trusted feature contracts;
3. a verified recipe, optional local model, or remote model proposes exact queries/actions;
4. the server alone validates and executes accepted operations;
5. every resolution and execution attempt returns an inspectable receipt, including explicit
   `unknown`, `unsupported`, `needs-input`, `unavailable`, `unsafe`, and `stale` outcomes;
6. a successful remote plan may become a reusable candidate recipe without becoming executable
   authority;
7. both planners can use the same server-mediated lexical/vector feature search while the local-AI
   assembly remains unaware of games and rulesets; and
8. every searchable/executable capability is explicitly scoped either to the reserved `system`
   namespace or to an application such as `dnd2024`, with deterministic directory overlays resolved
   before retrieval.

This plan does **not**:

- give either model direct database, vector-store, filesystem, shell, network, or unrestricted tool
  access;
- let a model invent or activate a procedure, mechanic, schema, JavaScript rule, permanent ID, or
  catalog contract;
- treat an embedding score, model confidence, learned recipe, or remote narration as authority;
- fine-tune or mutate local model weights;
- mix user-scanned information with trusted executable feature contracts;
- guarantee all-or-nothing semantics across unrelated commits;
- bypass authorization, stale-state validation, declared mechanic roles, effect validation,
  operation audit, or existing action transactions;
- add D&D meaning to C# or to the local-AI component;
- let an application override trusted system behavior, or let an untrusted document directory
  override an executable feature contract; or
- authorize runtime implementation from this dependency plan alone.

## Architectural invariants

### Authority

- Catalog procedure/mechanic records, component schemas, current authorized state, and their exact
  versions/source hashes are execution authority.
- A recipe is a retrieval aid that references authoritative contracts. It is never an executable
  contract itself.
- The server selects, validates, and executes. A planner may only search, inspect, ask for missing
  input, or propose a closed plan.
- D&D formulas, eligibility, timing, and outcomes remain catalog JavaScript/data. The orchestrator
  is ruleset-neutral.

### Planner symmetry

Local and remote planners consume the same public planning concepts:

```text
authorized intent envelope
  + trusted feature-search results
  + exact contract inspections
  + verified recipes
  -> closed execution proposal or typed non-resolution
```

The local planner receives those values through a bounded server-controlled completion loop. The
remote planner receives them through protocol queries. Both proposals pass the same semantic
verifier and executor.

### No-tools local model

The local completion provider keeps its current no-tools boundary. It never calls a search tool or
action directly. Instead it emits one schema-bound next step:

```text
search request | inspect request | proposed plan | needs input | unknown
```

The orchestration host performs permitted reads, appends the observation, and invokes the provider
again. The loop has fixed round, candidate, prompt, output, and elapsed-time budgets.

### Separate trust domains

Maintain separate indexes and APIs:

1. **Trusted feature corpus** — active procedures, active mechanics, their declared roles/input
   contracts, component schemas, and verified recipes. It may inform executable proposals.
2. **Untrusted information corpus** — scanned files, campaign notes, user documents, and other
   information records. It may answer questions but may never supply instructions or executable
   contracts.

No query may silently search both corpora. Every result carries its corpus/trust class, source key,
version, source hash, and embedding-generation identity where applicable.

### Application namespaces

The [generic application kernel](../application-kernel/APPLICATION-KERNEL-DEPENDENCY-PLAN.md) owns
registration, qualified keys, source relationships, activation, and migrations. Interaction
orchestration consumes its effective application view and must not create a parallel registry.

- `system` is a reserved application namespace for ruleset-neutral platform capabilities only.
- Every non-system command, procedure, mechanic, component contract, feature document, and recipe
  belongs to exactly one explicitly registered application. The first application is `dnd2024`.
- The canonical public/search key begins with the application namespace: `system.*` for platform
  behavior and `dnd2024.*` for the initial application. A proposed expanded shape is
  `<application>.<record-kind>.<name>` when the kind is needed to prevent collisions.
- Existing authored IDs such as `procedure.system.*`, `mechanic.dnd2024.*`, and `game.core.*` are not
  silently renamed. A confirmed compatibility slice must map them to application-scoped keys,
  retain aliases for existing callers where required, and decide the owner of every `game.core.*`
  record. Recommended initial owner: `dnd2024`, unless a separate non-system base application is
  explicitly introduced.
- The generic kernel and local-AI component treat application IDs as opaque scope values. They must
  not contain `dnd2024` constants, D&D vocabulary, or application-specific branching.
- A search is scoped to one application plus its explicitly ordered base applications. System and
  application results are requested as separate collections; they are never blended implicitly.
- Application dependency order is deterministic, cycles are rejected, and an unknown dependency is
  an invalid configuration. `system` is a service namespace, not a base game-mechanics application.

### Registered directory overlays

The application kernel owns the database registry and overlay resolver. The rules below are the
retrieval/receipt contract this downstream consumer requires, not a second implementation owner.

The running database owns the directory registry, scan generations, and effective winner state.
File bytes remain authoritative for file-backed documents, authored catalog records retain their
existing development authority, and vector/lexical indexes are rebuildable derived data.

Each registered source directory has at least:

- opaque directory ID, application ID, display/logical name, canonical root path, enabled state,
  include pattern(s), reader profile, trust class, and explicit precedence;
- a scan generation and bounded scan status/error evidence; and
- administrative provenance sufficient to determine who may register or reorder a trusted source.

Each observed document records its directory ID, normalized relative path, logical identity,
content hash, media type, size, scan generation, and effective/shadowed state. Logical identity is:

- `(application ID, record kind, declared record ID)` when a trusted reader can parse an authored
  record; or
- `(application ID, normalized relative path)` for generic files without a declared identity.

Overlay resolution is completed before indexing or ranking:

1. Within the selected application/base stack, the highest-precedence eligible source wins for one
   logical identity.
2. Equal-precedence competing definitions are a typed configuration conflict; filesystem order is
   never a tie-breaker.
3. Shadowed documents remain queryable to authorized diagnostics and receipts, with their winning
   source reference, but are excluded from ordinary lexical/vector results.
4. Removing, disabling, or changing an override reveals the next eligible definition on the next
   successful scan and changes the effective-set fingerprint.
5. A source may override only an equal-or-lower trust class. Untrusted information sources cannot
   override trusted executable contracts, regardless of precedence.
6. Canonical-root and traversal/reparse protections remain mandatory. Remote projections redact
   host paths unless the caller is explicitly authorized to inspect them.

This gives an application a base directory plus one or more ordered override directories without
making semantic similarity decide which definition is authoritative.

For example, `dnd2024` may register `core-mechanics` at precedence 100, `dnd2024-pack` at 200,
and an approved extension at 300. If all three declare the same logical feature, the extension is
effective; if it is removed, the D&D definition becomes effective; if that is absent, the core
definition is used. All three directories contribute unrelated features normally.

## Proposed component ownership

```text
src/system/
  application-registry/             application scopes, base ordering, source-directory registry
  interaction-orchestration/       intent, plans, receipts, recipes, verifier, coordinator
  deterministic-retrieval/         trusted document model, lexical/hybrid search orchestration
  local-ai/                         generic embeddings/completion/vector storage only
  actions/                          existing action execution and transaction owner
  procedures/                       authoritative procedure retrieval
  mechanics/                        authoritative mechanic retrieval and sandbox invocation
  operations-and-audit/             existing operation evidence
  mcp-protocol/                     public orient/query/commit adapters
src/applications/dantes-roleplay-host/
                                    planner selection and optional local provider configuration
src/game-adapters/dantes-roleplay/  semantic content adapters only; no generic orchestration
```

Proposed component manifest dependency:

```text
interaction-orchestration
  -> building-blocks
  -> application-registry
  -> deterministic-retrieval
  -> procedures
  -> mechanics
  -> actions
  -> operations-and-audit
  -> local-ai contracts

local-ai
  -> general-purpose runtime libraries only
  -X-> interaction-orchestration, catalog semantics, game adapters, MCP, main game database
```

The `interaction-orchestration` label is proposed, not yet confirmed. Do not create its directory or
manifest until the confirmation gate closes.

## Existing owners and evidence

| Concern | Owner | State | Evidence/constraint |
| --- | --- | --- | --- |
| Provider-neutral completion/embedding | `src/system/local-ai/DantesRoleplay.LocalAI` | verified | Standalone project, no game dependency; schema-bound completion and embeddings exist. |
| Local route proposal | `src/game-adapters/dantes-roleplay/local-routing` | verified foundation, too narrow | Returns `proposed`, `needs-input`, or `unknown`; never executes. It selects only the first current mechanic candidate. |
| Mechanic discovery/inspection | `IMechanicStore` and `MechanicStore` | verified | Active records, versions, source hashes, requirements, and ranked phrase lookup exist. |
| Procedure discovery/inspection | `IProcedureStore` and `ProcedureStore` | verified | Active records, versions, source hashes, and phrase lookup exist. |
| Role/input projection | `IProjectionResolver` | verified | Server materializes declared context and rejects missing/invalid roles before mechanics run. |
| Action execution | `IActionRunner` / `ActionRunner` | verified | Owns mechanic selection, sandbox call, effects, transaction, seed, and audit. |
| Operation audit | `IOperationLog` / `OperationLog` | verified, insufficient for planning trace | Records public operations but not candidate searches, rejected plans, or unknown resolution attempts. |
| Story step orchestration | Story adapter | verified consumer | Can consume a local route proposal and has bounded participant behavior; it is not a generic planner. |
| Generic local files | local-AI scanner | verified | Provides bounded generic documents but no persistent generic vector index yet. |
| Catalog manifest provenance | `CatalogManifestEntry` | partial | Stores kind, ID, version, hash, and path, but no application, registered-directory, precedence, logical-identity, or shadow metadata. |
| Directory registration/overlay | none | missing | Scanner requests accept path specifications and return absolute paths/content metadata; the running database has no ordered application source registry or effective-winner state. |
| Trusted feature vector index | none | missing | Knowledge vector records are game-facing and cannot become the generic feature index. |
| Resolution receipt | none | missing | No append-only record covers searches, candidates, planner, non-resolution, or proposal fingerprint. |
| Execution-plan receipt | action/operation results | partial | Individual actions are audited, but no interaction-level ordered plan/step receipt exists. |
| Recipe memory | none | missing | No candidate/verified/stale/retired parameterized interaction recipe owner exists. |
| Remote fallback planning | current MCP queries/commits | partial | A remote model can search and act manually, but cannot submit/verify a reusable plan or inspect an interaction receipt. |
| Authorization/redaction | host/game policies | conflicting prerequisite | Planner inputs and receipts need one generic authorized-context/redaction port; do not embed game roles in orchestration. |

## Closed terminology

Use these terms consistently in all slice documents and code:

- **Intent envelope** — bounded semantic request plus optional role hints and idempotency key.
- **Resolution attempt** — one recipe/local/remote/deterministic attempt to produce a plan.
- **Resolution receipt** — immutable evidence of searches, candidates, inspections, outcome, and
  proposal fingerprint, including failures.
- **Execution plan** — ordered closed server operations referencing exact contract versions/hashes.
- **Execution receipt** — immutable interaction-level evidence of validation and executed/skipped
  steps, linked to existing operation IDs.
- **Recipe** — parameterized retrieval memory derived from successful receipts and exact contracts.
- **Feature document** — derived trusted search document for one authoritative record/version/hash.
- **Application** — explicitly registered non-system capability scope, identified by an opaque ID
  such as `dnd2024`, with a deterministic ordered set of allowed base applications.
- **Qualified feature key** — stable public/search key whose first segment is `system` or a registered
  application ID.
- **Source directory** — database-registered scan root with application, trust, reader, include, and
  precedence metadata.
- **Effective document** — the single eligible winner for a logical identity after directory and
  application overlay resolution; lower definitions are shadowed, not deleted.
- **Planner** — recipe resolver, local model adapter, or remote proposal adapter. A planner never
  executes.

Do not use “contract” as a synonym for recipe. A contract is authoritative; a recipe is not.

## Proposed closed contracts

The following are conceptual shapes. Exact names and serialized schemas require confirmation in
Slice 3 before becoming public or permanent.

### Intent envelope

- client idempotency key;
- trimmed intent text;
- required or server-derived application ID, plus optional explicit system-collection request;
- optional application/ruleset scope hint treated only as a search filter and never as authority;
- optional named role hints or already-resolved entity references;
- bounded conversation facts already authorized for planning;
- maximum planned steps and planner preference;
- plan-only or execute-after-explicit-authorization mode.

The caller may not supply mechanic outcomes, derived modifiers, effects, authorization decisions,
current revisions, source hashes, or validation results.

### Resolution status

Closed values:

- `resolved` — one semantically valid plan and fingerprint exist;
- `needs-input` — named roles or bounded facts are missing;
- `ambiguous` — multiple materially different valid routes remain;
- `unknown` — the planner cannot determine how to perform the request;
- `unsupported` — trusted feature search proves no executable contract is available;
- `unavailable` — requested planner/index/provider is disabled or unreachable;
- `unsafe` — authorization, trust-domain, or policy boundary rejects planning;
- `stale` — referenced state, recipe, procedure, mechanic, or schema changed.

`unknown` and `unsupported` are successful protocol outcomes, not thrown exceptions. They always
have receipts and make no state change.

### Resolution receipt

- receipt ID, interaction ID, timestamp, planner kind, optional model identity;
- application ID, application/base-order revision, and effective-source-set fingerprint;
- hash of the authorized intent envelope, not necessarily its unrestricted raw text;
- authorized scope/state revision used for planning;
- ordered searches with corpus, filters, and bounded query text;
- candidate qualified keys, authoritative IDs, versions, hashes, source-directory IDs, logical
  identities, lexical/vector/fusion ranks, and rejection reasons;
- authorized overlay evidence for effective winners and any rejected/shadowed source involved in
  resolution, with host paths redacted from remote projections;
- exact inspected contract references;
- chosen recipe reference if any;
- missing information and typed status/reason;
- closed proposed plan or no proposal;
- canonical proposal fingerprint;
- budgets consumed and fallback chain taken;
- redaction metadata for remote readers.

### Execution plan

- resolution receipt ID and proposal fingerprint;
- ordered step IDs and explicit dependencies;
- read-only query steps or existing action steps only in the initial delivery;
- application-scoped qualified keys plus exact procedure/mechanic IDs, versions, source hashes,
  effective-source-set fingerprint, role bindings, bounded JSON inputs, and expected state revisions;
- stop-on-failure policy;
- no arbitrary tool name, shell/network call, raw SQL, effect list, or model prompt.

Queries may run before commits. Unrelated commits retain their existing independent transaction
ownership. If several state changes require atomicity, they must be expressed through one existing
action/composition root; the orchestrator must not fake a distributed transaction.

### Execution receipt

- receipt/plan/fingerprint linkage and idempotency key;
- validation status for every step;
- operation IDs and safe summaries of action results;
- mechanic versions/hashes and deterministic seeds actually used;
- state revisions before/after where supported;
- completed, failed, skipped, stale, or replayed step status;
- partial-progress marker when earlier independent commits succeeded;
- final outcome and recovery/clarification advice;
- authorized/redacted view for the remote narrator.

### Recipe

- recipe ID/version/status: `candidate`, `verified`, `stale`, or `retired`;
- application ID, application/base-order revision, and effective-source-set fingerprint;
- intent examples and normalized retrieval text;
- typed slots/roles with constraints, never prior campaign entity IDs;
- exact procedure/mechanic/schema references and source hashes;
- parameterized query/action templates with no executable code;
- preconditions, expected postconditions, and stop conditions;
- provenance resolution/execution receipt IDs;
- catalog/application revision, winning source hashes, overlay fingerprint, and embedding generation;
- successful-use count, failure count, last validation, and invalidation reason.

A recipe never stores model system prompts, arbitrary instructions from scanned documents,
JavaScript, effects, credentials, private chain-of-thought, or previous campaign entity IDs.

## Planner and fallback policy

Recommended order:

```text
1. exact verified recipe lookup
2. deterministic lexical/exact feature lookup
3. optional local model planning loop
4. return typed non-resolution receipt to the remote model
5. accept a remote proposal through the same verifier
6. execute only after explicit authorization
```

The remote model is not invoked by the server in the initial delivery; it is the MCP client. If the
local planner is disabled or returns `unknown`, the response includes sufficient authorized search
and receipt evidence for that client to continue through feature-search/inspect queries and submit
a plan.

No automatic fallback may silently execute a lower-confidence plan. The transition from planning
to execution is explicit and fingerprinted.

## Trusted hybrid feature retrieval

### Source documents

First materialize the effective application view from the registered directory stack. Then
materialize one deterministic feature document per winning authoritative version from:

- active mechanic metadata, declared roles, input description/schema, scope, version, and hash;
- active procedure metadata, governed public operation, version, and hash;
- component schema identity/description needed to understand declared roles;
- verified recipe metadata and slot contract, in a distinct trusted recipe collection.

Do not index mechanic JavaScript source as natural-language instruction. Store its hash/reference
for validation and retrieve source only through the existing sandbox owner during execution.

The materializer records the application ID, qualified feature key, authoritative record ID,
source-directory ID, logical identity, winning hash, application/base-order revision, and
effective-source-set fingerprint. It does not emit shadowed definitions into the ordinary search
collection. An authorized diagnostic query may explain why one source shadowed another without
making the shadowed source executable.

### Search behavior

- require an application scope or an explicit `system` scope;
- resolve the effective directory/application overlay before candidate generation;
- exact qualified-key/authoritative-ID/version/hash matches first;
- filter to the selected application and its ordered bases; do not leak candidates from unrelated
  applications or blend `system` results without an explicit collection request;
- lexical/category matching is always available;
- vector similarity is optional and generation-scoped;
- fuse stable ranks with a documented deterministic method such as reciprocal-rank fusion;
- apply trust class, active status, ruleset/scope, record type, and authorization filters before
  returning candidates;
- return bounded results with scores/ranks and citations;
- hydrate selected candidates from the current authoritative stores before proposal or execution;
- reject stale source hashes, application revisions, source generations, and effective-set
  fingerprints, and never use cached document text as execution authority.

Both local and remote planners call this same server-owned search contract. Neither accesses the
SQLite/vector implementation directly.

### Derived index ownership

The vector index is disposable and component-owned, preferably in a separate SQLite file from the
game database. It stores generic collection/application/source/document/chunk keys, embedding
identity, content hash, and vector only. The trusted feature corpus adapter lives outside local AI
and maps effective catalog/application records into those generic documents. The local-AI API sees
opaque scope/source/logical keys and never interprets application names or override semantics.

Disabled/missing/wrong-dimension embeddings or vector extension must leave exact/lexical search
complete and deterministic.

## Recipe learning lifecycle

1. A server-validated remote or local plan executes successfully.
2. The server derives a parameterized recipe candidate from the validated plan and receipts. The
   model may suggest slot mappings, but the server reconstructs and validates them.
3. The candidate is stored append-only with exact contract hashes and is retrievable only as a
   candidate until promotion.
4. Promotion policy verifies that templates contain no fixed prior entity IDs, every step maps to
   current contracts, all provenance receipts succeeded, and no trust-domain content is embedded.
5. A verified recipe may be retrieved by intent. It must still bind current roles, rehydrate exact
   contracts, validate current state, and produce a new resolution receipt before execution.
6. Any referenced version/hash/schema change marks the recipe stale. A stale recipe cannot execute
   and falls back to ordinary discovery. A directory reorder, application-base change, or newly
   effective override also invalidates any recipe whose referenced winner or effective-set
   fingerprint changed.
7. Failures append evidence; they never rewrite or silently “repair” prior receipts.
8. Reviewed portable recipes may later be exported through an explicit synchronization boundary.
   Runtime learning does not edit authored catalog files.

Recommended initial promotion policy: **candidate after one successful execution; verified only by
explicit review**. Automatic promotion after repeated successes is a later policy decision, not part
of the first implementation.

## Dependency tree

```text
Auditable intent execution with reusable learning                         [planned]
├─ A. Generic application-kernel prerequisite                             [external dependency]
│  ├─ Application/source/type/state-space registries                       [planned by owner]
│  ├─ Effective manifest and trust-aware directory overlays                [planned by owner]
│  ├─ Application-scoped ECS and schema enforcement                        [planned by owner]
│  └─ Stable application/source/effective-set query contracts              [required before B]
├─ B. Contract and trust-boundary ratification                            [depends on A]
│  ├─ Component name/dependency direction                                 [proposed]
│  ├─ Status, intent, plan, receipt, and recipe schemas                    [proposed]
│  ├─ Local no-tools loop and explicit execution boundary                 [proposed]
│  ├─ Trusted/untrusted corpus separation                                 [proposed]
│  └─ Authorization/redaction port                                        [missing]
├─ C. Trusted feature retrieval                                           [planned; depends on A/B]
│  ├─ Effective-document materializer before indexing                     [ready after A/B]
│  ├─ Exact/lexical search                                                 [verified seam; needs generic owner]
│  ├─ Generic vector index                                                 [missing]
│  ├─ Stable hybrid fusion and current-store hydration                    [planned]
│  └─ Public server-mediated feature search                               [public gate]
├─ D. Receipts and idempotency                                             [planned; depends on B]
│  ├─ In-memory contracts/canonical fingerprints                          [ready after B]
│  ├─ Append-only resolution receipt store                                [migration gate]
│  ├─ Interaction execution receipt linked to operations                  [planned]
│  └─ Authorized/redacted receipt projection                              [depends on auth port]
├─ E. Symmetric planners                                                   [planned; depends on C/D]
│  ├─ Verified recipe resolver                                            [depends on G]
│  ├─ Deterministic lexical fallback                                      [planned]
│  ├─ Bounded local completion state machine                              [planned]
│  ├─ Typed local unknown/unavailable/needs-input outcomes                 [planned]
│  └─ Remote proposal submission through same verifier                    [public gate]
├─ F. Verified execution                                                   [planned; depends on D/E]
│  ├─ Plan semantic verifier and stale rehydration                        [missing]
│  ├─ Explicit fingerprinted execution authorization                      [public gate]
│  ├─ Existing IActionRunner delegation                                   [verified dependency]
│  ├─ Stop/partial-progress/replay behavior                               [planned]
│  └─ Narrator-safe receipt result                                        [depends on auth port]
├─ G. Recipe learning                                                      [planned; depends on D/F]
│  ├─ Candidate derivation from successful receipts                       [planned]
│  ├─ Append-only recipe store and provenance                             [migration gate]
│  ├─ Explicit review/promotion                                            [public/admin gate]
│  ├─ Hash/version/application/overlay invalidation                        [planned]
│  └─ Trusted recipe retrieval/index                                      [depends on C]
└─ H. Acceptance and independence                                         [planned; depends on A-G]
   ├─ Local disabled -> remote completes and candidate is recorded         [missing]
   ├─ Local unknown -> receipt explains searches and missing capability    [missing]
   ├─ Verified recipe -> current contracts revalidated and executed        [missing]
   ├─ Stale/poisoned/untrusted recipe/document -> no state change           [missing]
   ├─ Base definition overridden/removed -> deterministic winner changes    [missing]
   ├─ System/application/cross-application isolation                       [missing]
   ├─ Generic system build has no game pack                                [existing broader blocker]
   └─ Full suite/catalog/protocol evidence                                 [pending]
```

## Ordered implementation slices and model routing

OpenAI documentation describes Terra as the everyday workhorse and Sol as the stronger choice for
complex, ambiguous, or high-value work. The correctness gates below are model-independent; model
choice changes review depth, never authority.

| Order | Slice | Default model | Switch/review gate | Exit gate |
| ---: | --- | --- | --- | --- |
| Prerequisite | Application-kernel Slices 0–7: registries, effective manifest, schemas, and application-scoped ECS | Follow [kernel model routing](../application-kernel/APPLICATION-KERNEL-DEPENDENCY-PLAN.md#ordered-implementation-slices-and-model-routing) | Do not implement these owners inside orchestration. | Stable application/source/type/state-space ports and receipts are accepted. |
| 0 | Ratify orchestration threat model, component boundary, public two-phase flow, auth/redaction, persistence, and promotion policy against the accepted application kernel | **Sol High** recommended | User confirmation required. | Decisions recorded; Leaf B becomes verified; no runtime changes. |
| 1 | Internal orchestration contracts, canonical fingerprints, status taxonomy, guards, and in-memory fake stores | **Terra High** | Switch to Sol only if existing public contracts conflict or canonicalization has security ambiguity. | Contract tests cover every status, application/source revisions, bounds, deterministic fingerprints, and forbidden fields; no public kinds. |
| 2 | Effective trusted feature-document materialization and exact/lexical search | **Terra High** | Sol not normally needed. | Only effective application-scoped winners are indexed; current-store hydration and inactive/stale/shadowed rejection pass. |
| 3 | Generic vector index plus optional hybrid search | **Terra High** | Sol review if index ownership or trust-domain separation would change. | Non-game fixtures cover rebuild, generations, application isolation, dedupe, stable fusion, vector-disabled lexical parity, and separate corpora. |
| 4 | Append-only resolution receipt store and redacted projections | **Terra High** | **Sol High review before migration/public schema confirmation.** | Unknown/unavailable/unsafe/stale attempts and overlay provenance persist with no operation/state mutation; authorized views redact correctly. |
| 5 | Bounded local planner state machine using server-mediated search/inspect | **Sol High** recommended for first implementation | Terra may implement from a fully active slice, but switch to Sol on loop/state-machine ambiguity, prompt injection, or planner/authority leakage. | Round/budget/cancellation tests; every path ends in proposal or typed receipt; local provider has no tools/game dependency. |
| 6 | Common plan verifier and remote proposal adapter | **Terra High** | Sol review if remote and local validation paths diverge. | Identical proposal succeeds/fails identically for both planner kinds; forged app/source/ID/hash/effect/tool references reject. |
| 7 | Two-phase protocol and execution coordinator | **Sol High** recommended | **Sol required for final design review** of new public kinds, application-qualified keys, idempotency, partial progress, and authorization; user confirmation required before edits. | Plan-only makes no writes; explicit execute delegates only to existing actions; stale/replay/partial results have receipts; protocol walk passes. |
| 8 | Recipe candidate derivation and append-only storage | **Terra High** | Switch to Sol if slot generalization could preserve prior entity IDs or untrusted instructions. | Only successful receipts derive application-scoped bounded candidates; failures/old IDs/code/prompts are excluded. |
| 9 | Review/promotion, verified retrieval, and hash/application/overlay invalidation | **Sol High** recommended | Security/learning-policy review is high-value and cross-owner. | Candidate cannot execute; verified recipe revalidates current effective contracts; changed winner/ordering/hash makes it stale; review is audited. |
| 10 | Remote-disabled/local-disabled end-to-end matrix and final independence audit | **Sol Extra High** recommended | Use Terra for mechanical fixes found by the audit; return to Sol for acceptance judgment. | All acceptance cases pass on one worktree; receipt and roadmap status updated; deliberate exclusions recorded. |

### Can Terra implement all of it?

Yes, **Terra can implement every bounded slice** when its implementation document is active,
semantic decisions are already confirmed, and tests define the boundary. Do not ask Terra—or Sol—to
implement this master plan in one run. The plan intentionally requires a new implementation
document and receipt per slice.

Use Sol at Slice 0, the named review in Slice 4, and Slices 5, 7, 9, and 10 because these contain
architectural ambiguity, persistence security, public execution semantics, learning-policy
security, or final acceptance. Application/overlay/schema model gates are owned by the prerequisite
plan. If cost or availability requires Terra throughout, use Terra High and require a separate
review turn at every Sol-labelled gate.

## Slice acceptance matrix

Every implementation document must select relevant rows and make them concrete:

| Class | Required evidence |
| --- | --- |
| Positive | Exact recipe, lexical discovery, local proposal, remote proposal, and authorized execution each have a focused success case when their slice exists. |
| Non-resolution | `unknown`, `unsupported`, `needs-input`, `ambiguous`, `unavailable`, `unsafe`, and `stale` are typed, persisted when applicable, and make no state change. |
| Boundary | Local AI has no game/project dependency; planner cannot execute; executor cannot search arbitrary corpora; recipes cannot contain executable code/effects. |
| Namespace | `system.*` resolves only platform capabilities; `dnd2024.*` resolves only that application and confirmed bases; unrelated applications never leak into results. |
| Overlay | Higher eligible directory shadows the same logical identity before indexing; equal precedence conflicts; removal reveals the base; shadowed content cannot execute. |
| Trust | Untrusted scanned text that says “run this mechanic” never enters trusted feature search or a recipe. |
| Authorization | Planner sees only authorized context; receipt projection does not leak hidden candidates, prompts, state, or operation detail. |
| Current authority | Every selected contract is rehydrated and hash/version checked before proposal and again before execution. |
| Determinism | Canonical plan/receipt fingerprints, lexical ranking, hybrid fusion, and recipe invalidation are stable. |
| Vector fallback | Disabled/missing/wrong-dimension vector support yields the same complete lexical fallback and no state mutation. |
| Replay | Duplicate execution idempotency key/fingerprint returns the prior receipt and never repeats an action. |
| Partial progress | If step 2 fails after committed step 1, receipt reports step 1 committed and later steps skipped; no false rollback claim. |
| Atomicity | A required all-or-nothing change is one action/composition transaction, not multiple unrelated commits. |
| Learning | Only successful validated receipts create candidates; candidates never execute; verified recipes always revalidate. |
| Compatibility | Existing direct `query`/`commit(kind: "action")` clients still work when new orchestration is disabled. |
| Surface | Exactly three verbs remain; advertised kinds, dispatcher cases, examples, and protocol walk agree. |

## Confirmation gates

The following decisions must be confirmed before their slice becomes `active`:

1. Accept the application-kernel contracts needed by this plan: application scope, effective
   manifest/source fingerprint, exact type/contract versions, and authorized redacted projections.
2. Adopt `interaction-orchestration` as a system component and the dependency direction above.
3. Confirm the closed intent/status/receipt/plan/recipe semantics and whether raw intent text is
   stored or only a hash/redacted form.
4. Confirm an authorization/redaction port that is generic and supplied by the application/game
   adapter.
5. Confirm a separate local-AI derived-index SQLite location and trusted/untrusted collections.
6. Confirm migrations/tables for orchestration receipts and recipes, including retention and
   reconciliation with application/source revisions. Application/source tables remain kernel-owned.
7. Confirm new application-qualified public kinds while retaining exactly three verbs and
    compatibility for existing clients. Recommended initial system kinds:
   - `query(kind: "system.feature-search")`
   - `query(kind: "system.interaction-plan")`
   - `query(kind: "system.interaction-receipt")`
   - `commit(kind: "system.interaction-execute")`
   - an administrative recipe review route, whose exact placement remains to be decided.
8. Confirm two-phase plan/execute semantics and that local planning never implies execution consent.
9. Confirm candidate-after-success and explicit-review-only promotion for the first delivery.
10. Confirm whether successful remote plans are learned by default or only when the caller sets an
   explicit bounded `learn` flag. Recommended: explicit `learn` initially.
11. Confirm final feature acceptance after full catalog/build/test/protocol evidence.

## Implementation-document lifecycle

- Keep this dependency tree as the single master plan.
- Create **one** slice implementation document only when the preceding dependencies are verified
  and that slice's confirmation gates are closed.
- Mark only one orchestration slice `active` at a time.
- Each document must follow `docs/FEATURE_IMPLEMENTATION_AUTHORING.md`, name exact allowed files,
  use existing IDs unless separately confirmed, and stop at its own receipt.
- Do not pre-author all slice plans; prospective detail belongs here until its leaf is ready.
- Collapse completed leaf detail to a receipt link instead of growing implementation diaries.

## Planning receipt

- Runtime artifacts created: none.
- Catalog records, permanent IDs, schemas, migrations, database rows, and public kinds created: none.
- Existing owners reused: local AI, deterministic retrieval seam, procedures, mechanics,
  projection, actions, operations/audit, MCP protocol, and application/game authorization adapters.
- New proposed owners in this plan: interaction contracts/verifier/coordinator,
  resolution/execution receipts, trusted feature index, and recipe memory. Application/source/ECS
  owners are proposed by the application-kernel plan.
- Default implementation model: Terra High for bounded slices.
- Recommended Sol gates: application/ID/overlay semantics, architecture/threat model, first planner
  state machine, public execution semantics, learning/promotion security, and final acceptance.
- Deliberate stop: planning documents and roadmap links only.

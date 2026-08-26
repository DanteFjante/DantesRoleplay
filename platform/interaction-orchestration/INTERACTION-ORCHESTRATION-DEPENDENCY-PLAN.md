# Interaction orchestration dependency tree — auditable local/remote intent execution and learning

Status: **Slices 12A–12H and 13A–13E accepted; Slice 13F implemented awaiting final acceptance**
Ruleset alignment: **ruleset-neutral**  
Source: **not applicable**  
Owner: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Related owner: [Local intent routing](../../LOCAL_INTENT_ROUTING_PLAN.md)  
Prerequisite owner: [Generic application kernel](../application-kernel/APPLICATION-KERNEL-DEPENDENCY-PLAN.md)  
Implementation guide: [Agent implementation guide](INTERACTION-ORCHESTRATION-AGENT-GUIDE.md)
Proposed extension: [Slice 13 local/remote outer AI and bounded task batches](INTERACTION-ORCHESTRATION-SLICE-13-DEPENDENCY-PLAN.md)

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
   before retrieval; and
9. both planners can traverse described catalog directories, page through records, search
   deterministically, and inspect exact contracts when vectors and local AI are disabled;
10. a player-facing **outer AI** can narrate an application, delegate bounded mechanic work to an
    **inner AI**, or submit its own inspected proposal through the same verifier;
11. an inner AI can resolve and propose small batches from authoritative contracts while treating
    the outer AI as its application-facing guide, without becoming transaction authority;
12. successful outer- or inner-authored plans can produce reviewable candidate recipes so a future
    inner attempt can reuse the route without treating model prose as a contract; and
13. applications can host the outer conversation through one reusable application-scoped web
    component while navigation and application state remain owned by the containing page.

This plan does **not**:

- give either model direct database, vector-store, filesystem, shell, network, or unrestricted tool
  access;
- let a model invent or activate a procedure, mechanic, schema, JavaScript rule, permanent ID, or
  catalog contract;
- treat an embedding score, model confidence, learned recipe, or remote narration as authority;
- fine-tune or mutate local model weights;
- require embeddings, vector storage, or a local model for catalog/capability discovery;
- mix user-scanned information with trusted executable feature contracts;
- guarantee all-or-nothing semantics across unrelated commits;
- bypass authorization, stale-state validation, declared mechanic roles, effect validation,
  operation audit, or existing action transactions;
- add D&D meaning to C# or to the local-AI component;
- let an application override trusted system behavior, or let an untrusted document directory
  override an executable feature contract; or
- let the browser select an arbitrary model, reasoning effort, prompt, tool policy, or inner/outer
  privilege profile;
- expose the inner AI as a general player chat surface or let the outer AI bypass verification when
  it chooses to traverse contracts itself;
- turn one successful model-authored route directly into an authoritative catalog contract; or
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

### Inner and outer AI roles

The user-facing names describe responsibilities, not separate execution authorities:

| Role | Runtime profile | Responsibility | Allowed result |
| --- | --- | --- | --- |
| **Inner AI** | Codex `gpt-5.6-luna`, reasoning effort `low` | Resolve one bounded mechanic batch from authorized intent, searches, inspections, and verified recipes; ask the outer AI for a missing decision when needed. | Closed proposal or typed non-resolution receipt. |
| **Outer AI** | Codex `gpt-5.6-luna`, reasoning effort `high` | Hold the player-facing application conversation, reason about the broader situation, delegate bounded work to the inner AI, or traverse trusted contracts and submit its own proposal. | Narration, bounded delegation, closed proposal, or typed non-resolution. |

These are closed host-owned profiles. The web client chooses the product role it is authorized to
use, not the model, effort, developer instructions, sandbox, tools, or approval policy. Each turn
records the requested role, effective model/effort, application, interaction ID, and any parent
delegation ID. Resuming a conversation must preserve its role profile; a role cannot be changed by
editing a request.

The inner AI controls sequencing only in the product sense: it selects the next small batch and may
ask for another bounded observation. The server remains the sole authority for context resolution,
contract hydration, semantic verification, authorization, execution, receipts, and recipe
derivation.

For a D&D application, the outer AI is the game-master-facing conversational role and the inner AI
treats its bounded delegation as GM intent. That prompt/persona and D&D vocabulary belong to the
application adapter or catalog procedure, never to generic C# or the Codex bridge. Other
applications may supply different outer personas without changing orchestration contracts.

When the outer AI traverses the system itself, it uses the same application-scoped search and exact
inspection contracts available to the inner AI and submits the same closed plan shape. A successful
execution may create a candidate recipe. It does not author or modify an authoritative procedure,
mechanic, component schema, or catalog contract. Authoring such a contract remains a separate
administrative workflow with its own review and synchronization boundary.

### Reusable outer application surface

The outer AI is exposed through an application-scoped conversation port and a reusable browser-native
custom element. The exact public route and element name remain a confirmation gate, but the boundary
is closed:

- the containing application supplies its registered application ID, authorized state-space or
  session context, presentation options, and a bounded player message;
- the server derives identity, authorization, profile, current revisions, and contract access;
- the component renders the outer transcript, streaming state, typed requests for player input,
  safe execution progress, and narrator-safe receipt results;
- an outer-to-inner delegation stays server-side and appears in the player transcript only through
  a safe summary; and
- the component owns no game state, recipe state, Codex thread authority, or direct MCP transport.

The existing control-center assistant panel remains an administrative/debug consumer. The reusable
outer surface must use the same conversation/application service but must not inherit control-center
settings, filesystem, approval, or operator-only capabilities.

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
  codex-bridge/                     pinned app-server transport and closed role-profile application
  assistant-conversations/          durable role/application-scoped conversation evidence
  web-interface/                    reusable outer application component and administrative consumer
  mcp-protocol/                     public orient/query/commit adapters
src/applications/dantes-roleplay-host/
                                    planner selection, closed inner/outer profiles, and composition
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

codex-bridge
  -> assistant-conversations
  -> app-server protocol only
  -X-> mechanics, actions, game adapters, and orchestration persistence

application host role adapter
  -> interaction-orchestration public application port
  -> codex-bridge contracts

web-interface reusable outer component
  -> application host role adapter
  -> assistant-conversations read model
  -X-> orchestrator stores, action runner, Codex process, and MCP transport internals
```

The `interaction-orchestration` label is proposed, not yet confirmed. Do not create its directory or
manifest until the confirmation gate closes.

## Existing owners and evidence

| Concern | Owner | State | Evidence/constraint |
| --- | --- | --- | --- |
| Provider-neutral completion/embedding | `src/system/local-ai/DantesRoleplay.LocalAI` | verified | Standalone project, no game dependency; schema-bound completion and embeddings exist. |
| Codex app-server transport | `src/system/codex-bridge` | verified foundation, single profile | Pinned CLI transport, streaming, cancellation, approvals, and `gpt-5.6-luna` selection exist; the bridge has no role-bound reasoning profile yet. |
| Durable assistant conversations | `src/system/assistant-conversations` | verified foundation, role-unaware | Provider-neutral messages and Codex thread bindings exist; requests currently identify only provider, not application, inner/outer role, or delegation. |
| Browser assistant element | `src/system/web-interface/examples/control-center/index.html` | verified administrative consumer, not reusable application surface | Browser-native `<assistant-panel>` exists inside the control center; it is operator-scoped and cannot be reused as the application-play contract. |
| Codex reasoning override | pinned app-server v2 `TurnStartParams.effort` | verified external seam | Generated schema for pinned Codex `0.149.1` supports a turn reasoning-effort override; current bridge does not send it. |
| Local route proposal | `src/game-adapters/dantes-roleplay/local-routing` | verified foundation, too narrow | Returns `proposed`, `needs-input`, or `unknown`; never executes. It selects only the first current mechanic candidate. |
| Mechanic discovery/inspection | `IMechanicStore` and `MechanicStore` | verified | Active records, versions, source hashes, requirements, and ranked phrase lookup exist. |
| Procedure discovery/inspection | `IProcedureStore` and `ProcedureStore` | verified | Active records, versions, source hashes, and phrase lookup exist. |
| Role/input projection | `IProjectionResolver` | verified | Server materializes declared context and rejects missing/invalid roles before mechanics run. |
| Exact application action execution | `IApplicationActionRunner` / `ApplicationActionRunner` | verified | Owns exact current application mechanic evaluation, generic effect mapping, atomic ECS transaction, deterministic replay identity, and audit. The legacy `IActionRunner` retains only its existing direct-action compatibility path. |
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
- **Outer AI** — application-facing Codex guide that narrates, delegates, or submits a closed plan
  after trusted traversal. It never bypasses the verifier.
- **Inner AI** — role-bound Codex planner that resolves one bounded mechanic batch and returns a
  proposal or typed non-resolution. It never executes directly.
- **Delegation** — one outer-to-inner child resolution bound to the parent interaction, authorized
  application context, fixed profile, limits, and safe return summary.

Do not use “contract” as a synonym for recipe. A contract is authoritative; a recipe is not.

## Proposed closed contracts

The following are conceptual shapes. Exact names and serialized schemas require confirmation in
Slice 12B before becoming public or permanent.

### Intent envelope

- client idempotency key;
- trimmed intent text;
- required or server-derived application ID, plus optional explicit system-collection request;
- optional application/ruleset scope hint treated only as a search filter and never as authority;
- optional named role hints or already-resolved entity references;
- bounded conversation facts already authorized for planning;
- maximum planned steps and planner preference;
- plan-only or execute-after-explicit-authorization mode.

The host also binds a closed AI role profile (`inner` or `outer`), effective model/reasoning effort,
application conversation ID, and optional parent delegation ID. These values are trusted host
context, not caller-authored plan fields.

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

- receipt ID, interaction ID, optional parent delegation ID, timestamp, planner kind, AI role, and
  effective model/reasoning profile;
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

### Role-bound application conversation

- registered application ID and authorized application/session context reference;
- immutable role profile (`outer` for the public reusable surface, `inner` only for server-created
  delegations);
- provider conversation/thread reference bound to that role profile;
- parent interaction/delegation linkage and bounded safe context transferred between roles;
- per-turn effective model, effort, limits, status, receipts, and narrator-safe result;
- no raw hidden prompt, chain-of-thought, model-select field, arbitrary tool policy, filesystem
  capability, or caller-supplied authorization.

Only the server may create an inner conversation. An application-facing caller creates or resumes an
outer conversation through the reusable application port. An administrative consumer may inspect
authorized diagnostics, but it does not acquire the application player's authority by doing so.

## Planner and fallback policy

Recommended order:

```text
1. exact verified recipe lookup
2. exact qualified-key/record lookup
3. deterministic described catalog browse and lexical branch search
4. optional vector-assisted rank fusion
5. optional local model planning loop
6. return typed non-resolution receipt to the remote model
7. accept a remote proposal through the same verifier
8. execute only after explicit authorization
```

The original remote path remains available to an external MCP client. The confirmed product
extension also permits the application host to invoke role-bound Luna planning through a dedicated
schema-only, no-tools Responses adapter, but only after Slice 12E is confirmed and accepted. The
existing pinned app-server bridge remains a separate private-operator coding-agent integration and
is not eligible for product planning unchanged. An outer role may continue through
feature-search/inspect and submit a plan; an inner role is fed only bounded server-mediated
observations. Both use the common verifier, receipt, and later execution path. Neither provider
transport nor browser component becomes execution authority.

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
- expose application-kernel catalog collections and described directory nodes with stable cursor
  pagination, breadcrumbs, direct/subtree counts, and exact record inspection;
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
and catalog traversal complete and deterministic. Disabling local completion must not change browse,
pagination, exact inspection, or lexical results.

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
9. A successful outer-authored plan and a successful inner-authored plan use the same candidate
   derivation. The source AI role is provenance, not a promotion shortcut.
10. An outer AI may request contract authoring only through a separately authorized administrative
    workflow. Gameplay learning never writes catalog contracts or source files.

Recommended initial promotion policy: **candidate after one successful execution; verified only by
explicit review**. Automatic promotion after repeated successes is a later policy decision, not part
of the first implementation.

## Dependency tree

```text
Auditable intent execution with reusable learning                         [accepted; Slices 12A–12H]
├─ A. Generic application-kernel prerequisite                             [verified; Slice 12A]
│  ├─ Application/source/type/state-space registries                       [accepted by owner]
│  ├─ Effective manifest and trust-aware directory overlays                [accepted by owner]
│  ├─ Application-scoped ECS, edges, schema enforcement, and evaluation    [accepted by owner]
│  └─ Stable application/source/effective-set query contracts              [accepted by owner]
├─ B. Contract and trust-boundary ratification                            [accepted; Slice 12B]
│  ├─ Component name/dependency direction                                 [accepted]
│  ├─ Closed intent/status/proposal and execution-consent contracts        [accepted]
│  ├─ Product-provider no-tools and explicit execution boundary            [accepted]
│  ├─ Closed inner/outer Luna profiles and delegation linkage              [accepted]
│  ├─ Future trusted/untrusted storage separation                          [accepted boundary; implementation deferred]
│  └─ Generic application authorization port                               [accepted; policy composition deferred]
├─ C. Trusted feature retrieval                                           [accepted; Slice 12C; depends on A/B]
│  ├─ Effective-document materializer with source-winner trust            [accepted]
│  ├─ Described catalog traversal/exact inspection                        [application-kernel prerequisite]
│  ├─ Exact/lexical search and vector-disabled fallback                   [accepted]
│  ├─ Disposable SQLite vector generation index                           [accepted]
│  ├─ Stable hybrid fusion and current-store hydration                    [accepted]
│  └─ Public server-mediated feature search                               [accepted; Slice 12F]
├─ D. Receipts and idempotency                                             [accepted; Slice 12D; depends on B]
│  ├─ In-memory contracts/canonical fingerprints                          [accepted; Slice 12B]
│  ├─ Append-only resolution receipt store                                [accepted]
│  ├─ Interaction execution receipt linked to operations                  [accepted; Slice 12F]
│  └─ Authorized/redacted receipt projection                              [accepted]
├─ E. Symmetric planners                                                   [accepted; Slice 12E; depends on C/D]
│  ├─ Verified recipe resolver port                                       [accepted empty implementation until G]
│  ├─ Deterministic lexical fallback                                      [accepted]
│  ├─ Bounded local completion state machine                              [accepted]
│  ├─ Role-bound inner Luna small-batch planner                            [accepted]
│  ├─ Role-bound outer Luna direct-proposal adapter                        [accepted; conversation/delegation in F]
│  ├─ Typed local unknown/unavailable/needs-input outcomes                 [accepted]
│  └─ Remote proposal submission through same verifier                    [internal seam accepted; public gate in F]
├─ F. Verified execution                                                   [accepted; Slice 12F; depends on D/E]
│  ├─ Common verifier and execution-time stale rehydration                 [ready]
│  ├─ Explicit fingerprinted execution authorization                      [confirmed]
│  ├─ Exact application action transaction owner                          [ready cross-owner correction]
│  ├─ Stop/partial-progress/at-most-once replay behavior                  [ready]
│  └─ Narrator-safe receipt result                                        [ready with host auth policy]
├─ G. Recipe learning                                                      [accepted; Slice 12G; depends on D/F]
│  ├─ Candidate derivation from successful receipts                       [accepted]
│  ├─ Append-only recipe store and provenance                             [accepted]
│  ├─ Explicit review/promotion                                            [accepted]
│  ├─ Hash/version/application/overlay invalidation                        [accepted]
│  └─ Trusted recipe retrieval/index                                      [accepted]
├─ H. Reusable outer application surface                                  [accepted; Slice 12F; depends on D-F]
│  ├─ Application-scoped outer conversation port                           [ready; ephemeral first delivery]
│  ├─ Server-only correlated outer-to-inner delegation                     [ready]
│  ├─ Browser-native reusable outer conversation component                 [ready]
│  └─ Control-center diagnostic consumer without application authority      [partial foundation]
└─ I. Acceptance and independence                                         [accepted; Slice 12H; depends on A-H]
   ├─ Local disabled -> remote completes and candidate is recorded         [accepted]
   ├─ Outer Luna High -> inner Luna Low delegation stays bounded            [accepted]
   ├─ Outer direct traversal -> same verifier and candidate lifecycle       [accepted]
   ├─ Application page -> reusable outer component preserves shell/context  [accepted]
   ├─ Local unknown -> receipt explains searches and missing capability    [accepted]
   ├─ Verified recipe -> current contracts revalidated and executed        [accepted]
   ├─ Stale/poisoned/untrusted recipe/document -> no state change           [accepted]
   ├─ Base definition overridden/removed -> deterministic winner changes    [accepted]
   ├─ System/application/cross-application isolation                       [accepted]
   ├─ Generic system build has no game pack                                [accepted; compile wildcards removed and guarded]
   └─ Full suite/catalog/protocol evidence                                 [accepted]
```

## Lowest ready leaf

Slice 12B is **accepted** through its
[implementation document](INTERACTION-ORCHESTRATION-SLICE-12B-IMPLEMENTATION.md) and
[receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12B-RECEIPT.md). Slice 12C is accepted through
its [implementation document](INTERACTION-ORCHESTRATION-SLICE-12C-IMPLEMENTATION.md) and
[receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12C-RECEIPT.md). Slice 12D is accepted through
its [implementation document](INTERACTION-ORCHESTRATION-SLICE-12D-IMPLEMENTATION.md) and
[receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12D-RECEIPT.md). Slice 12E is accepted through its
[implementation document](INTERACTION-ORCHESTRATION-SLICE-12E-IMPLEMENTATION.md) and
[receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12E-RECEIPT.md). Slice 12F is accepted through its
[implementation document](INTERACTION-ORCHESTRATION-SLICE-12F-IMPLEMENTATION.md) and
[receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12F-RECEIPT.md). It closes the public kinds,
exact two-phase consent, application authorization, process-local conversation lifetime, reusable
element/routes, exact application action owner, at-most-once/partial progress, and protocol
evidence. Slice 12G is accepted through its
[implementation document](INTERACTION-ORCHESTRATION-SLICE-12G-IMPLEMENTATION.md) and
[receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12G-RECEIPT.md). It adds explicit opt-in recipe
learning, append-only evidence and review, value-free role-slot templates, private traversal, and
current-authority verified reuse. Slice 12H is accepted through its
[implementation document](INTERACTION-ORCHESTRATION-SLICE-12H-IMPLEMENTATION.md) and
[receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12H-RECEIPT.md). It closes combined acceptance and
independence. No interaction-orchestration leaf remains.

## Ordered implementation slices and model routing

OpenAI documentation describes Terra as the everyday workhorse and Sol as the stronger choice for
complex, ambiguous, or high-value work. The correctness gates below are model-independent; model
choice changes review depth, never authority.

This table selects the **implementation agent** for each repository slice. It is separate from the
accepted **runtime product profiles**: inner Codex uses Luna Low and outer Codex uses Luna High.

Slice 12 has exactly **eight subslices, 12A–12H**. All eight are accepted, so **none remain**. This
consolidates the former prospective orchestration labels into capability-complete phases; do not
split them further unless a concrete transaction, authority, or confirmation conflict makes one
phase impossible to accept coherently.

| Order | Slice | Default model | Switch/review gate | Exit gate |
| ---: | --- | --- | --- | --- |
| Prerequisite | Application-kernel Slices 0–11 and accepted Slice 12A read handoff | Follow [kernel model routing](../application-kernel/APPLICATION-KERNEL-DEPENDENCY-PLAN.md#ordered-implementation-slices-and-model-routing) | Do not reimplement registry, ECS, catalog, or sandbox owners inside orchestration. | Stable application/source/type/state-space/catalog/execution ports and receipts are accepted. |
| 12A | Planner-neutral catalog handoff and zero/two-application host independence | **Terra High** | **Accepted 2026-08-24**; [receipt](../application-kernel/receipts/APPLICATION-KERNEL-SLICE-12A-RECEIPT.md). | Direct and remote traversal/inspection are application-isolated and complete without vectors or local AI. |
| 12B | Ratify the threat model, component/authority boundary, two-phase consent, auth/redaction, persistence/retention, recipe promotion, immutable AI roles, and reusable outer surface; then freeze bounded internal contracts and fakes | **Sol High** | **Accepted 2026-08-24**; [receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12B-RECEIPT.md). | Every status/envelope/fingerprint/role/delegation boundary is deterministic and tested in memory; no migration or public kind exists. |
| 12C | Materialize effective trusted feature documents and provide exact, lexical, and optional vector/hybrid retrieval | **Terra High** | **Accepted 2026-08-24**; [receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12C-RECEIPT.md). | Only current effective application winners are retrievable; separate trusted/untrusted corpora, deterministic ranking, rebuild generations, isolation, and vector-disabled lexical parity pass. |
| 12D | Persist append-only resolution/execution receipts with authorized redacted projections | **Terra High** | **Accepted 2026-08-24 after review remediation**; [receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12D-RECEIPT.md). | Every success and typed non-resolution persists provenance without state mutation; retention, replay identity, rollback, and redaction tests pass. |
| 12E | [Produce proposals through the bounded local planner, common local/remote verifier, and immutable inner/outer role adapter](INTERACTION-ORCHESTRATION-SLICE-12E-IMPLEMENTATION.md) | **Sol High** | **Accepted 2026-08-24**; [receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12E-RECEIPT.md). | Both planners use the same search/inspect/verifier path; forged/stale references reject; inner Luna Low and outer Luna High cannot change role or gain execution/tools; no action executes yet. |
| 12F | [Add the confirmed two-phase protocol/execution coordinator and application-scoped outer conversation/web surface](INTERACTION-ORCHESTRATION-SLICE-12F-IMPLEMENTATION.md) | **Sol High** | **Accepted 2026-08-24**; [receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12F-RECEIPT.md). | Planning changes no application state; explicit execution delegates only to the exact application action owner; receipts cover stale/replay/partial results; a non-control-center page can host the outer flow without operator authority. |
| 12G | [Derive, review, retrieve, and safely reuse parameterized recipes](INTERACTION-ORCHESTRATION-SLICE-12G-IMPLEMENTATION.md) | **Sol High** | **Accepted 2026-08-24**; [receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12G-RECEIPT.md). | Only successful validated receipts produce candidates; candidates never execute; promotion is audited; old entity IDs/code/prompts are excluded; every reuse revalidates current authority. |
| 12H | [Run the complete disabled-provider, role-isolation, application-isolation, replay, learning, embedded-play, and independence acceptance matrix](INTERACTION-ORCHESTRATION-SLICE-12H-IMPLEMENTATION.md) | **Sol xhigh** | **Accepted 2026-08-25**; [receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12H-RECEIPT.md). | All Slice 12 acceptance rows pass together; completion receipt and roadmap close Slice 12 with deliberate exclusions. |

### Can Terra implement all of it?

Terra can implement bounded mechanical work once semantics are confirmed and tests close the
boundary. Use Sol High for 12B, 12E, 12F, and 12G because they contain architectural authority,
planner isolation, public execution, or learning-policy decisions. Use Sol xhigh for 12H acceptance.
If cost or availability requires Terra throughout, use Terra High but require a separate Sol review
turn at every Sol-labelled gate. Never implement all remaining subslices in one undifferentiated run.

## Slice acceptance matrix

Every implementation document must select relevant rows and make them concrete:

| Class | Required evidence |
| --- | --- |
| Positive | Exact recipe, lexical discovery, local proposal, remote proposal, and authorized execution each have a focused success case when their slice exists. |
| Non-resolution | `unknown`, `unsupported`, `needs-input`, `ambiguous`, `unavailable`, `unsafe`, and `stale` are typed, persisted when applicable, and make no state change. |
| Boundary | Local AI has no game/project dependency; planner cannot execute; executor cannot search arbitrary corpora; recipes cannot contain executable code/effects. |
| Role profiles | Inner and outer are immutable host-owned roles; Luna Low/High is applied and recorded respectively; the browser cannot override model, effort, prompt, sandbox, tools, or approval policy. |
| Delegation | Only an authorized outer interaction can create a bounded inner child; parent/application/context linkage is stable; inner output returns through the verifier and only a safe summary reaches the player transcript. |
| Outer direct path | Outer contract traversal and proposal submission use the same search, hydration, verifier, execution, receipt, and candidate-recipe lifecycle as an inner proposal. |
| Application surface | A reusable outer component works within a non-control-center application page, preserves the containing navigation/context, and has no operator settings, filesystem, raw Codex, inner-direct, or MCP authority. |
| Namespace | `system.*` resolves only platform capabilities; `dnd2024.*` resolves only that application and confirmed bases; unrelated applications never leak into results. |
| Overlay | Higher eligible directory shadows the same logical identity before indexing; equal precedence conflicts; removal reveals the base; shadowed content cannot execute. |
| Trust | Untrusted scanned text that says “run this mechanic” never enters trusted feature search or a recipe. |
| Authorization | Planner sees only authorized context; receipt projection does not leak hidden candidates, prompts, state, or operation detail. |
| Current authority | Every selected contract is rehydrated and hash/version checked before proposal and again before execution. |
| Determinism | Canonical plan/receipt fingerprints, lexical ranking, hybrid fusion, and recipe invalidation are stable. |
| Vector fallback | Disabled/missing/wrong-dimension vector support yields the same complete lexical fallback and no state mutation. |
| No-AI discovery | With embeddings, vector storage, and local completion disabled, a remote planner can traverse described catalog pages, search lexically, inspect exact contracts, and submit the same valid proposal. |
| Replay | Duplicate execution idempotency key/fingerprint returns the prior receipt and never repeats an action. |
| Partial progress | If step 2 fails after committed step 1, receipt reports step 1 committed and later steps skipped; no false rollback claim. |
| Atomicity | A required all-or-nothing change is one action/composition transaction, not multiple unrelated commits. |
| Learning | Only successful validated receipts create candidates; candidates never execute; verified recipes always revalidate. |
| Compatibility | Existing direct `query`/`commit(kind: "action")` clients still work when new orchestration is disabled. |
| Surface | Exactly three verbs remain; advertised kinds, dispatcher cases, examples, and protocol walk agree. |

## Confirmation gates

The following decisions must be confirmed before their slice becomes `active`:

1. **Slice 12B:** Accept the application-kernel contracts needed by this plan: application scope, effective
   manifest/source fingerprint, exact type/contract versions, and authorized redacted projections.
2. **Slice 12B:** Adopt `interaction-orchestration` as a system component and the dependency direction above.
3. **Slice 12B:** Confirm the closed intent/status/receipt/plan/recipe semantics and whether raw intent text is
   stored or only a hash/redacted form.
4. **Slice 12B:** Confirm an authorization/redaction port that is generic and supplied by the application/game
   adapter.
5. **Slice 12C:** Confirm a separate local-AI derived-index SQLite location and trusted/untrusted collections.
6. **Slices 12D/12G:** Confirm migrations/tables for orchestration receipts and recipes, including retention and
   reconciliation with application/source revisions. Application/source tables remain kernel-owned.
7. **Slice 12F:** Confirm new application-qualified public kinds while retaining exactly three verbs and
    compatibility for existing clients. Recommended initial system kinds:
   - `query(kind: "system.feature-search")`
   - `query(kind: "system.interaction-plan")`
   - `query(kind: "system.interaction-receipt")`
   - `commit(kind: "system.interaction-execute")`
   - an administrative recipe review route, whose exact placement remains to be decided.
8. **Slices 12B/12F:** Confirm two-phase plan/execute semantics and that local planning never implies execution consent.
9. **Slices 12B/12G:** Confirm candidate-after-success and explicit-review-only promotion for the first delivery.
10. **Slice 12G:** Confirm whether successful remote plans are learned by default or only when the caller sets an
   explicit bounded `learn` flag. Recommended: explicit `learn` initially.
11. **Slice 12F:** Confirm the exact application conversation route, reusable custom-element name, authorization
    context, and whether outer conversations are durable per player/session or explicitly ephemeral.
12. **Slice 12E:** Confirm the dedicated schema-only provider isolation mechanism for inner and
    outer planning turns. The recommended implementation uses stateless OpenAI Responses requests
    with no tools and does not reuse the operator Codex app-server bridge. Prompt instructions alone
    are insufficient; the active slice must prove the model cannot acquire filesystem, shell,
    network tools, arbitrary MCP, approval, or direct mechanic-execution authority.
13. **Slice 12H:** Confirm final feature acceptance after full catalog/build/test/protocol evidence.

Confirmed product constraints from the user on 2026-08-24:

- the system has distinct inner and outer Codex interfaces;
- inner uses `gpt-5.6-luna` with `low` reasoning and handles small bounded mechanic batches;
- outer uses `gpt-5.6-luna` with `high` reasoning and owns the application/player conversation;
- outer may delegate to inner or traverse trusted contracts and submit a plan itself;
- a successful new route enters the candidate-recipe/review lifecycle so inner can reuse it later;
  and
- the outer interface must be reusable inside application pages, including a D&D play page.

These constraints close the product-role choice, not the outstanding storage, public identifier,
authorization, isolation, execution-consent, retention, or promotion gates above.

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
  resolution/execution receipts, trusted feature index, recipe memory, role-bound Codex adapter,
  application conversation port, and reusable outer application component. Application/source/ECS
  owners are supplied by the application-kernel plan.
- User-confirmed runtime profiles: inner `gpt-5.6-luna`/`low`; outer
  `gpt-5.6-luna`/`high`.
- Official GPT-5.6/Responses documentation verifies Luna, turn-level effort, strict structured
  output, and a no-tools request. Existing pinned Codex `0.149.1` remains the separate operator
  coding-agent bridge and is deliberately ineligible for Slice 12E product planning unchanged.
- Default implementation model: Terra High for bounded slices.
- Recommended Sol gates: application/ID/overlay semantics, architecture/threat model, first planner
  state machine, public execution semantics, learning/promotion security, and final acceptance.
- Deliberate stop: planning documents and roadmap links only.

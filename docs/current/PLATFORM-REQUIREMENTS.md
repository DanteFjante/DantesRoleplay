# Current platform requirements

Implementation baseline reviewed: 2026-09-11.

This document describes the application-independent capabilities and constraints present in the
current working tree. It provides a baseline for evaluating libraries, architectural changes, or
a replacement implementation. Requirements below express the behavior that the current platform
is designed to preserve; implementation notes explain how it currently provides that behavior.

The review uses implementation code and contracts, including uncommitted work. It does not assert
that every path has been accepted or deployed. Application gameplay, application-specific websites
and content, and test inventories are outside scope. This is a current-state description, not an
implementation plan or a list of proposed features.

## 1. Platform purpose and authority

The platform hosts applications whose state shape, rules, queries, and published content can be
authored as data. People and AI clients use a shared execution kernel through web, MCP, and
in-process interfaces.

- The C# kernel must own generic execution, identity, storage, validation, authorization,
  transactions, retrieval, and audit. Application rule calculations and branching belong in
  catalog JavaScript and application declarations.
- Authored development content belongs in the catalog. Runtime changes and content authored only
  through runtime interfaces belong in SQLite until explicitly synchronized with files.
- A browser view, AI response, search result, or cached projection must not become an independent
  authority for application state.
- The platform must distinguish an application's identity, its registered revision, its effective
  activated content, and a particular runtime state space. Fingerprints bind work to the exact
  definitions used.
- Narrative continuity, notifications, documents, and operational records may be durable without
  being ECS state. Their owning stores determine their meaning and lifecycle.

| Information | Current storage and authority |
| --- | --- |
| Applications, namespaces, registrations, activation records, schemas | SQLite records and immutable versions/fingerprints where declared |
| Runtime entities, component values, containment, relationships | SQLite; JSON component payloads under registered schemas |
| Events, operations, notifications, tasks, conversations, knowledge | SQLite in the corresponding subsystem's tables |
| Published website HTML and revision-bound assets | SQLite web-content tables; asset payloads deduplicated by content hash |
| Runtime image uploads | SQLite metadata and upload sessions; immutable bytes in filesystem blob storage |
| Authored source files | Catalog and registered allowed roots; file-backed active reads check retained content fingerprints |
| Embeddings | Separate, disposable SQLite index under the derived-data directory |
| Query plans, catalog snapshots, media tickets, browser state | Derived caches or temporary client state; never substitutes for the owning records |

Implementation anchors: [host composition](../../DantesRoleplay.MCPServer/Program.cs),
[application/state-space contracts](../../src/system/application-registry/domain/ApplicationContracts.cs),
[storage paths](../../DantesRoleplay.MCPServer/RuntimeStoragePaths.cs).

## 2. Runtime authoring and controlled dynamism

- The platform must support authored procedures, JavaScript mechanics, component schemas, event
  types, subscriptions, registered queries/objects, and application content without embedding
  their application-specific meaning in C#.
- Versioned authored records must retain the source and metadata of earlier revisions. A new
  accepted revision must be identifiable by version and content fingerprint.
- Discovery must expose lifecycle status, descriptions, aliases or matching phrases, ownership,
  and exact identities. Ordinary discovery must respect enabled/active and effective-content rules.
- Namespaces must declare ownership, permitted record kinds, review state, and discovery metadata.
  Runtime writes must respect the namespace registry rather than treating every dotted ID as valid.
- Runtime authoring and file authoring must coexist. Editing a file must not silently overwrite an
  independently edited database record.

| Can change through supported data/content operations | Still requires host implementation work |
| --- | --- |
| Component schemas and application entity composition | New generic storage/effect primitives |
| JavaScript mechanics and declared context/composition | New sandbox capabilities or native integrations |
| Registered query/object declarations within supported profiles | New query executors, schema dialect features, or adapter implementations |
| Application/source/extension registrations and selected content | New HTTP route families and system capability handlers |
| Published HTML, scripts, styles, and asset bundles | New host-provided browser components or automatic form-rendering features |
| Subscriptions, trigger definitions, workflow/recipe data | New trigger adapters, AI providers, or tool implementations |

These changes use explicit registration, validation, versioning, and activation boundaries. The
system does not promise unrestricted executable plug-in loading or arbitrary hot replacement of
host code.

Implementation anchors: [mechanic ownership/versioning](../../src/system/mechanics/domain/Mechanic.cs),
[namespace contracts](../../src/system/catalog/domain/CatalogNamespaceContracts.cs),
[registered system dispatch](../../src/system/system-capabilities/persistence/SystemCapabilityCatalog.cs).

## 3. Applications, sources, extensions, and activation

- Applications must have stable identities and revisioned registrations, including declared base
  applications. Runtime state spaces retain their exact application binding.
- File sources must be registered against configured allowed roots, with relative path/glob,
  logical identity, trust, and precedence. Source retirement retains its identity and reason.
- Extensions must declare their sources, namespace contributions, dependencies, conflicts, and
  ordering. Effective resolution must be deterministic and reject ambiguous or invalid combinations.
- Preview must identify winning and shadowed content and report invalid sources or declarations
  without activating the candidate.
- Activation must bind the candidate to exact source/content and dependency fingerprints, check
  the expected active version, and require matching preview evidence. Stale preview evidence must
  fail explicitly. Successful activation retains an immutable record and operation identity.
- Exact qualified-ID inspection and ordinary effective-content discovery must remain distinct.
  Diagnostic access may explain shadowed or unavailable records.
- File-backed active documents must remain inside their allowed roots and match retained length
  and content hash. File drift must be reported instead of silently changing an active definition.

Implementation anchors: [source contracts](../../src/system/source-registry/domain/SourceContracts.cs),
[extension contracts](../../src/system/source-registry/domain/ExtensionContracts.cs),
[activation](../../src/system/application-activation/persistence/ApplicationActivationService.cs),
[active document reads](../../src/system/application-activation/persistence/ActivatedApplicationDocumentReader.cs).

## 4. Dynamic state, schemas, relationships, and lifecycle

- Entity identity must be scoped to a state space. Entities gain state by receiving independently
  registered components rather than application-specific C# object classes.
- Component types must use accepted, bounded JSON Schemas. A component value identifies its exact
  qualified type, schema version, schema hash, and current value revision.
- Schema revisions must be immutable; an identical normalized schema may reuse an existing version.
  Revising a type does not implicitly migrate existing component values to that version.
- Writes must validate JSON, type ownership, and expected revisions. Changing an existing
  component's immutable type contract must not be disguised as an ordinary value update.
- Generic schema annotations must support semantic roles and constraints, including cardinality,
  required roles/components, and uniqueness over declared JSON paths.
- Containment must support one container per entity, slot metadata, revision checks, and cycle
  rejection. Directed relationships must retain source, target, qualified kind, JSON payload, and
  revision. Edge endpoints must satisfy the owning state-space and lifecycle rules.
- Disabling an entity must preserve its identity and exclude it from ordinary discovery.
  Re-enabling must revalidate constraints. Permanent deletion must require a disabled identity
  without prohibited references. Identity correction must report immutable-reference blockers.

Current component rows hold the current value and revision counter. Immutable schema history and
operation/event evidence do not constitute a universal historical-version store for every value.

Implementation anchors: [schema registry](../../DantesRoleplay.DataAccess/Ecs/SqliteComponentTypeRegistry.cs),
[component store](../../DantesRoleplay.DataAccess/Ecs/SqliteApplicationScopedEcsStore.cs),
[constraints](../../DantesRoleplay.DataAccess/Ecs/SqliteEcsRoleConstraintValidator.cs),
[edges](../../src/system/state-space-edges/persistence/SqliteStateSpaceEdgeStore.cs),
[lifecycle](../../DantesRoleplay.DataAccess/Ecs/SqliteEcsLifecycleStore.cs).

## 5. Declared reads, objects, and controlled writes

- Mechanic context must be materialized from declared roles, required/optional components,
  references, objects, and supported graph selections. Missing optional state must remain
  distinguishable from a required input that makes execution unavailable.
- Registered projections and objects must describe their sources, field mappings, dependencies,
  relationships, collection ordering/paging, access perspectives, and resource limits.
- Read results must retain the source/version evidence needed to explain and validate them.
  Field-based objects can expose field availability and source provenance without making the
  returned presentation object writable authority.
- Query roles derived from caller input or other query results must go through the declared role
  binding and authorization path. A supplied entity ID does not prove access to that entity.
- Query contracts must distinguish output that is model-visible from output used only for binding
  later steps. Read-model mechanics must reject effects, events, and notifications, including
  proposals from their composed children.
- Object edits must use declared writable paths and supported operations. The host must validate
  source revisions, map accepted edits or submitted-object differences back to owning components
  or relationships, apply typed effects, and return fresh source evidence.
- Compiled plans may be cached by immutable declaration identity. Materialized results must use
  current authorized data. The existing plan cache deduplicates dependencies and supports batched
  source reads without caching application values as authority.

Implementation anchors: [object contracts](../../src/system/projection-materialization/domain/ApplicationObjectContracts.cs),
[query contracts](../../src/system/catalog-navigation/domain/ApplicationQueryContracts.cs),
[materialization](../../src/system/projection-materialization/persistence/ProjectionMaterializer.cs),
[object writes](../../src/system/projection-materialization/persistence/ApplicationObjectWriteService.cs),
[plan cache](../../src/system/projection-materialization/persistence/ProjectionPlanCache.cs).

## 6. Sandboxed rules and transactional effects

- Rule code must execute with a declared JSON input/context and produce a result and proposed typed
  effects. It must not receive a database, filesystem, network client, or CLR object graph.
- The current Jint engine uses a fresh execution realm per invocation, freezes projected input,
  supplies seeded random helpers, and imposes statement, time, memory, recursion, log, and output
  limits. Syntax errors and limit failures become structured execution failures.
- Composite mechanics must use declared child identities and available exact version/fingerprint
  constraints, bounded dependency graphs, validated bindings, and derived execution identities and
  seeds. Children cannot arbitrarily discover and invoke host functionality.
- Before committing effects, the host must revalidate read evidence and write preconditions.
  Evidence includes component revisions, entity state, and relevant collection membership,
  including previously absent components or empty collections.
- An owning ECS transaction must apply ordered effects, validate final constraints, stage resulting
  events/reactions and registered transaction participants, and record audit/recovery evidence
  together. Failure must roll back that transaction's changes.
- Execution identities must support idempotent replay and reject reusing an identity for another
  request. Resource limits must also bound reaction cascades.

The atomic boundary matters: one composed mechanic/effect transaction can include its child
effects and reactions. A multi-step interaction plan executes individual actions and stops on
failure; earlier successful actions may already be committed. Likewise, the current ECS dry-run
path applies root effects and rolls back before event-source and reaction routing, so it is not a
complete simulation of all eventual consequences.

Current default mechanic limits include 100,000 statements, two seconds, 8 MiB engine memory,
and recursion depth 64; the read-model profile allows 16 MiB. These are execution profiles, not
response-time or capacity promises. Seeded helpers support reproducible authored behavior;
arbitrary JavaScript or model output is not thereby guaranteed deterministic.

Implementation anchors: [sandbox](../../DantesRoleplay.DataAccess/Mechanics/JintMechanicEngine.cs),
[execution profiles](../../src/system/mechanics/domain/IMechanicEngine.cs),
[mechanic evaluator](../../src/system/application-execution/persistence/ApplicationMechanicEvaluator.cs),
[effect transaction](../../DantesRoleplay.DataAccess/Ecs/ApplicationEcsEffectApplier.cs),
[multi-step execution](../../src/system/interaction-orchestration/hosting/InteractionExecutionCoordinator.cs).

## 7. Capabilities, protocols, and authorization

- System capabilities, application mechanics, and application queries must be discoverable through
  transport-neutral descriptors carrying provenance, lifecycle, input/output schemas, roles,
  scope, authorization, operation mode, confirmation/idempotency requirements, and recovery actions.
- The current MCP surface groups operations under orient, query, and commit. Responses use a
  common success/error envelope with operation identity and structured next actions.
- HTTP and direct AI tools must invoke supported runtime services. AI tools expose the authorized
  fine-grained operations rather than passing the model the MCP multiplexers.
- The host must establish the principal and allowed scope. Caller-supplied prompts, role names,
  or capability claims must not confer authorization. Secret capabilities require host opt-in.
- Writes must preserve their owning preflight/confirmation and idempotency contract across
  transports. A model cannot supply its own trusted confirmation.
- Application resolution, state-space binding, proposal fingerprints, and relevant source
  revisions must be checked again where required at execution. Stale work must produce explicit
  recovery information instead of silently adopting changed definitions.

Read-only operations can still record audit evidence. Their classification means they do not
perform the requested application-state mutation, not that the database is never written.

Implementation anchors: [common descriptors](../../src/system/system-capabilities/domain/CapabilityContractContracts.cs),
[system dispatch](../../src/system/system-capabilities/persistence/SystemCapabilityCatalog.cs),
[MCP envelope](../../DantesRoleplay.MCPServer/Mcp/McpToolEnvelope.cs),
[direct AI tools](../../DantesRoleplay.MCPServer/Mcp/DirectCapabilityAiTools.cs).

## 8. Dynamic websites and reusable browser controls

- Applications must be able to publish website content through runtime operations. The current
  store retains immutable numbered HTML revisions, revision-bound assets, and a separately
  selected active revision. Draft appends and explicit activation use optimistic revision checks;
  direct bundle publication appends and activates a revision transactionally.
- Page identity and navigation must be separate from HTML bytes. A dedicated application
  publication state space carries page metadata and index-page designation; web-content tables
  retain content and assets.
- Application discovery must expose usable landing/secondary pages and readiness. Direct page
  and asset requests must recheck current publication state; knowing a URL cannot bypass disabled,
  hidden, missing-content, or ambiguous publication state.
- The host must provide reusable navigation, page hosting, loading/error/empty/data views, entity
  selection, mechanic actions/forms, system actions/forms/chat, AI workspace, and page administration.
  Controls discover current contracts and use supported prepare/review/execute/recovery operations.
- Browser input must be validated on the server regardless of form validation. Browser state may
  retain bounded recovery identifiers but cannot authorize an action or replace persistent state.
- The shared browser client must support same-origin requests, cancellation, bounded responses,
  structured errors, transient read retries, paging, and resolution fingerprints.
- Page bundles must obey the accepted ZIP layout and size/path restrictions. Current bundles have
  one root index.html and assets under assets/. Published pages are served as documents, not
  injected into the surrounding page-host element.

Automatic form generation supports a bounded profile. Application forms primarily render
primitive fields; system forms support bounded closed objects with primitive/enum fields and
JSON input for object/array values. This is not an arbitrary JSON Schema visual editor.

Implementation anchors: [page storage](../../DantesRoleplay.Web/Storage/WebPageStore.cs),
[publication discovery](../../DantesRoleplay.Web/Pages/WebPublicationDiscovery.cs),
[page administration](../../DantesRoleplay.Web/Http/WebInterfacePageAdministrationEndpoints.cs),
[bundle validation](../../DantesRoleplay.Web/Pages/WebPageBundleReader.cs),
[browser client](../../DantesRoleplay.Web/BrowserComponents/system-client.js),
[application controls](../../DantesRoleplay.Web/BrowserComponents/application-workspace.js).

## 9. Live updates and the web trust model

- Clients must receive recoverable notices that relevant content/state has changed and reread the
  authoritative data. The current transport is server-sent events at /api/changes, backed by
  SQLite polling, scoped cursors/replay, page-revision events, and keepalives.
- Change streams must authorize their application, state-space, and perspective scope. Durable
  object-change records must be staged with the owning write transaction.
- Registered component/relationship dependencies must identify affected objects and their
  dependents. Where dependency coverage or replay is incomplete, the system must conservatively
  invalidate the applicable scope.
- Current entity-existence and containment changes require broader fallback because registered
  objects do not yet declare those dependencies. Stream delivery is invalidation/recovery, not
  synchronous replication of all state into the browser.
- Ordinary web access uses the local operator model. Optional Tailscale access checks a configured
  hostname and allowlisted login. Browser mutations retain operator and same-origin checks.
- An explicit anonymous-public-access option grants network visitors website operator capabilities.
  It must not be represented as read-only anonymous access or a per-user multi-tenant permission model.

Authored pages run trusted same-origin website code. The current content-security policy allows
inline scripts/styles and restricts resource/network access; it is not isolation for hostile
third-party applications. SSE defaults are one-second database polling and 15-second keepalives.

Implementation anchors: [change feed](../../DantesRoleplay.Web/Live/WebChangeFeed.cs),
[scope authorization](../../DantesRoleplay.MCPServer/WebChangeScopeAuthorizer.cs),
[dependency tracking](../../src/system/projection-materialization/persistence/ApplicationObjectChangeTransactionParticipant.cs),
[remote access](../../DantesRoleplay.Web/Security/WebRemoteAccess.cs),
[web security](../../DantesRoleplay.Web/Security/WebInterfaceSecurity.cs).

## 10. AI providers and controlled tool use

- AI requests must use explicit provider/model capabilities and bounded request options. Provider
  and model discovery must allow clients to present supported controls and report unavailability.
- Ollama completion currently uses a loopback-only endpoint. Completion must enforce prompt/output
  and timeout limits and bounded concurrency/queueing; the structured-completion interface also
  restricts configured task classes. Unsupported tool, image, or reasoning requests fail explicitly.
- The host must supply registered agent identity and generate capability context from the exact
  authorized tools for the invocation. The browser cannot choose its own system prompt or grant
  additional tools/capabilities.
- Structured provider output and tool arguments must satisfy their declared schemas. Tool calls
  must pass through authorized in-process dispatch; generated text is a proposal or response, not
  evidence that a mutation occurred.
- Current generic provider integration includes Ollama and a Codex app-server bridge. Separate
  interaction-planning, outer-response/narration, and agenda integrations can use opt-in OpenAI
  Responses providers. The platform therefore supports both local and externally backed AI paths.
- The Codex bridge must validate its configured executable/version and working root. The generic
  provider route runs under its constrained app-server policy, declines external approvals, and
  exposes permitted host tools through the bridge. Its separate conversation route can retain
  interactive approval state; these routes must not be treated as identical approval behavior.
- Persisted observable tool activity, outcomes, and permitted reasoning summaries must remain
  distinct from raw hidden model reasoning and authoritative application state.

Provider configuration and availability are operational prerequisites. The implementation does
not imply that every model supports tools, structured output, or reasoning controls equally.

Implementation anchors: [AI contracts](../../DantesRoleplay.LocalAI/Contracts/AiContracts.cs),
[AI service](../../DantesRoleplay.LocalAI/Services/AiService.cs),
[host provider composition](../../DantesRoleplay.MCPServer/Program.cs),
[system agent composition](../../src/system/system-capabilities/hosting/SystemAiAgentService.cs),
[Codex bridge](../../src/system/codex-bridge).

## 11. Conversations, tasks, plans, and learned workflows

- Conversations must retain durable ordered messages and the authorized principal/application/
  state-space binding appropriate to the conversation kind. Bounded context windows and history
  paging must permit continuity without loading an entire transcript into each model request.
- Conversation kinds have separate contracts. The private system-read conversation has no tools,
  receives bounded authorized non-secret system evidence, and requires citations to supplied
  references. Broader web agent conversations can use authorized tools and application context.
- Play-facing continuity must retain exact visible replies, situations, and supported durable
  truth assertions with provenance. These records supplement context and do not substitute for
  verified component effects.
- System tasks must retain their steps, exact capability contracts, plan/proposal fingerprints,
  confirmation state, execution receipts, and terminal/partial outcomes. Supported workflows must
  allow preparation and inspection before authorized execution and support recovery. Execution
  records cancellation/interruption outcomes; there is no general durable task-cancellation command.
- Interaction planning must resolve current catalog capabilities and declared roles, verify the
  resulting plan, and retain the binding evidence used for execution. Query results may bind
  subsequent steps only through supported declared bindings.
- Verified recipes must allow reusable query/action graphs to run without a model call at each
  step. Parameter substitution and result bindings remain bounded and validated against current
  contracts. A model may help resolve missing inputs without receiving execution authority.
- Learning must preserve provenance and a governed verification/lifecycle boundary. A successful
  model suggestion must not automatically become a trusted reusable rule or workflow.
- Completely successful routes can produce recipe candidates, and eligible candidates can be
  automatically verified through the same governance owner. Repeated use may produce an inert
  mechanic opportunity with evidence and proposed contracts; it does not publish executable code.
- Scheduled AI work must use durable scheduling and audited runs. An unattended run cannot
  confirm its own writes; it can perform permitted reads and prepare work for later approval.

Current multi-step interaction graphs have a 16-step bound. System-task planning has a separate
profile of three rounds, twelve total steps, and eight writes. Plans can finish partially; their
receipts must preserve which actions succeeded before execution stopped.

Implementation anchors: [system conversations](../../src/system/system-conversations),
[play recording](../../src/system/play-recording),
[system task orchestration](../../src/system/system-task-orchestration),
[interaction execution](../../src/system/interaction-orchestration/hosting/InteractionExecutionCoordinator.cs),
[verified recipe resolution](../../src/system/interaction-orchestration/hosting/VerifiedInteractionRecipeResolver.cs).

## 12. Discovery, knowledge, and embeddings

- Exact identities and unambiguous aliases must retain precise resolution behavior. Ordinary
  discovery must use the current effective catalog and retain source identity/provenance.
- Feature retrieval must support deterministic lexical search and optional semantic assistance.
  The current hybrid path combines bounded lexical/vector rankings using reciprocal-rank fusion;
  returned candidates are reconstructed from active catalog records.
- Embedding generation is opt-in and currently uses a configured loopback Ollama endpoint with
  checked model identity, digest, and dimensions. Search must continue lexically when the provider
  or a valid vector generation is unavailable.
- Embedding requests must be batched and bounded, and returned vector counts, dimensions, and
  numeric values validated before accepting a generation.
- Derived vector generations must be scoped by application, trust lane, catalog/content
  fingerprint, format, provider/model identity, and dimensions. A stale or mismatched generation
  must not silently supply authoritative candidates.
- Verified-recipe matching is a second embedding consumer. It uses a separate trusted-recipe lane
  and revalidates recipe contracts; ambiguous matching must not silently choose an executable route.
- Knowledge retrieval must filter authorization and scope before ranking or returning content.
  The current knowledge path is deterministic lexical retrieval, separate from feature/recipe
  embeddings. This implementation does not provide general semantic search over every document,
  conversation, or world-state value.
- Optional knowledge-answer generation must use only authorized candidates, cite supplied
  identities, preserve the declared presentation categories, and recheck candidate/authorization
  revisions before returning an answer. Missing completion capability must remain explicit.
- Generic information records have their own scoped, source-filtered lexical search and
  schema-bound answer path. They are not automatically included in the embedding index.

The derived index stores vector bytes in a separate SQLite database. Current similarity search
loads the selected generation's vectors and computes cosine distance in C#; there is no approximate
nearest-neighbor index or external vector database. Feature-index startup warmup builds configured
applications; a later activation can require an explicit rebuild or restart before semantic search
uses a matching generation. Recipe matching can build its generation on demand.

Implementation anchors: [feature retrieval](../../src/system/interaction-orchestration/persistence/InteractionFeatureRetriever.cs),
[derived vector index](../../src/system/interaction-orchestration/persistence/InteractionDerivedVectorIndex.cs),
[startup warmup](../../DantesRoleplay.MCPServer/InteractionRetrievalWarmup.cs),
[recipe matching](../../src/system/interaction-orchestration/hosting/VerifiedInteractionRecipeResolver.cs),
[authorized knowledge answers](../../src/system/knowledge/persistence/AuthorizedKnowledgeCoordinator.cs),
[information search](../../src/system/information/persistence/InformationStore.cs),
[knowledge lexical retrieval](../../src/system/knowledge/persistence/DeterministicKnowledgeLexicalRetriever.cs).

## 13. Events, notifications, observations, and scheduling

- Events must retain type/version, payload, time, operation/correlation, causation, ordering,
  depth, and applicable source context. Structural component events may retain actual before/after
  values and revisions from applied effects.
- Subscription reactions must use declared event matching and role bindings, permitted mechanic
  versions, and bounded execution. Current chain limits include depth 8, 100 events, and 100
  guard/reaction executions, plus per-subscription limits.
- Notifications must persist their content and entity links and support their declared read/archive
  lifecycle. Notification delivery and acknowledgement must be distinguishable from application
  state. Notification archival is currently one-way.
- The scheduler must support one-time, recurring, observation-matching, and conditional triggers
  with durable definitions, fire receipts, worker leases, retries, and terminal states.
- Recurrence must carry interval, timezone/local-time rules, applicable date bounds, and daylight
  saving gap/overlap policy. Misfires must follow declared behavior.
- External observations must pass principal/source/schema validation, timestamp and replay bounds,
  rate limits, and bounded JSON admission. Conditional triggers must use exact component contracts
  and registered adapter versions, with supported edge/level activation and rearming behavior.

Current generic trigger fire targets are notifications. Scheduled AI work consumes durable
scheduling through a separate work layer. Conditional/observation adapter implementations are
reviewed host code selected by registered data, not arbitrary uploaded adapter programs.

Implementation anchors: [event ledger](../../src/system/events-and-notifications/persistence/EventLedger.cs),
[chain budgets](../../src/system/events-and-notifications/domain/ChainBudget.cs),
[notifications](../../src/system/events-and-notifications/persistence/NotificationStore.cs),
[trigger contracts](../../src/system/trigger-scheduling/domain/TriggerSchedulingContracts.cs),
[recurrence](../../src/system/trigger-scheduling/domain/RecurringTriggerContracts.cs),
[observation admission](../../src/system/trigger-scheduling/persistence/SqliteObservationIngestionService.cs).

## 14. Blobs, snapshots, synchronization, and backup

- Blob uploads must use issued upload identities/tokens, declared length/hash/media type, bounded
  streaming, and byte-signature verification. Finalized bytes must use content-addressed paths;
  SQL metadata must identify the corresponding immutable content.
- The current blob profile accepts PNG, JPEG, and WebP images up to 10 MiB, with 15-minute upload
  sessions. It is not a general arbitrary-file storage API. Website bundle assets use the separate
  web-content store described above.
- Missing blob metadata/bytes must be reported. Finalization moves verified files and then saves
  metadata; filesystem changes and SQL changes are not one atomic transaction. Derived media URL
  tickets are bounded, expiring references rather than another durable media store.
- Snapshot packages must retain immutable opaque bytes, scope/producer identity and version,
  encoding, boundary fingerprint, digest, size, capture time, and root operation. Staging requires
  an existing owning transaction. Current packages declare dantes-canonical-json-v1 encoding and
  are bounded to 1 MiB; the generic store verifies metadata/digests and treats the bytes as opaque.
  There is no generic public snapshot-restore API.
- Catalog import must compare file, database, and last-agreed manifest fingerprints and handle
  conflicts explicitly. Its SQL mutation is transactional; filesystem manifest changes occur after
  commit. Live edits must be exported/reviewed before editing corresponding authored files.
- Catalog export must preserve its declared scope. It currently exports latest supported authored
  records and a manifest, with optional legacy world records and operation JSONL. It is not an
  export of every platform table, every historical version, or blob bytes.
- Database backup must use SQLite backup behavior and integrity verification. The current backup
  helper covers the database, not the filesystem blob directory. Complete preservation therefore
  also depends on retaining authoritative blob bytes and required authored source files.

Implementation anchors: [blob contracts](../../src/system/blob-storage/domain/BlobStorageContracts.cs),
[blob transfer](../../src/system/blob-storage/persistence/FileBlobTransferService.cs),
[snapshot packages](../../src/system/snapshots/persistence/SnapshotPackageStore.cs),
[catalog import](../../src/system/catalog/persistence/CatalogImporter.cs),
[catalog export](../../src/system/catalog/persistence/CatalogExporter.cs),
[database backup](../../src/system/catalog-tools/tooling/DatabaseBackup.cs).

## 15. Operations, resource bounds, and implementation constraints

- The current host is an ASP.NET application on .NET 10, composing MCP, HTTP/web, background work,
  the generic kernel, and configured AI providers. Runtime persistence uses EF Core and SQLite.
- Database, blob, derived-data, and allowed source-root locations must be explicit and resolvable.
  Live data and local configuration must stay outside published application content.
- Supported operator settings must be discoverable, validated, and revisioned with audit evidence.
  Current persisted setting overrides distinguish staged and applied versions and take effect at
  restart; restoring a setting creates an explicit later revision.
- Operations must retain observable execution evidence and stable errors/recovery actions.
  System feedback must support durable reports, triage, and reversible archival/retention controls
  independently of application state.
- Untrusted or authored workloads must be bounded: schema size/depth, JSON values, projections,
  traversal, paging, sandbox execution, event chains, uploads, requests, and streams. Cancellation
  and unavailable dependencies must produce controlled outcomes.
- Caches must have explicit identities, invalidation or lifetime rules, and resource limits.
  Existing structural-plan caches retain declarations rather than materialized world values.
- The baseline is a local/private operator-oriented host. Some services explicitly coordinate
  within one host process. This document makes no claim of distributed transactions, horizontal
  multi-host coordination, hostile application isolation, real-time simulation throughput, or
  quantified latency/capacity guarantees.

When evaluating an alternative library, preserve the authority, versioning, authorization,
transaction, and recovery boundaries above. Query speed or entity iteration throughput alone does
not replace the dynamic authoring, web publication, AI orchestration, and durable-storage contracts.

Implementation anchors: [host project](../../DantesRoleplay.MCPServer/DantesRoleplay.MCPServer.csproj),
[shared runtime target](../../Directory.Build.props),
[setting overrides](../../src/system/host-settings/persistence/HostSettingOverrideStore.cs),
[audit model](../../src/system/operations-and-audit/domain/Operation.cs),
[feedback model](../../src/system/feedback/domain/SystemFeedback.cs),
[schema bounds](../../src/system/schema-validation/domain/SchemaValidationContracts.cs).

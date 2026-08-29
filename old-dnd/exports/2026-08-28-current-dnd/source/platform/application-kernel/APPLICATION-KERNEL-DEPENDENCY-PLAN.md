# Generic application kernel dependency tree — application-scoped ECS, sources, and derived projections

Status: **Slices 0–12H and the legacy ownership ratification are accepted; initial kernel upgrade complete**
Ruleset alignment: **ruleset-neutral**  
Source: **not applicable**  
Owner: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Builds on: [System modularization dependency plan](../modularization/SYSTEM-MODULARIZATION-DEPENDENCY-PLAN.md)  
Downstream consumer: [Interaction orchestration dependency plan](../interaction-orchestration/INTERACTION-ORCHESTRATION-DEPENDENCY-PLAN.md)  
Implementation guide: [Application-kernel agent guide](APPLICATION-KERNEL-AGENT-GUIDE.md)
Completion evidence: [Application-kernel completion receipt](receipts/APPLICATION-KERNEL-COMPLETION-RECEIPT.md)
Accepted semantic root: [Slice 0 semantic contract ratification](APPLICATION-KERNEL-SLICE-0-IMPLEMENTATION.md)
([receipt](receipts/APPLICATION-KERNEL-SLICE-0-RECEIPT.md)); no active implementation slice
Accepted inventory: [Slice 1 read-only legacy inventory](APPLICATION-KERNEL-SLICE-1-IMPLEMENTATION.md)
([receipt](receipts/APPLICATION-KERNEL-SLICE-1-RECEIPT.md)); no active implementation slice
Accepted legacy ownership decision: [all current unresolved gameplay records target `dnd2024`](LEGACY-OWNERSHIP-RATIFICATION.md)
Accepted pure contracts: [Slice 2](APPLICATION-KERNEL-SLICE-2-IMPLEMENTATION.md)
([receipt](receipts/APPLICATION-KERNEL-SLICE-2-RECEIPT.md)); no active implementation slice
Accepted application/source persistence: [Slice 3](APPLICATION-KERNEL-SLICE-3-IMPLEMENTATION.md)
([receipt](receipts/APPLICATION-KERNEL-SLICE-3-RECEIPT.md)); no active implementation slice
Accepted source overlays and generic candidate manifests: [Slice 4](APPLICATION-KERNEL-SLICE-4-IMPLEMENTATION.md)
([receipt](receipts/APPLICATION-KERNEL-SLICE-4-RECEIPT.md)); no active implementation slice
Accepted component type/schema security: [Slice 5](APPLICATION-KERNEL-SLICE-5-IMPLEMENTATION.md)
([receipt](receipts/APPLICATION-KERNEL-SLICE-5-RECEIPT.md)); no active implementation slice
Accepted application-scoped ECS state: [Slice 6](APPLICATION-KERNEL-SLICE-6-IMPLEMENTATION.md)
([receipt](receipts/APPLICATION-KERNEL-SLICE-6-RECEIPT.md)); no active implementation slice
Accepted versioned structural projections: [Slice 7](APPLICATION-KERNEL-SLICE-7-IMPLEMENTATION.md)
([remediation](APPLICATION-KERNEL-SLICE-7-REMEDIATION.md),
[receipt](receipts/APPLICATION-KERNEL-SLICE-7-RECEIPT.md)); no active implementation slice.
Accepted atomic ECS effects and audit: [Slice 8A](APPLICATION-KERNEL-SLICE-8-IMPLEMENTATION.md)
([remediation](APPLICATION-KERNEL-SLICE-8A-REMEDIATION.md),
[receipt](receipts/APPLICATION-KERNEL-SLICE-8A-RECEIPT.md)); no active implementation slice.
Accepted deterministic catalog navigation: [Slice 9](APPLICATION-KERNEL-SLICE-9-IMPLEMENTATION.md)
([receipt](receipts/APPLICATION-KERNEL-SLICE-9-RECEIPT.md)); no active implementation slice.
Accepted public read-only catalog protocol: [Slice 10A](APPLICATION-KERNEL-SLICE-10A-IMPLEMENTATION.md)
([receipt](receipts/APPLICATION-KERNEL-SLICE-10A-RECEIPT.md)); no active implementation slice.
Accepted authenticated application/source discovery: [Slice 10B](APPLICATION-KERNEL-SLICE-10B-IMPLEMENTATION.md)
([receipt](receipts/APPLICATION-KERNEL-SLICE-10B-RECEIPT.md)); no active implementation slice.

## Outcome and non-goals

Redesign the generic system kernel so that:

1. `system.*` owns ruleset-neutral administration, application/source registration, ECS contracts,
   validation, persistence, transactions, audit, retrieval, and protocol transport;
2. every non-system capability belongs to a registered application, initially `dnd2024`;
3. applications contribute component types, schemas, procedures, mechanics, events, fixtures, and
   search documents through registered file/directory/glob sources;
4. ordered application sources produce one deterministic effective definition for each logical ID;
5. the ECS stores arbitrary application-defined JSON values without adding C# types or database
   columns for new concepts;
6. every component write is validated against the exact active application schema version;
7. applications can declare versioned derived projections whose exact output fields are assembled
   from exact component fields and other derived projections through one bounded acyclic dependency
   graph, without teaching the mapper how data is retrieved;
8. the effective application manifest exposes the forward and reverse dependency graph so a source
   schema, field, mapping, or projection change identifies every potentially affected consumer;
9. application activation and state-space upgrades are explicit, versioned, auditable, and atomic;
10. the generic kernel builds and runs without `dnd2024` or another application installed;
11. local and remote AI discover the same effective application contracts without either model
   becoming registration, validation, or execution authority; and
12. every effective application catalog is traversable and deterministically searchable through
   described, cursor-paginated logical directories without vector search or a local model.

This plan does **not**:

- make arbitrary CLR objects, executable code, SQL, shell commands, or polymorphic type loading an
  ECS datatype;
- place D&D component interfaces, records, formulas, or IDs in the system kernel;
- make `system` an application or a reusable game-mechanics base;
- let a remote endpoint create arbitrary host filesystem directories;
- let directory precedence bypass application namespace, trust, schema, or authorization checks;
- silently upgrade existing state when an application/component schema changes;
- persist a derived projection as a second source of truth or let a stale cache authorize a write;
- give a projection mapper unrestricted database, filesystem, network, dynamic-code, or arbitrary
  graph-query access;
- fabricate catalog-directory descriptions, return unbounded catalog dumps, or require semantic
  embeddings for basic discovery;
- treat reverse dependency discovery as proof that a schema or semantic change is compatible;
- replace typed effects, sandboxed mechanics, one-root transaction ownership, or operation audit;
- require one database per application in the first delivery; or
- authorize a big-bang migration from this dependency plan.

## Does ECS fit this requirement?

Yes, with one qualification: ECS handles arbitrary **application-defined component types** without
requiring kernel schema changes. It does not mean the kernel should deserialize arbitrary runtime
classes.

The safe portable datatype boundary is any valid JSON value:

```text
object | array | string | number | boolean | null
```

Application schemas represent semantic datatypes such as dates, identifiers, enums, tuples,
vectors, maps, and tagged unions using JSON Schema constraints/formats without teaching the kernel
what those values mean.

Each application owns a JSON Schema describing the allowed value for one component type. The
system owns generic schema compilation/evaluation, size/depth limits, persistence, revisions, and
transactions; it never interprets field names or game meaning. `component.add` and `component.set`
may accept any schema-valid JSON value. `component.merge` remains object-only because merge has no
unambiguous meaning for arrays, scalars, or null. Null is a stored value when a schema permits it;
removing a component remains a separate operation.

Large/binary data is stored outside ECS through a future content-addressed resource owner and
referenced by a small schema-valid component. Raw files and arbitrary CLR serialization do not
belong in component rows.

## Existing owners and evidence

| Concern | Current owner | State | Evidence/constraint |
| --- | --- | --- | --- |
| Generic ECS state | `src/system/state` / `IWorldStore` | verified foundation, over-broad | Entities, components, containment, and relationships are generic, but definition administration and runtime state share one world-named port. |
| Arbitrary component shape | `Component.Data` | partial | Stored as JSON text, so new concepts need no columns, but `WorldStore.ParseObject` rejects arrays, scalars, and null. |
| Component schemas | catalog component definition plus `.schema.json` | conflicting | Applications already author schemas, but `ComponentDefinition` says the kernel does not enforce them and current state writes only check for a JSON object. |
| Component definition history | `WorldStore.DefineComponentAsync` | conflicting | Existing IDs update name/description/schema in place, causing already-stored data to inherit new meaning without a version or migration boundary. |
| Structural writes | effects/transactions | verified foundation | Nine generic effects, dry-run validation, atomic application, guards/events/audit, and no game-specific write verbs already exist. |
| Declared component projections | `src/system/mechanics` / `IProjectionResolver` | verified foundation | A mechanic declares role components and references; the resolver materializes only that bounded state before sandbox execution and already batches the declared component set. |
| Dependent projection composition | E6 / `MechanicComposer` | verified foundation | Closed child-data dependencies are statically checked, cycle-rejected, executed in topological order, frozen, audited, and included in the root transaction. The current form accepts one complete sibling object per dependent input and does not yet define reusable multi-source projection records. |
| Uncommitted virtual state | [E7](../e7/E7-DEPENDENCY-PLAN.md) | planned separate owner | E7 owns projections over preceding root-local validated effects. Ordinary derived projections in this plan read committed canonical components; they depend on E7 only when a staged root must expose uncommitted virtual state. |
| Derived-projection registry and impact graph | `src/system/projection-materialization` | accepted | Versioned definitions, persistence, bounded structural materialization, exact dependency edges, cycle detection, and stable reverse impact are accepted. Outputs are recomputed from canonical state; a disposable cache remains an optional optimization. |
| Application registry | `src/system/application-registry` | accepted | Opaque non-system IDs, immutable registration identity, authoritative revisions, deterministic fingerprints, exact source-overlay activation, state-space creation, and binding history are accepted. Incompatible non-empty transformation remains an explicit later migration boundary. |
| Source registration | `src/system/source-registry` plus local-AI scanner | accepted | Registered allowed roots/path specifications, scan evidence, trust/precedence, deterministic winners, and path redaction are database-owned and accepted. |
| Catalog import | `src/system/catalog` | accepted | File parsing, hashes, validation/import/export, application/source provenance, and effective-winner handoff are accepted. |
| Runtime state authority | shared SQLite database through application-scoped ECS ports | accepted | State-space isolation, exact component contracts, generic JSON values, effects, audit, and legacy adoption are accepted. |
| Public protocol | `orient`, `query`, `commit` and `GenericVerbSurface` | accepted | Exactly three verbs expose qualified `system.*` administration and application-scoped discovery/execution with compatibility guards. |
| Catalog hierarchy | `src/system/catalog-navigation` | accepted | Authored descriptions, effective application trees, breadcrumbs/counts, exact inspection, and snapshot-bound cursor pages are accepted. |
| Deterministic catalog search | catalog navigation plus interaction retrieval | accepted | Exact and lexical search are application-scoped, manifest-bound, deterministic, and complete without vectors or local AI. |
| System modularization | modularization receipts through Slice 24 plus Slice 12H independence evidence | accepted prerequisite | Generic components and standalone local AI are physically separated; generic projects do not compile application/game-adapter trees. Retained legacy files remain uncompiled by user decision. |
| AI orchestration | interaction orchestration Slices 12B–12H | accepted downstream | Local and remote planners consume bounded effective kernel contracts through the common verifier without becoming registration, validation, or execution authority. |

## Target ownership and dependency direction

```text
src/system/
  application-registry/       application identities, revisions, state-space bindings
  source-registry/            allowed roots, path/glob sources, precedence, trust, scans
  ecs/                        generic entities, type definitions, values, queries
  schema-validation/          bounded JSON Schema evaluation and schema fingerprints
  projection-materialization/ versioned derived views, dependency DAG, impact analysis, batched reads
  catalog-navigation/         described logical trees, lexical search, cursors, exact inspection
  effects-and-transactions/   generic atomic state mutations
  catalog/                    generic readers/materialization/import/export
  deterministic-retrieval/    effective-document lookup/search
  local-ai/                   opaque documents, embeddings, completion only
  mcp-protocol/               three verbs and closed system/application dispatch

applications/
  dnd2024/                    application manifest and application-owned catalog sources
    application.json
    catalog-nodes/            logical directory titles/descriptions (proposed representation)
    components/
    projections/
    procedures/
    mechanics/
    event-types/
    subscriptions/
    fixtures/

src/applications/
  dantes-roleplay-host/       selects system components and installed applications
```

The physical labels are proposals. The dependency rule is authoritative for the plan:

```text
host -> system hosting contracts + selected application adapters
application files -> declared generic catalog formats
application adapter -> public system contracts only
system -> no application assembly, ID, vocabulary, fixture, or default
local-ai -> opaque generic document/scope/source contracts only
```

The current central `catalog/` remains the authored source until a separately confirmed move. The
first migration may register parts of that tree as compatibility sources instead of moving files.

## Proposed system interfaces

Exact names are conceptual until Slice 0 confirmation.

### `IApplicationRegistry`

Owns immutable application identity and revisions:

- register an application ID other than reserved `system`;
- append metadata/dependency/source-order revisions;
- retrieve one exact revision or the active revision;
- reject unknown/cyclic dependencies and namespace collisions;
- compute a canonical application revision fingerprint; and
- create/bind state spaces to an exact active application revision.

An application registration contains at least: ID, display name, description, status, declared
format version, optional ordered base applications, and administrative provenance. It does not
contain runtime game state or executable C# types.

### `ISourceRegistry`

Owns existing filesystem source relationships:

- source ID, application ID, logical name, canonical allowed root, path/glob specification;
- reader profile, trust class, enabled state, explicit precedence, and registration revision;
- scan generation/status, observed files, hashes, errors, and effective/shadowed winners; and
- preview, rescan, reorder, disable, and unregister operations with audit evidence.

The interface registers an existing path specification. It does not create arbitrary directories.
A local administrative CLI may initialize a directory under an explicitly allowed workspace, but
that filesystem convenience is not an MCP/model capability and is not required by the registry.

### `IComponentTypeRegistry`

Owns immutable application component contracts:

- application ID plus qualified component type ID;
- version, name, description, JSON Schema, content hash, status, and source provenance;
- effective winner and shadow evidence from the source registry; and
- exact-version and active-version lookup.

Changing a schema appends a type version. It never mutates the meaning of the previous version in
place. A definition must begin with its owning application namespace, such as
`dnd2024.abilities`. System state contracts, if any, use separately governed `system.*` records and
cannot be overridden by applications.

### `IEntityComponentStore`

Owns generic runtime state only:

- create/read/query/soft-delete an entity inside one state space;
- add/set/remove one component value by qualified type ID;
- object-only shallow merge as an explicit separate operation;
- bounded projections by entity IDs and component type IDs; and
- revision/concurrency evidence for entities and component instances.

The interface accepts a generic JSON value plus an expected type version/hash. It resolves and
validates through `IComponentTypeRegistry`; callers cannot supply validation truth. It contains no
method named for a game concept.

Containment and relationship graphs may remain generic ECS-adjacent system capabilities, but they
should be separate ports or explicit generic component types rather than reasons for one
`IWorldStore` interface to own all state and all metadata.

### Derived projection registry and materializer

Conceptual interfaces `IProjectionDefinitionRegistry` and `IProjectionMaterializer` own reusable,
application-scoped read models without creating another state authority:

- one immutable projection definition version declares its application-qualified identity, output
  JSON Schema/version/hash, source roles, exact component type versions and JSON Pointer field
  paths, optional projection dependencies, bounds, mapper provenance, and status;
- dependencies name exact projection versions from the same effective application graph or an
  explicitly declared base application; implicit cross-application reads are rejected;
- application preview validates source schemas/paths, output schemas, mapper declarations,
  dependency availability, namespace/trust rules, and the complete acyclic graph before activation;
- materialization computes the transitive source closure, deduplicates entity/component reads,
  fetches them in one bounded batch where the store supports it, executes dependencies in stable
  topological order, validates every intermediate output, and freezes the result;
- a consumer declares the exact projection it requires and receives the resulting JSON value. It
  knows the input contract, not the database, table, component location, or retrieval procedure;
- structural field selection/renaming may use a closed declarative mapping interpreted generically.
  Application-specific calculations, eligibility, defaults, or rule meaning remain sandboxed
  application JavaScript and may only consume the declared source projection;
- a donor compatibility view is an ordinary application-owned derived projection. Prefer
  operation-specific views such as attack, spell-cast, or action-resource state over assembling an
  entire campaign when the operation needs only a bounded subset; and
- derived results are ephemeral. An optional materialized cache is disposable and keyed by the
  effective-manifest fingerprint, projection version/hash, state-space ID, source entity/component
  instance revisions, and authorization scope. A cache miss or invalidation recomputes from
  canonical state; cached data never becomes write or authorization authority.

This extends rather than replaces the existing `IProjectionResolver` and E6 composition. E6
continues to own closed dependent execution. E7 is consulted only for a separately declared
root-local overlay source; the ordinary materializer cannot observe uncommitted state.

### `IApplicationActivator`

Coordinates a two-phase application revision:

1. scan registered sources without changing the active revision;
2. parse declarations and resolve directory overlays;
3. validate namespaces, schemas, references, dependency order, trust, and collision rules;
4. materialize a complete immutable candidate manifest and fingerprint;
5. report all failures with no active/runtime-state change; and
6. explicitly activate the exact candidate fingerprint in one transaction.

Activation changes available contracts, not existing application state. A state-space upgrade is a
separate explicit operation with compatibility/migration evidence.

### `ICatalogNavigator`

Owns the mandatory non-vector discovery path over one effective application manifest:

- list the catalog collections installed for one application revision;
- browse one logical directory node with its title, authored description, breadcrumbs,
  direct/subtree counts by record kind, and paginated direct children/records;
- search qualified IDs, names, authored descriptions, aliases/match phrases, and category paths
  with deterministic lexical ranking and optional collection/branch/kind/status filters;
- inspect one exact current or historical record version with source/hash/provenance; and
- return stable opaque cursors bound to the application/effective-manifest fingerprint,
  collection, branch, filters, sort order, page size, and last stable key.

Directory descriptions are application-authored catalog-node metadata, not generated by a model or
inferred from a folder name. Directory overlay resolution applies to catalog nodes like other
authored records. During legacy migration, a derived category without metadata is exposed with
`descriptionStatus: "missing"` instead of fabricated prose; activation policy may require authored
descriptions for newly published nodes.

Cursor pagination uses bounded `pageSize` plus `nextCursor`, never mutable offsets or an unbounded
dump. Children sort by logical path and records by `(record kind, qualified ID)`. A cursor whose
manifest or filter fingerprint no longer matches returns typed `CURSOR_STALE` with a root restart
call instead of silently continuing against different content.

This interface is always available when the deterministic-retrieval component is installed. Exact
browse/search/inspect works with local AI, embeddings, and vector extensions disabled.

## Application, source, and state model

```text
Application
  └─ ApplicationRevision (immutable)
       ├─ ordered base application revisions
       ├─ ordered SourceRegistration revisions
       └─ EffectiveApplicationManifest
            ├─ ComponentTypeVersion
            ├─ ProjectionDefinitionVersion
            │    ├─ exact component-field dependencies
            │    ├─ exact projection dependencies
            │    └─ output schema + mapper fingerprint
            ├─ CatalogNodeVersion (title, description, logical path)
            ├─ Procedure/Mechanic/Event/Subscription versions
            └─ feature/search documents

StateSpace
  ├─ bound ApplicationRevision + effective-manifest fingerprint
  └─ Entity
       └─ ComponentInstance
            ├─ qualified type ID
            ├─ exact type version/hash
            ├─ any valid schema-approved JSON value
            └─ instance revision
```

A state space is the isolation boundary for one running instance, not the application definition.
For example, several campaigns may use the same `dnd2024` application revision without sharing
entities. Proposed initial legacy binding is one state space for the current database; its ID and
application assignment require migration confirmation.

### Dependency graph and change impact

The effective manifest materializes one deterministic dependency graph whose nodes include
component type/schema versions, projection definition versions, procedures, mechanics, events,
subscriptions, and other declared consumers. Edges distinguish at least:

- reads component type/version and exact JSON Pointer path;
- depends on derived projection version;
- produces projection output schema/version;
- mechanic/procedure consumes projection or component state; and
- effect/event may write or invalidate a component type.

Application preview rejects missing dependencies, ambiguous effective versions, cycles, paths that
cannot exist under the declared source schema, unauthorized cross-application edges, and configured
node/depth/fan-out/output limits. It also produces a reverse-impact report. Replacing a component
schema, field meaning, mapper, or output schema therefore identifies every transitively affected
projection and consumer before activation or state-space upgrade.

The impact report is evidence for required review and tests, not an automatic migration decision.
Removing or changing a field used by an active projection blocks compatibility until the affected
definition is versioned, migrated, disabled, or explicitly proven compatible. Unknown additional
component fields do not invalidate a projection that never declared them. Existing consumers that
declare only a component ID are conservatively indexed as reading the whole component until they
migrate to an exact projection; the kernel never infers dependencies by parsing JavaScript source.

### Directory overlay rules

- Every source belongs to one application revision and has a unique explicit precedence within it.
- Trusted authored declarations use logical identity
  `(application ID, record kind, declared local/qualified ID)`.
- Generic files without a declared ID use
  `(application ID, normalized relative path)` and cannot become executable contracts merely by
  winning an overlay.
- The highest-precedence eligible source wins before catalog import, lexical search, or vector
  indexing.
- Equal-precedence competitors for one identity reject the candidate application revision.
- Shadowed declarations remain diagnostic evidence and never enter normal search or execution.
- Removing/disabling an override reveals the next eligible declaration only in a newly previewed
  application revision; active state is not silently changed.
- A source cannot override a higher trust class. No application source can override `system.*`.
- An application may use a low-precedence `core` directory and higher `dnd2024`/extension
  directories. This provides fallback without treating core game mechanics as system behavior.

## Proposed public system surface

Keep exactly `orient`, `query`, and `commit`. The following `kind` values are proposals requiring
public-surface confirmation:

| Operation | Purpose | State effect |
| --- | --- | --- |
| `query(kind: "system.applications")` | Inspect registered applications/revisions and active bindings. | Read/audit only. |
| `query(kind: "system.sources")` | Inspect registered source stacks, scan status, winners, and redacted conflicts. | Read/audit only. |
| `query(kind: "system.application-preview")` | Preview/validate one source-stack revision and return its fingerprint. | No active application/state change; optional disposable scan cache only. |
| `query(kind: "system.dependencies")` | Traverse bounded direct/transitive component-field, projection, mechanic, and catalog dependents for impact review. | Read/audit only. |
| `query(kind: "system.catalogs")` | List authorized effective catalog collections with root descriptions and counts. | Read/audit only. |
| `query(kind: "system.catalog.browse")` | Browse one described logical directory with cursor-paginated direct children and records. | Read/audit only. |
| `query(kind: "system.catalog.search")` | Deterministically search one application/collection/branch without vectors. | Read/audit only. |
| `query(kind: "system.catalog.record")` | Inspect one exact qualified record/version with current source provenance. | Read/audit only. |
| `commit(kind: "system.application.register")` | Register or append application metadata. | Registry transaction only. |
| `commit(kind: "system.source.register")` | Register an existing allowed path/glob and precedence. | Registry transaction only; no directory creation. |
| `commit(kind: "system.application.activate")` | Activate the exact successful preview fingerprint. | Atomic application-manifest switch only. |
| `commit(kind: "system.state-space.create")` | Create an isolated runtime state space bound to an exact app revision. | State-space transaction. |
| `commit(kind: "system.state-space.upgrade")` | Upgrade one empty state space after exact compatibility validation. | State-space transaction; non-empty state returns `MIGRATION_REQUIRED`. |

Application operations are then qualified under the selected application, for example discovery of
`dnd2024.*` feature keys. Generic ECS mutation may remain a system transport capability only if its
payload requires a state space and application-qualified component type; an application cannot add
a new kernel verb for each component.

All administrative commits require dry-run/preview support, trusted administrative authorization,
idempotency keys, canonical fingerprints, path redaction, and append-only operation evidence.

## Schema and datatype contract

Initial delivery supports JSON Schema 2020-12 with a documented safe subset/limits. The generic
validator must:

- compile and fingerprint the exact effective schema version;
- validate `object`, `array`, `string`, `number`, `integer`, `boolean`, `null`, and declared unions;
- reject invalid schemas at application preview, before activation;
- reject instance values that fail the exact active/pinned schema before any state mutation;
- preserve numeric text/fidelity and never coerce strings, booleans, nulls, or floating values;
- bound schema bytes, reference depth, instance bytes/depth, regex work, errors, and evaluation time;
- prohibit uncontrolled remote schema resolution/network access;
- return typed path-specific failures without leaking hidden values; and
- cache compiled schemas only by trusted content hash, with deterministic invalidation.

The initial policy should require self-contained schemas. Cross-file `$ref` support is deferred
unless Slice 0 closes canonical URI, overlay, cycle, and offline resolution semantics.

## Compatibility and migration strategy

Do not rewrite current IDs or tables in one migration.

1. Produce a read-only inventory assigning every existing component/procedure/mechanic/event/source
   to `system`, `dnd2024`, another confirmed application, or `unresolved`.
2. Confirm the fate of current `game.core.*` records. They are not system records merely because
   they are shared game concepts.
3. Add application/source/revision tables and nullable compatibility scope columns first.
4. Register the current catalog paths as explicit legacy sources and build an effective manifest
   without changing runtime behavior.
5. Backfill one legacy state space and exact component-definition hashes/versions after the mapping
   is reviewed. Unresolved records block enforcement rather than receiving a guessed owner.
6. Introduce dual-read compatibility adapters and compare old/new projections byte-for-byte.
7. Inventory current role/component/child dependencies, register equivalent projection definitions
   where reuse or donor compatibility needs them, and compare materialized results byte-for-byte
   with existing committed-state projections before changing consumers.
8. Materialize current procedure/mechanic categories as compatibility catalog nodes, preserve their
   exact branch membership/counts, mark missing descriptions honestly, and add reviewed authored
   descriptions before requiring them for published `dnd2024` directories.
9. Turn on namespace/schema/state-space enforcement for one test application, then `dnd2024`.
10. Migrate public kinds through advertised aliases and deprecation evidence; never break clients
   before the compatibility gate.
11. Remove in-place definition mutation and legacy unscoped writes only after all current callers and
   catalog imports use exact application/type revisions.
12. Delete compatibility columns/shims only at a separate destructive acceptance gate.

Existing component values that do not satisfy their newly assigned schema are migration findings,
not values the kernel may coerce or silently discard.

## Dependency tree

```text
Generic application kernel with application-owned component types       [accepted; Slices 0–12H]
├─ A. Semantic and ownership ratification                               [accepted; Slice 0]
│  ├─ JSON-value datatype boundary and object-only merge                 [accepted]
│  ├─ Application vs state-space identity                                [accepted]
│  ├─ Reserved `system` and initial `dnd2024` namespace                  [accepted]
│  ├─ Versioned component type/schema meaning                            [accepted]
│  ├─ Source registration vs physical directory creation                [accepted]
│  └─ Public/admin authorization and redaction semantics                 [accepted; implementation depends on E9]
├─ B. Legacy inventory and compatibility map                            [accepted first-delivery; aliases retained]
│  ├─ Current ID/application classification                              [accepted; Slice 1]
│  ├─ `game.core.*` ownership decision                                   [accepted; `dnd2024` migration target]
│  ├─ Current component-value/schema compatibility report                [accepted; Slice 1]
│  └─ Public kind/alias migration inventory                              [accepted compatibility; removal deferred]
├─ C. Generic registries and immutable contracts                         [accepted; depends on A/B]
│  ├─ Application/revision registry                                      [accepted; Slice 3]
│  ├─ Source/scan/precedence registry                                    [accepted; Slice 3]
│  ├─ Versioned component type registry                                  [accepted; Slice 5]
│  └─ State-space/application binding                                    [accepted; Slice 6]
├─ D. Effective application materialization                              [accepted; depends on C]
│  ├─ File/directory/glob scan adapter                                   [verified reusable seam]
│  ├─ Trust-aware deterministic overlay resolver                         [accepted; Slice 4]
│  ├─ Closed schema/reference validation                                 [accepted; Slice 5]
│  ├─ Candidate manifest/fingerprint                                     [accepted; Slice 4]
│  └─ Atomic activation/replay                                           [accepted; Slice 10F source-overlay evidence]
├─ E. Application-scoped ECS                                             [accepted; depends on C/D]
│  ├─ Split metadata registry from runtime state port                     [accepted; Slice 6]
│  ├─ Any-JSON component add/set/read                                    [accepted; Slice 6]
│  ├─ Object-only merge and explicit remove                              [accepted; Slice 6]
│  ├─ Exact schema/version/hash validation                               [accepted; Slice 6]
│  ├─ State-space isolation/concurrency                                  [accepted; Slice 6]
│  └─ Effects/events/audit transaction parity                            [accepted; Slice 11J]
├─ F. Dependency-aware derived projections                               [accepted first-delivery; depends on C-E]
│  ├─ Versioned output/source/field/dependency declarations              [accepted; Slice 7]
│  ├─ Closed multi-source acyclic DAG over committed canonical state     [accepted; Slice 7]
│  ├─ Transitive closure, deduplicated batched read, and frozen outputs   [accepted; Slice 7]
│  ├─ Reverse schema/mapping/consumer impact index and preview report     [accepted; Slice 10E]
│  ├─ Revision-keyed disposable cache and deterministic invalidation      [optional optimization; recomputation is authoritative]
│  └─ E7 adapter only for separately declared root-local virtual sources [separate E7 feature]
├─ G. Deterministic catalog navigation                                   [accepted; Slices 9/10A/12A]
│  ├─ Immutable described catalog-node metadata                           [accepted; Slice 9]
│  ├─ Effective application/collection/path tree over a supplied manifest [accepted; Slice 9]
│  ├─ Snapshot-bound cursor pagination                                   [accepted; Slice 9]
│  ├─ Exact inspection and deterministic lexical/branch search           [accepted; Slice 9]
│  └─ Dependency/impact citations and activated-manifest retention       [accepted; Slices 10E/10F]
├─ H. System-qualified administrative/discovery protocol                  [accepted first-delivery; Slices 10A–10H]
│  ├─ Application/source queries                                         [accepted; Slice 10B]
│  ├─ Public catalog list/browse/search/inspect queries                    [accepted; Slice 10A]
│  ├─ Dependency impact queries                                           [accepted; Slice 10E declared component-field/projection coverage]
│  ├─ Application/source registration commits                            [accepted; Slice 10C]
│  ├─ Application preview                                                [accepted; Slice 10D]
│  ├─ Application activation                                             [accepted; Slice 10F exact-preview activation]
│  ├─ State-space create commit                                          [accepted; Slice 10G exact-activation binding]
│  ├─ State-space upgrade commit                                         [accepted; Slice 10H empty-state compatibility/history]
│  └─ Legacy aliases and capability discovery                            [accepted compatibility; destructive removal deferred]
├─ I. `dnd2024` application adoption                                     [accepted first-delivery; depends on B-H]
│  ├─ Register current catalog sources                                   [accepted proof; Slice 11A exact fresh-host registration/activation]
│  ├─ Move/declare application-owned component contracts                 [all 33 legacy component contracts accepted; Slice 11F]
│  ├─ Register required native/donor compatibility projections            [accepted zero-adoption classification; Slice 11I]
│  ├─ Author/migrate described catalog directory nodes                    [accepted kernel handoff; Slice 11H]
│  ├─ Backfill one legacy state-space binding                             [accepted; Slice 11J; normal live database untouched]
│  ├─ Remove game IDs/defaults from system                               [accepted compile boundary; modularization Slice 24/12H guard]
│  └─ Full catalog/projection/sandbox/replay parity                       [accepted; Slice 11J]
└─ J. AI and host consumption                                            [accepted; Slices 12A–12H]
   ├─ Effective application documents feed deterministic retrieval       [accepted]
   ├─ Remote/local planners can browse and search catalogs without vectors [accepted]
   ├─ Local AI receives opaque app/source/logical metadata               [accepted]
   ├─ System-only host runs with zero applications                       [accepted]
   └─ Multi-application isolation and final acceptance                   [accepted; Slice 12H]
```

## Ordered implementation slices and model routing

Each slice requires one active implementation document and stops at its own receipt.

| Order | Slice | Default model | Review/switch gate | Exit gate |
| ---: | --- | --- | --- | --- |
| 0 | [Ratify datatype, application/state-space, schema-version, derived-projection, described-catalog/cursor, directory, trust, and endpoint semantics](APPLICATION-KERNEL-SLICE-0-IMPLEMENTATION.md) | **Sol High** recommended | **Accepted 2026-08-23**; [receipt](receipts/APPLICATION-KERNEL-SLICE-0-RECEIPT.md). | Decisions recorded; no runtime changes. |
| 1 | [Read-only legacy namespace/schema/value/public-surface inventory](APPLICATION-KERNEL-SLICE-1-IMPLEMENTATION.md) | **Terra High** | **Accepted 2026-08-23**; [receipt](receipts/APPLICATION-KERNEL-SLICE-1-RECEIPT.md). | Machine-readable report; zero DB/catalog mutation. |
| 2 | [Pure application/source/type/state-space/projection/catalog contracts and validators with in-memory fakes](APPLICATION-KERNEL-SLICE-2-IMPLEMENTATION.md) | **Terra High** | **Accepted 2026-08-23**; [receipt](receipts/APPLICATION-KERNEL-SLICE-2-RECEIPT.md). | Identifier, cycles, precedence, schema bounds, fingerprints, cursor binding, and forbidden-field tests pass. |
| 3 | [Application/source/scan registry persistence](APPLICATION-KERNEL-SLICE-3-IMPLEMENTATION.md) | **Terra High** | **Accepted 2026-08-23**; [receipt](receipts/APPLICATION-KERNEL-SLICE-3-RECEIPT.md). | Reviewed forward migration, rollback/no-change tests, idempotent registration, path redaction. |
| 4 | [Effective overlay materializer and candidate application manifest](APPLICATION-KERNEL-SLICE-4-IMPLEMENTATION.md) | **Terra High** | **Accepted 2026-08-24**; [receipt](receipts/APPLICATION-KERNEL-SLICE-4-RECEIPT.md). | Stable winner/conflict/removal/trust/generation/fingerprint tests pass. |
| 5 | [Versioned component type registry and bounded JSON Schema evaluator](APPLICATION-KERNEL-SLICE-5-IMPLEMENTATION.md) | **Terra High** | **Accepted 2026-08-24 after Sol schema-security review**; [receipt](receipts/APPLICATION-KERNEL-SLICE-5-RECEIPT.md). | Every JSON kind, invalid schema/value, no-network `$ref`, and resource-bound tests pass. |
| 6 | [Application-scoped ECS ports, any-JSON add/set, object merge, and state-space isolation](APPLICATION-KERNEL-SLICE-6-IMPLEMENTATION.md) | **Terra High** | **Accepted 2026-08-24**; [receipt](receipts/APPLICATION-KERNEL-SLICE-6-RECEIPT.md). | Generic fixtures pass; schema failures and cross-space access produce no mutation. |
| 7 | [Versioned dependency-aware projection registry and committed-state materializer](APPLICATION-KERNEL-SLICE-7-IMPLEMENTATION.md) | **Terra High** | **Accepted 2026-08-24**; [receipt](receipts/APPLICATION-KERNEL-SLICE-7-RECEIPT.md). | Immutable structural DAG, bounded batch reads, frozen schema-valid output, and reverse-impact evidence pass; cache and legacy parity remain excluded. |
| 8 | [Atomic ECS effects and audit](APPLICATION-KERNEL-SLICE-8-IMPLEMENTATION.md) | **Sol High** | **8A accepted 2026-08-24**; [receipt](receipts/APPLICATION-KERNEL-SLICE-8A-RECEIPT.md). Application execution and read-only `dnd2024` parity remain separate later leaves. | Atomic mutation/audit and rollback evidence pass without legacy or public integration. |
| 9 | [Described effective catalog tree, exact inspection, deterministic lexical search, and snapshot cursor pagination](APPLICATION-KERNEL-SLICE-9-IMPLEMENTATION.md) | **Terra High** | **Accepted 2026-08-24**; [receipt](receipts/APPLICATION-KERNEL-SLICE-9-RECEIPT.md). Authorization, activated-manifest retention, and dependency-impact citations remain later owners. | Non-game fixture browses every collection/page without vectors; descriptions/counts/order/cursors are deterministic; stale cursor and redaction tests pass. |
| 10 | `system.*` application/source/dependency/catalog/preview/activate/state-space protocol | **Sol High** recommended | **Slices 10A–10H accepted 2026-08-24**: [public catalog receipt](receipts/APPLICATION-KERNEL-SLICE-10A-RECEIPT.md), [authenticated reads receipt](receipts/APPLICATION-KERNEL-SLICE-10B-RECEIPT.md), [registration receipt](receipts/APPLICATION-KERNEL-SLICE-10C-RECEIPT.md), [preview receipt](receipts/APPLICATION-KERNEL-SLICE-10D-RECEIPT.md), [dependency-impact receipt](receipts/APPLICATION-KERNEL-SLICE-10E-RECEIPT.md), [activation receipt](receipts/APPLICATION-KERNEL-SLICE-10F-RECEIPT.md), [state-space creation receipt](receipts/APPLICATION-KERNEL-SLICE-10G-RECEIPT.md), and [empty-state upgrade receipt](receipts/APPLICATION-KERNEL-SLICE-10H-RECEIPT.md). Non-empty migration and cross-consumer impact indexing remain explicitly incomplete. | Exactly three verbs; capability catalog/dispatch/examples/protocol walk agree; unauthorized calls fail. |
| 11 | Register and activate `dnd2024`, classify/migrate current component definitions, projections, described catalog nodes, and legacy state space | **Terra High** | **Accepted 2026-08-24 through 11A–11J**: [final state-adoption and execution-parity receipt](receipts/APPLICATION-KERNEL-SLICE-11J-RECEIPT.md) links the prerequisite receipts and proves explicit complete state adoption, generic edges/effects, replay, rollback, migration, authenticated protocol, application-ECS projection parity, and deterministic sandbox invocation across all 14 ratified mechanics without touching the normal live database. | Catalog validates; full app suite/action/projection/navigation parity; no D&D literals in generic components. |
| 12 | Feed effective application view and deterministic catalog navigation to AI orchestration; prove zero-app/multi-app hosts | **Mixed; see 12A–12H routing** | **All eight subslices accepted.** [Slice 12H receipt](../interaction-orchestration/receipts/INTERACTION-ORCHESTRATION-SLICE-12H-RECEIPT.md). | With vectors/local AI disabled, remote traversal still finds and inspects capabilities; system-only, `dnd2024`, and two non-game fixture applications pass isolation and full acceptance. |

Terra can implement all bounded slices after Slice 0 decisions are confirmed and each slice has a
closed test contract. Sol is recommended at Slice 0 and the named migration/schema/public/final
review gates; no model may invent missing application ownership or migration policy.

For the user-facing count, Slices **1–12** are twelve entries; the table has thirteen because it
also includes Slice 0. Terra High is the default for Slices 1–7, 9, and 11, with a Sol High
schema-security review at Slice 5. Sol High is recommended for Slices 8 and 10. Slice 12 has eight
subslices; all of 12A–12H are accepted under the linked interaction-orchestration owner.
Terra High owns bounded retrieval and receipt mechanics; Sol High owns threat-model, planner
isolation, public execution, and learning-policy gates; Sol xhigh owns final acceptance.

Slice 10 is divided into eight transaction- and authority-coherent parts: 10A public catalog
navigation, 10B authenticated registry reads, 10C application/source registration, 10D application
preview, 10E dependency-impact inspection, 10F exact-preview activation, 10G state-space creation,
and 10H state-space upgrade/compatibility. All eight parts are accepted; Slice 10 is complete.

## Acceptance matrix

| Class | Required evidence |
| --- | --- |
| Datatypes | Object, array, string, integer, non-integer number, boolean, and null values round-trip exactly when allowed by schema. |
| Schema | Invalid schemas block activation; invalid values block writes; schema/hash/version changes cannot silently reinterpret state. |
| ECS | A non-game fixture adds a new component type using only application files and registry calls—no C# or DB schema change. |
| Projection declaration | A non-game application defines a reusable output from exact fields across multiple components and another projection; missing paths, unknown versions, ambiguous sources, cycles, excess depth/fan-out, and undeclared reads fail before activation. |
| Projection materialization | The host computes one stable topological plan, deduplicates/fetches the transitive component set in a bounded batch, validates/freezes every intermediate result, and gives the consumer no database/query capability. |
| Change impact | Replacing a source schema/path, mapper, or output schema produces a deterministic transitive reverse-impact report and blocks incompatible activation without changing active state. |
| Projection cache | Equal source revisions and manifest/projection hashes may reuse byte-identical derived output; any source, scope, schema, mapping, or application revision change invalidates it, and cache loss only causes recomputation. |
| Catalog browse | Every authorized collection is traversable root-to-leaf with authored descriptions, breadcrumbs, direct/subtree counts, stable child/record ordering, bounded pages, and exact record inspection. |
| Catalog search | Exact/lexical branch search returns stable cited results with vector/local AI disabled; unrelated applications, shadowed records, and unauthorized content never appear. |
| Cursor | Same manifest/filter/cursor produces the same next page without gaps/duplicates; changed fingerprint returns `CURSOR_STALE`, not a mixed-revision page. |
| Namespace | `system.*` cannot be registered/overridden as an application; application records require their own namespace. |
| Isolation | State spaces and unrelated applications cannot read/write each other's entities, types, receipts, or search results. |
| Overlay | Higher eligible source wins before import/search; ties conflict; disabling it reveals the base only after explicit activation. |
| Trust | Untrusted/generic files cannot become executable definitions or override trusted/system contracts. |
| Negative/no-change | Failed preview, activation, schema validation, authorization, concurrency, or migration leaves active manifests and state unchanged. |
| Replay | Repeated registration/activation/write idempotency keys return prior evidence and do not duplicate revisions or effects. |
| Transactions | Component effects, guards, events, notifications, and audit retain one-root atomicity and deterministic replay. |
| Compatibility | Existing catalog and clients continue through confirmed aliases during migration; dual reads agree before legacy removal. |
| AI boundary | Local AI has no application assembly/reference or game vocabulary; it receives only bounded opaque source documents. |
| Host independence | A zero-application system host builds/runs, and adding a fixture application requires no system source change. |
| Repository | Focused tests, solution build/full suite, fresh catalog validation, protocol walk when applicable, and `git diff --check` pass together. |

## Confirmation gates

Slice 0 accepted the semantic meaning in gates 1–7 and 9–11. Later slices still require
confirmation for their exact serialized schemas, numeric bounds, tables/migrations, authorization
implementation, aliases, and activation/migration evidence. Before activating the relevant slice,
confirm:

1. ECS component values support every JSON value, while `component.merge` remains object-only and
   large/binary data uses references rather than raw ECS payloads.
2. Component schemas become system-enforced, immutable versioned contracts rather than mutable
   documentation.
3. The application/state-space split and the legacy database's initial state-space binding.
4. `system` remains reserved; `dnd2024` is the initial application; and every `game.core.*` record
   receives an explicit non-system owner or migration outcome.
5. Source registration records existing allowed paths/globs but does not remotely create arbitrary
   filesystem directories.
6. Directory precedence, logical identity, trust, tie conflicts, preview/activation, and path
   redaction semantics.
7. JSON Schema dialect/subset, bounds, self-contained `$ref` policy, version/hash behavior, and
   handling of currently invalid legacy values.
8. Application/source/type/state-space tables, migration/backfill, retention, and rollback plan.
9. Derived-projection identity/versioning, exact field-path syntax, multi-source dependency shape,
   mapper boundary, graph bounds, reverse-impact policy, cache policy, and the rule that canonical
   components remain the sole state authority. The user has confirmed the overall dependency-aware
   automapping direction; exact permanent/public/schema forms still require Slice 0 confirmation.
10. Catalog-node identity/metadata format, required-description policy, included collections,
    deterministic lexical ranking, page-size bounds, cursor encoding/expiry/stale behavior, and
    authorized redaction. The user has confirmed the overall described/paginated, no-vector
    discovery direction; exact permanent/public/schema forms still require Slice 0 confirmation.
11. Administrative authorization/idempotency and the exact proposed `system.*` public kinds while
    retaining three verbs.
12. Compatibility alias duration and the gate for removing unscoped `world`, `component`, and
    related legacy kinds.
13. `dnd2024` activation and state/projection/catalog-navigation migration after full parity evidence.
14. Final acceptance only after zero-application, multi-application, and vector/local-AI-disabled
    catalog-discovery proof.

## Implementation-document lifecycle

- This is the master dependency plan for the application kernel; do not duplicate it in the AI or
  modularization plans.
- The modularization plan remains authoritative for physical moves/game-code eviction. This plan
  owns new application/ECS/source semantics and migrations.
- Create one active slice document only after its dependencies and named confirmation gates close.
- Each slice names exact files/tables/public kinds, migration forward/back behavior, compatibility
  adapter, focused tests, full acceptance commands, and a stop point.
- Complete kernel Slices 0–9 before implementing AI orchestration that depends on application
  search/overlay authority. Protocol Slice 10 may be coordinated with orchestration public kinds but
  must retain one owner and one compatibility map.
- Replace completed prose with receipt links; do not implement the next slice in the same document.

## Planning receipt

- Runtime artifacts created: none.
- Permanent IDs, schemas, migrations, database rows, public kinds, and application registrations
  created: none.
- Existing foundations reused: modular system components, generic ECS rows, typed effects and root
  transactions, catalog readers/hashes, local-AI glob scanning, SQLite, and three MCP verbs.
- New proposed owners: application registry, source registry, versioned component type registry,
  state-space binding, application activator, bounded schema validation, and an application-scoped
  derived-projection registry/materializer with reverse dependency evidence, plus described
  deterministic catalog navigation and cursor pagination.
- Recommended implementation: Terra High per confirmed bounded slice; Sol at semantic, migration,
  schema-security, public-surface, and final acceptance gates.
- Deliberate stop: planning documents and owner links only.

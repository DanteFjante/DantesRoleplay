# Generic application kernel dependency tree — application-scoped ECS and source registration

Status: **planning only; data, migration, and public-surface semantics awaiting confirmation**  
Ruleset alignment: **ruleset-neutral**  
Source: **not applicable**  
Owner: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Builds on: [System modularization dependency plan](../modularization/SYSTEM-MODULARIZATION-DEPENDENCY-PLAN.md)  
Downstream consumer: [Interaction orchestration dependency plan](../interaction-orchestration/INTERACTION-ORCHESTRATION-DEPENDENCY-PLAN.md)  
Implementation guide: [Application-kernel agent guide](APPLICATION-KERNEL-AGENT-GUIDE.md)

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
7. application activation and state-space upgrades are explicit, versioned, auditable, and atomic;
8. the generic kernel builds and runs without `dnd2024` or another application installed; and
9. local and remote AI discover the same effective application contracts without either model
   becoming registration, validation, or execution authority.

This plan does **not**:

- make arbitrary CLR objects, executable code, SQL, shell commands, or polymorphic type loading an
  ECS datatype;
- place D&D component interfaces, records, formulas, or IDs in the system kernel;
- make `system` an application or a reusable game-mechanics base;
- let a remote endpoint create arbitrary host filesystem directories;
- let directory precedence bypass application namespace, trust, schema, or authorization checks;
- silently upgrade existing state when an application/component schema changes;
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
| Application registry | none | missing | There is no authoritative application identity, revision, dependency/source stack, activation, or state-space binding. |
| Source registration | local-AI scanner plus catalog reader | partial | Generic file/directory/glob scanning exists, but registered roots, precedence, trust, scan generations, and effective winners are not database-owned. |
| Catalog import | `src/system/catalog` | verified single-root foundation | File parsing, hashes, manifest, validation, import/export exist for one authored root, without application/source/overlay provenance. |
| Runtime state authority | shared SQLite database | verified, unscoped | State is authoritative but entities/component definitions are not partitioned by application instance/state space. |
| Public protocol | `orient`, `query`, `commit` and `VerbSurface` | verified, conflicting content | Three-verb transport exists, but kinds are unqualified and include system and game behavior in one flat static surface. |
| System modularization | modularization receipts through Slice 23 | verified prerequisite | Generic components and standalone local AI are physically separated; compiled game eviction and final host independence remain. |
| AI orchestration | interaction orchestration plan | planned downstream | It already requires application scopes and directory overlays; it must consume this kernel rather than invent parallel registries. |

## Target ownership and dependency direction

```text
src/system/
  application-registry/       application identities, revisions, state-space bindings
  source-registry/            allowed roots, path/glob sources, precedence, trust, scans
  ecs/                        generic entities, type definitions, values, queries
  schema-validation/          bounded JSON Schema evaluation and schema fingerprints
  effects-and-transactions/   generic atomic state mutations
  catalog/                    generic readers/materialization/import/export
  deterministic-retrieval/    effective-document lookup/search
  local-ai/                   opaque documents, embeddings, completion only
  mcp-protocol/               three verbs and closed system/application dispatch

applications/
  dnd2024/                    application manifest and application-owned catalog sources
    application.json
    components/
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

## Application, source, and state model

```text
Application
  └─ ApplicationRevision (immutable)
       ├─ ordered base application revisions
       ├─ ordered SourceRegistration revisions
       └─ EffectiveApplicationManifest
            ├─ ComponentTypeVersion
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
| `commit(kind: "system.application.register")` | Register or append application metadata. | Registry transaction only. |
| `commit(kind: "system.source.register")` | Register an existing allowed path/glob and precedence. | Registry transaction only; no directory creation. |
| `commit(kind: "system.application.activate")` | Activate the exact successful preview fingerprint. | Atomic application-manifest switch only. |
| `commit(kind: "system.state-space.create")` | Create an isolated runtime state space bound to an exact app revision. | State-space transaction. |
| `commit(kind: "system.state-space.upgrade")` | Upgrade one state space after compatibility/migration validation. | Explicit migration transaction; later slice. |

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
7. Turn on namespace/schema/state-space enforcement for one test application, then `dnd2024`.
8. Migrate public kinds through advertised aliases and deprecation evidence; never break clients
   before the compatibility gate.
9. Remove in-place definition mutation and legacy unscoped writes only after all current callers and
   catalog imports use exact application/type revisions.
10. Delete compatibility columns/shims only at a separate destructive acceptance gate.

Existing component values that do not satisfy their newly assigned schema are migration findings,
not values the kernel may coerce or silently discard.

## Dependency tree

```text
Generic application kernel with application-owned component types       [planned]
├─ A. Semantic and ownership ratification                               [awaiting confirmation]
│  ├─ JSON-value datatype boundary and object-only merge                 [proposed]
│  ├─ Application vs state-space identity                                [proposed]
│  ├─ Reserved `system` and initial `dnd2024` namespace                  [proposed]
│  ├─ Versioned component type/schema meaning                            [proposed]
│  ├─ Source registration vs physical directory creation                [proposed]
│  └─ Public/admin authorization and redaction                           [depends on E9]
├─ B. Legacy inventory and compatibility map                            [planned; depends on A]
│  ├─ Current ID/application classification                              [ready after A]
│  ├─ `game.core.*` ownership decision                                   [confirmation gate]
│  ├─ Current component-value/schema compatibility report                [planned]
│  └─ Public kind/alias migration map                                    [planned]
├─ C. Generic registries and immutable contracts                         [planned; depends on A/B]
│  ├─ Application/revision registry                                      [migration gate]
│  ├─ Source/scan/precedence registry                                    [migration gate]
│  ├─ Versioned component type registry                                  [migration gate]
│  └─ State-space/application binding                                    [migration gate]
├─ D. Effective application materialization                              [planned; depends on C]
│  ├─ File/directory/glob scan adapter                                   [verified reusable seam]
│  ├─ Trust-aware deterministic overlay resolver                         [missing]
│  ├─ Closed schema/reference validation                                 [missing]
│  ├─ Candidate manifest/fingerprint                                     [missing]
│  └─ Atomic activation/replay                                           [missing]
├─ E. Application-scoped ECS                                             [planned; depends on C/D]
│  ├─ Split metadata registry from runtime state port                     [planned]
│  ├─ Any-JSON component add/set/read                                    [missing]
│  ├─ Object-only merge and explicit remove                              [existing seam; needs generalization]
│  ├─ Exact schema/version/hash validation                               [missing]
│  ├─ State-space isolation/concurrency                                  [missing]
│  └─ Effects/events/audit transaction parity                            [verified seam; migration required]
├─ F. System-qualified administrative protocol                           [planned; depends on C/D]
│  ├─ Application/source queries                                         [public gate]
│  ├─ Register/preview/activate commits                                  [public gate]
│  ├─ State-space create/upgrade commits                                 [public gate]
│  └─ Legacy aliases and capability discovery                            [compatibility gate]
├─ G. `dnd2024` application adoption                                     [planned; depends on B-F]
│  ├─ Register current catalog sources                                   [planned]
│  ├─ Move/declare application-owned component contracts                 [planned]
│  ├─ Backfill one legacy state-space binding                             [migration gate]
│  ├─ Remove game IDs/defaults from system                               [depends on modularization Leaf E]
│  └─ Full catalog/action/replay parity                                  [pending]
└─ H. AI and host consumption                                            [planned; depends on D-G]
   ├─ Effective application documents feed deterministic retrieval       [interaction prerequisite]
   ├─ Local AI receives opaque app/source/logical metadata               [planned]
   ├─ System-only host runs with zero applications                       [final independence gate]
   └─ Multi-application isolation and final acceptance                   [pending]
```

## Ordered implementation slices and model routing

Each slice requires one active implementation document and stops at its own receipt.

| Order | Slice | Default model | Review/switch gate | Exit gate |
| ---: | --- | --- | --- | --- |
| 0 | Ratify datatype, application/state-space, schema-version, directory, trust, and endpoint semantics | **Sol High** recommended | User confirmation required; this is the cross-owner semantic root. | Decisions recorded; no runtime changes. |
| 1 | Read-only legacy namespace/schema/value/public-surface inventory | **Terra High** | Switch to Sol only for genuinely ambiguous ownership, especially `game.core.*`. | Machine-readable report; zero DB/catalog mutation. |
| 2 | Pure application/source/type/state-space contracts and validators with in-memory fakes | **Terra High** | Sol review if contracts imply game meaning or duplicate an owner. | Identifier, cycles, precedence, schema bounds, fingerprints, and forbidden-field tests pass. |
| 3 | Application/source/scan registry persistence | **Terra High** | **Sol High review before migration confirmation.** | Reviewed forward migration, rollback/no-change tests, idempotent registration, path redaction. |
| 4 | Effective overlay materializer and candidate application manifest | **Terra High** | Sol review if two sources can both be authoritative. | Stable winner/conflict/removal/trust/generation/fingerprint tests pass. |
| 5 | Versioned component type registry and bounded JSON Schema evaluator | **Terra High** | **Sol High review for schema-version and resource-exhaustion security.** | Every JSON kind, invalid schema/value, no-network `$ref`, bounds, cache invalidation tests pass. |
| 6 | Application-scoped ECS ports, any-JSON add/set, object merge, and state-space isolation | **Terra High** | Switch to Sol if transaction or migration ownership is ambiguous. | Generic fixtures pass; schema failures and cross-space access produce no mutation. |
| 7 | Effects/events/audit integration and legacy dual-read parity | **Terra High** | **Sol High review before enabling writes against migrated state.** | Existing transactional/replay behavior is byte-stable; old/new projections agree. |
| 8 | `system.*` application/source/preview/activate protocol | **Sol High** recommended | User confirmation required for public kinds and administrative authorization. | Exactly three verbs; capability catalog/dispatch/examples/protocol walk agree; unauthorized calls fail. |
| 9 | Register and activate `dnd2024`, classify/migrate current component definitions and legacy state space | **Terra High** | Sol review on unresolved ownership, incompatible state, or game/system leakage. | Catalog validates; full app suite/action parity; no D&D literals in generic components. |
| 10 | Feed effective application view to retrieval/AI orchestration and prove zero-app/multi-app hosts | **Sol Extra High** recommended for acceptance | Terra fixes mechanical findings; Sol performs final architecture/security judgment. | System-only, `dnd2024`, and two non-game fixture applications pass isolation and full acceptance. |

Terra can implement all bounded slices after Slice 0 decisions are confirmed and each slice has a
closed test contract. Sol is recommended at Slice 0 and the named migration/schema/public/final
review gates; no model may invent missing application ownership or migration policy.

## Acceptance matrix

| Class | Required evidence |
| --- | --- |
| Datatypes | Object, array, string, integer, non-integer number, boolean, and null values round-trip exactly when allowed by schema. |
| Schema | Invalid schemas block activation; invalid values block writes; schema/hash/version changes cannot silently reinterpret state. |
| ECS | A non-game fixture adds a new component type using only application files and registry calls—no C# or DB schema change. |
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

Before activating the relevant slice, confirm:

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
9. Administrative authorization/idempotency and the exact proposed `system.*` public kinds while
   retaining three verbs.
10. Compatibility alias duration and the gate for removing unscoped `world`, `component`, and
    related legacy kinds.
11. `dnd2024` activation and state migration after catalog/full-suite parity evidence.
12. Final acceptance only after zero-application and multi-application independence proof.

## Implementation-document lifecycle

- This is the master dependency plan for the application kernel; do not duplicate it in the AI or
  modularization plans.
- The modularization plan remains authoritative for physical moves/game-code eviction. This plan
  owns new application/ECS/source semantics and migrations.
- Create one active slice document only after its dependencies and named confirmation gates close.
- Each slice names exact files/tables/public kinds, migration forward/back behavior, compatibility
  adapter, focused tests, full acceptance commands, and a stop point.
- Complete kernel Slices 0–7 before implementing AI orchestration that depends on application
  search/overlay authority. Protocol Slice 8 may be coordinated with orchestration public kinds but
  must retain one owner and one compatibility map.
- Replace completed prose with receipt links; do not implement the next slice in the same document.

## Planning receipt

- Runtime artifacts created: none.
- Permanent IDs, schemas, migrations, database rows, public kinds, and application registrations
  created: none.
- Existing foundations reused: modular system components, generic ECS rows, typed effects and root
  transactions, catalog readers/hashes, local-AI glob scanning, SQLite, and three MCP verbs.
- New proposed owners: application registry, source registry, versioned component type registry,
  state-space binding, application activator, and bounded schema validation.
- Recommended implementation: Terra High per confirmed bounded slice; Sol at semantic, migration,
  schema-security, public-surface, and final acceptance gates.
- Deliberate stop: planning documents and owner links only.

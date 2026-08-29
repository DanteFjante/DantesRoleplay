# Application kernel Slice 0 implementation — semantic contract ratification

Status: **accepted**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Generic application kernel dependency plan](APPLICATION-KERNEL-DEPENDENCY-PLAN.md), leaf A  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Ratify the semantic boundaries that all later application-kernel slices must implement.  
Exclusions: Runtime code, schemas, permanent database IDs, migrations, catalog moves, protocol
registration, application registration, and game-rule work.  
Allowed files/areas: This document and status/link-only edits in the owning application-kernel plan
and platform roadmap.  
Stop point: Record the user's decision on the package below, write the Slice 0 receipt, mark leaf A
accepted, and stop before Slice 1 inventory work.

## Confirmed decisions

The user has already confirmed these product directions:

- generic platform commands live under `system.*`;
- non-system capabilities belong to a registered application, initially `dnd2024`;
- an application may register directory/path/glob sources and use higher-precedence sources to
  override lower-precedence definitions;
- ECS state must accept application-defined datatypes rather than game-specific C# component
  classes;
- reusable automapped/derived components may depend on exact fields from other components and
  derived components, and the system must expose their dependency impact;
- catalog directories need authored descriptions, bounded pagination, traversal, lexical search,
  and exact inspection without requiring vectors or a local model; and
- local AI remains application-agnostic and is a downstream consumer, not kernel authority.

The user accepted the exact package in the next section on 2026-08-23. It is the semantic
constraint for later slices; it does not itself register a runtime or public contract.

## Accepted decision package

The decisions are one coherent package. A later requested semantic change reopens only the affected
item and its listed dependents.

### S0.1 — Namespace and application identity

- `system` is a reserved platform namespace and cannot be registered as an application.
- An application ID is one lowercase ASCII segment matching `[a-z][a-z0-9-]{0,62}`.
- An application-owned qualified ID is `<application-id>.<local-id>`, where each local segment
  matches `[a-z][a-z0-9-]{0,62}`. Matching is ordinal and case-sensitive after validation; aliases
  are separate records and never alternate authorities.
- `dnd2024` is the first application ID, but generic system and local-AI code may not branch on or
  reference that literal.
- Every executable, state-bearing, or searchable non-system record has exactly one application
  owner. Existing `game.core.*` records remain explicitly unresolved until Slice 1 inventories them;
  they are not silently classified as system records.

**Consequence:** this closes identifier parsing without deciding the migration outcome of any
existing legacy record.

### S0.2 — Application revision, activation, and state space

- An application is an identity and history of immutable revisions. A revision names exact source
  revisions, dependency revisions, contracts, and one effective-manifest fingerprint.
- Preview constructs a candidate revision without changing active contracts or runtime state.
  Activation atomically selects the exact previewed fingerprint as the application's active
  revision.
- A state space is an isolated runtime instance, such as a campaign. It is bound to one exact
  application revision and effective-manifest fingerprint.
- Activating a newer application revision does not upgrade an existing state space. State-space
  upgrade is a separate, explicit, auditable transaction with compatibility or migration evidence.
- Multiple state spaces may use the same application revision without sharing entities. An entity,
  component instance, operation, or receipt belongs to exactly one state space.
- The current database's legacy state-space ID and application binding are migration decisions for
  Slices 1 and 3; Slice 0 does not invent them.

**Authority:** authored files own declaration bytes; SQLite owns registrations, selected revisions,
state-space bindings, runtime state, operations, and audit. Effective manifests are immutable
derived snapshots. Search indexes, vectors, projection caches, and scan caches are disposable.

### S0.3 — ECS datatype and mutation meaning

- A component value is any bounded valid JSON value: object, array, string, number, boolean, or
  null. The kernel does not load arbitrary CLR types or add per-application database columns.
- The generic JSON representation must preserve JSON kind and numeric fidelity. Hashing may use a
  documented canonical encoding, but canonicalization must not coerce the stored value.
- `component.add` and `component.set` accept any value permitted by the exact component schema.
  `component.merge` is a shallow object-only operation and rejects non-object existing or supplied
  values. Null is a stored value when allowed; removal is a distinct operation.
- Large or binary content is represented by a small schema-valid reference to a future resource
  owner, never by arbitrary serialization in a component row.
- Size, nesting, schema-work, and error-count limits are mandatory host policy advertised through
  capability discovery. Slice 2 may choose conservative numeric defaults and hard ceilings without
  changing these semantics; exceeding any limit is a typed no-change failure.

### S0.4 — Component type and JSON Schema meaning

- Each component instance records a qualified type ID, immutable type version, schema content hash,
  JSON value, and instance revision.
- Changing a component schema appends a new type version. Existing values retain their old type
  version and never silently inherit new meaning.
- The kernel validates a write against the state space's exact effective component contract before
  mutation. Callers and models cannot assert that validation already succeeded.
- The initial dialect is JSON Schema 2020-12 with a versioned, documented safe keyword profile.
  Unsupported keywords reject application preview instead of being ignored.
- Initial references are self-contained and offline: only same-document fragment references are
  accepted. Network, filesystem, and uncontrolled external `$ref` resolution are prohibited.
- Format handling, supported keywords, and resource ceilings are part of the validator profile and
  application-manifest fingerprint. Changing that profile creates a new candidate revision.
- Invalid legacy values are inventory findings. They are preserved and block enforcement or upgrade
  until an explicit migration is confirmed; they are never coerced or discarded.

### S0.5 — Dependency-aware derived projections

- “Automapped component” is represented as a **derived projection**, not as a second canonical
  component. Canonical component instances remain the only state authority.
- A projection definition is application-owned, immutable, versioned, qualified, and included in
  the effective manifest. It declares an exact output schema/version/hash.
- A definition declares named source roles. A materialization request binds every role to an exact
  entity ID in the same state space; a role may explicitly mean “the subject entity.” The mapper
  receives no entity search, relationship traversal, store, filesystem, network, or code-execution
  capability.
- Each source dependency names an exact qualified component type version and an RFC 6901 JSON
  Pointer. The empty pointer selects the whole value. A projection dependency names an exact
  projection version and output pointer. Legacy whole-component reads remain conservatively
  indexed as whole-value dependencies.
- The generic mapper may only select, copy, rename, and structurally compose declared JSON values.
  It may construct object/array shape but may not calculate, coerce, aggregate, default, branch, or
  interpret application meaning. Those behaviors remain sandboxed application JavaScript mechanics
  consuming a declared projection.
- Missing required source roles, components, or paths fail materialization. Optionality and omission
  must be explicit in the definition; an optional missing value is omitted, never replaced by an
  inferred/default value.
- Application preview validates exact source paths, unique output targets, output schema, explicit
  base-application edges, graph size/depth/fan-out limits, and the complete acyclic dependency graph.
- Materialization uses a stable topological order, deduplicates canonical reads, batches them where
  supported, validates each intermediate result, and freezes outputs before use.
- The effective manifest stores forward and reverse dependency edges. A changed schema, source path,
  mapping, output schema, mechanic declaration, or possible component write yields a deterministic
  transitive impact report. The report requires review; it is not proof of compatibility or an
  automatic migration decision.
- Results are virtual by default. An optional cache is disposable and keyed by manifest,
  projection, state-space, role/entity, source-instance revisions, and authorization scope. Cache
  loss only recomputes; cached data cannot authorize or source a write.
- Cross-state-space edges and implicit cross-application edges are forbidden. A dependency on an
  explicitly declared base application revision is permitted and visible in the graph.

### S0.6 — Registered directory/path/glob sources and overlays

- Remote registration selects a host-configured allowed-root ID plus a relative path or glob. It
  never accepts an unrestricted host path, creates a directory, or grants filesystem browsing.
  A trusted local administration tool may initialize a directory under an allowed workspace as a
  separate convenience.
- SQLite stores source identity, application, allowed-root identity, redacted relative
  specification, reader profile, trust class, explicit precedence, enabled state, registration
  revision, scan generation, observed hashes, errors, and effective/shadowed evidence.
- Source precedence is an integer unique within an application revision; greater values win.
  Equal precedence for the same logical identity is a conflict, not a path-order tie-breaker.
- Declared records resolve by `(application ID, record kind, declared local ID)`. Generic documents
  without declared IDs resolve by normalized relative path and cannot become executable merely by
  winning an overlay.
- Precedence applies only among equally trusted eligible application sources. Lower-trust content
  cannot override higher-trust content, and no application source can override `system.*`.
- One effective winner is chosen before catalog import, dependency analysis, lexical/vector
  indexing, or AI prompting. Shadowed records remain diagnostic evidence and do not execute or
  appear in ordinary results.
- Scanning and reordering create a candidate application revision. Removing or disabling an
  override reveals a lower source only after explicit preview and activation.
- Canonical host paths are retained only in trusted administration storage and logs. Remote results
  expose source IDs and redacted logical paths.

### S0.7 — Described, traversable catalogs

- A catalog collection is an application-declared logical collection of typed records; the kernel
  does not hard-code a game-specific collection list. Component types, projections, procedures,
  mechanics, events, subscriptions, and documents can participate through generic record
  adapters. Application/source administration remains a separate system catalog.
- A catalog node is identified by application revision, collection ID, and normalized logical path.
  Paths use `/`-separated validated logical segments, not operating-system paths; empty path is the
  collection root and `.`/`..` are forbidden.
- Nodes have application-authored title and description metadata. New published nodes require both.
  Migrated legacy nodes may use `descriptionStatus: "missing"`; the kernel never invents a
  description from a filename, directory, or model.
- Browse returns the selected node, breadcrumbs, direct and subtree counts by record kind, and only
  its direct child nodes and direct records. Child nodes sort by logical path; records sort by
  `(record kind, qualified ID)` unless a confirmed query requests deterministic lexical ranking.
- Exact inspection rehydrates an authoritative effective record version and its hash/provenance.
  A browse/search summary is a locator, not execution authority.
- Lexical search uses invariant Unicode normalization and tokenization with a stable priority:
  exact qualified ID, exact alias/match phrase, exact name, prefix match, then all-token textual
  match. Ties sort by `(record kind, qualified ID)`. The detailed score/version is advertised and
  included in the manifest so a future ranking change cannot silently reorder an existing cursor.
- Default page size is 25 and the public hard maximum is 100. The cursor is opaque and authenticated
  and binds the manifest fingerprint, application, collection, branch, filters, ranking/sort
  version, page size, and last stable key.
- Cursors have no initial wall-clock expiry while their immutable manifest and signing key remain
  retained. Tampering returns `CURSOR_INVALID`; a different active manifest returns `CURSOR_STALE`;
  an unavailable retained manifest/key returns `CURSOR_EXPIRED`. Each failure includes a restart
  request and never mixes revisions.
- Browse, lexical search, and exact inspection remain complete with local AI, embeddings, and
  vector search disabled. Vector retrieval is an optional downstream index over effective records.

### S0.8 — Public operation semantics

- Transport remains exactly `orient`, `query`, and `commit`; scope is expressed by the closed
  `kind`, never by adding application-specific transport verbs.
- The initial system query kinds are:
  `system.applications`, `system.sources`, `system.application-preview`, `system.dependencies`,
  `system.catalogs`, `system.catalog.browse`, `system.catalog.search`, and
  `system.catalog.record`.
- The initial system commit kinds are:
  `system.application.register`, `system.source.register`, `system.application.activate`,
  `system.state-space.create`, and `system.state-space.upgrade`.
- These names are reserved by this decision but are not registered until Slice 10 defines request,
  result, error, authorization, capability-discovery, compatibility, and protocol-walk contracts.
- Application actions and capabilities use their owning prefix, such as `dnd2024.*`. Generic ECS
  writes may be transported by a `system.*` kind only when they require an exact state space,
  qualified component type/version/hash, backend validation, typed effects, and the existing root
  transaction; applications do not add one platform verb per component.
- Preview and read queries have no active-manifest or runtime-state effect. Every administrative
  commit requires trusted administrative authorization, an idempotency key, expected revision or
  fingerprint, dry-run support where applicable, path/value redaction, and append-only evidence.
- Remote administration remains unavailable until the E9 authorization dependency is accepted.
  Local loopback is not implicitly trusted. Until then, internal ports and trusted in-process/local
  administration may be implemented, but Slice 10 cannot expose these commit kinds remotely.
- Catalog results are authorization-filtered before counts, pagination, search, and cursors are
  built, so hidden records cannot be inferred through totals or page gaps.

### S0.9 — Compatibility, failure, replay, and transaction policy

- Slice 1 inventories all existing IDs, component values/schemas, sources, and public kinds without
  mutation. Unresolved ownership blocks enforcement instead of receiving a guessed owner.
- Persistence is introduced additively: registries/revisions and nullable compatibility scope first,
  reviewed backfill second, dual-read comparison third, enforcement fourth, and destructive cleanup
  only at a separate confirmed gate.
- Legacy aliases are advertised compatibility adapters with audit and removal criteria. Their exact
  mapping and duration follow the Slice 1 inventory and require confirmation before Slice 10.
- Preview, activation, state-space upgrade, component writes, and administrative registration each
  have one declared transaction owner. A failure, stale fingerprint/revision, authorization denial,
  replay conflict, validation error, dependency conflict, or injected exception produces no partial
  active-manifest or runtime-state mutation.
- An identical idempotency-key replay returns the prior receipt. Reuse with a different canonical
  request fails. Activation commits only an exact previously validated candidate fingerprint;
  state-space upgrades remain separate.
- Existing typed effects, complete-batch validation, guards, events, notifications, deterministic
  replay, operation receipts, and one-root transaction ownership remain authoritative and must pass
  parity before migrated writes are enabled.

### S0.10 — AI boundary

- Local and remote models consume the same effective qualified records, catalog navigation,
  dependency evidence, and exact contracts exposed by the kernel.
- Models may propose plans, searches, calls, contracts, and bounded values. They do not become
  registration, overlay, schema-validation, authorization, migration, transaction, or execution
  authority by assertion.
- Local AI receives opaque application/source/logical identifiers and generic documents. It has no
  game assembly reference or hard-coded application vocabulary.
- Learned interaction recipes belong to the downstream interaction-orchestration owner and must be
  versioned against exact effective contracts. They are not ECS schemas or application-kernel
  authority.

## D&D 5e 2024 alignment

Not applicable. This slice creates ruleset-neutral platform semantics and no D&D rule, fixture,
formula, identifier mapping, or content. `dnd2024` appears only as the user-selected initial opaque
application ID.

## External implementation reference

No Foundry dnd5e review applies because this slice implements no game behavior. No external code or
licensed content is reused.

## Prerequisite evidence

- [Application-kernel dependency plan](APPLICATION-KERNEL-DEPENDENCY-PLAN.md): leaf A is the
  cross-owner semantic root and Slice 0 explicitly exits with decisions only.
- [Application-kernel agent guide](APPLICATION-KERNEL-AGENT-GUIDE.md): provides the system/app,
  datatype, projection, source, catalog, transaction, protocol, and AI invariants adopted here.
- Existing code/catalog ownership evidence is cataloged in the master plan. Slice 0 changes none of
  those owners and therefore does not reread or reinterpret their runtime behavior.
- E9 remains an explicit prerequisite for remote administrative authorization. This slice closes
  the deny-until-E9 policy, not the E9 implementation.

## Runtime artifacts

None. In particular, this slice creates no interface, schema, table, migration, kind registration,
alias, application, state space, source record, catalog record, component type, projection, or game
content.

The durable artifacts are this accepted semantic contract and its
[completion receipt](receipts/APPLICATION-KERNEL-SLICE-0-RECEIPT.md). The master plan links both.

## Authoritative state and closed input

There is no runtime request in Slice 0. The only input is an explicit user decision to accept this
package or amend named decision IDs. Repository prose does not substitute for that confirmation.

After acceptance, Slices 1–12 must treat S0.1–S0.10 as constraints. Serialized schemas, table names,
and internal interface names may be designed within those constraints by their owning slice; any
change to the stated meanings reopens Slice 0 confirmation.

## Behavior, result, and typed effects

No executable behavior or typed effect is added. The result is either:

1. all S0 decisions accepted together, allowing Slice 1 to be authored; or
2. one or more named decisions amended, with dependent language reconciled before acceptance.

The transaction owner is therefore **none** for this documentation-only slice.

## Failure, replay, and rollback contract

- A later partial or ambiguous amendment reopens the affected decision and blocks its dependent
  runtime/public work until the meaning is confirmed again.
- Conflicting requested amendments are written as open decisions; the implementation agent does not
  choose between them.
- Repeated identical approval is idempotent and does not create duplicate receipts.
- Rejection or amendment requires no rollback because Slice 0 has no runtime mutation.
- Unrelated dirty-worktree changes are preserved. Only this document and explicit link/status lines
  may be changed.

## Implementation sequence

The Slice 0 sequence is complete: S0.1–S0.10 were accepted, the receipt records the evidence and
exclusions, and dependency leaf A and its roadmap owner link that result. Further work starts from a
separate Slice 1 document; inventory, migration, registration, and runtime code remain outside this
slice.

## Acceptance matrix

| Case | Slice 0 evidence |
| --- | --- |
| Namespace | S0.1 reserves `system`, makes apps opaque, and leaves legacy ownership unresolved for inventory. |
| Application/state | S0.2 separates immutable application activation from isolated state-space upgrade. |
| Datatypes | S0.3 covers every JSON kind, object-only merge, null/remove, fidelity, and resource references. |
| Schema/version | S0.4 prevents in-place reinterpretation and closes offline validation authority. |
| Projection | S0.5 defines exact paths/roles, structural-only mapping, DAG/impact, and disposable caching. |
| Sources | S0.6 closes allowed roots, no remote creation, trust, precedence, winner, and redaction semantics. |
| Catalog | S0.7 closes authored metadata, traversal, deterministic ranking, bounds, and cursor failure behavior. |
| Protocol | S0.8 reserves the proposed kinds while gating remote administration on E9. |
| Compatibility | S0.9 requires additive inventory/backfill/dual-read/enforcement and preserves transaction parity. |
| AI | S0.10 keeps models downstream and proves deterministic discovery does not require them. |
| Ruleset neutrality | No D&D rule/content is introduced and zero-application operation remains required. |
| No runtime change | Diff contains planning evidence only; no source, catalog, schema, or migration artifact changes. |

## Verification commands

Documentation-stage verification only:

```powershell
git diff --check -- platform/application-kernel
git diff --name-only -- platform/application-kernel platform/PLATFORM-ENABLING-FEATURES-ROADMAP.md
```

Expected result: whitespace validation succeeds and the Slice 0 diff contains only this semantic
document plus link/status-only owner edits. Build, test, catalog validation, and protocol walk are
not applicable because this slice changes no executable/catalog/protocol artifact.

## Completion receipt and exit gate

The [completion receipt](receipts/APPLICATION-KERNEL-SLICE-0-RECEIPT.md) records the accepted S0
decision IDs, documentation verification, no runtime artifacts, and the deliberate deferral of
inventory, serialized contracts, numeric resource limits, persistence, migrations, authorization
implementation, aliases, application activation, and AI integration.

The exit gate is satisfied: the package is confirmed, the receipt and owner links exist, and this
document and dependency leaf are accepted. Work stops here; Slice 1 requires its own implementation
document.

# Application kernel Slice 10E implementation — authenticated dependency impact

Status: **accepted**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Application-kernel F/H dependency impact](APPLICATION-KERNEL-DEPENDENCY-PLAN.md)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Expose authenticated `system.dependencies` through `query`; traverse the persisted exact
component-field and projection dependency graph for one registered application with deterministic,
bounded, redacted impact evidence and no state change.  
Exclusions: Candidate-document parsing, schema compatibility judgment, mechanics/procedure/event/
subscription/catalog dependency declarations, JavaScript inference, activation, state-space
administration, cache, vectors/models, remote MCP, application migration, and game behavior.  
Allowed files/areas: projection-materialization contracts/persistence/hosting/tests, MCP query
surface/adapter/tests, system-use procedure/component metadata, this document/receipt, and
status-only roadmap/dependency updates. No migration or application/catalog content.  
Stop point: Stop when loopback MCP can list and traverse exact declared component-field and
projection dependents, denial occurs before identifier parsing/registry access, full graph evidence
survives result truncation, and no definition/application/source/active/runtime row changes.

## Confirmed decisions

- Slice 0 reserves `query(kind: "system.dependencies")` for bounded direct/transitive dependency
  review and forbids dependency inference from JavaScript source.
- Slice 7 accepted immutable exact component inputs, structural source pointers, projection inputs,
  and deterministic forward/reverse projection edges as impact evidence.
- On 2026-08-24 the user said “continue” after Slice 10E was named as dependency-impact inspection
  before activation. This confirms registration of the reserved query kind and this bounded purpose.
- The query reports its coverage. In this slice `component-field` and `projection` are indexed;
  mechanic, procedure, event, subscription, and catalog consumers are explicitly deferred. Their
  absence must never be presented as proof that they are unaffected.

## External implementation reference

No Foundry dnd5e review applies because this slice implements no game behavior. No external code or
licensed content is reused.

## Prerequisite evidence

- [Slice 7 receipt](receipts/APPLICATION-KERNEL-SLICE-7-RECEIPT.md) proves immutable projection
  definitions, exact component/schema references and paths, deterministic reverse edges, and
  read-only impact evidence.
- [Slice 10B receipt](receipts/APPLICATION-KERNEL-SLICE-10B-RECEIPT.md) proves private-operator
  authorization-before-parse and redacted authenticated registry queries.
- [Slice 10D receipt](receipts/APPLICATION-KERNEL-SLICE-10D-RECEIPT.md) proves the exact application
  preview remains disposable and does not activate or persist candidates.

## Runtime artifacts

- Extend the projection registry with a read-only application impact snapshot containing canonical
  nodes and dependency-to-consumer edges. Existing definition and materialization behavior remains
  unchanged.
- Component-field nodes use exact registered type ID, version, schema hash, and RFC 6901 pointer.
  Projection nodes use exact qualified ID, version, and content hash. An input mapped from the
  component root is conservatively a whole-component read.
- Add one pure impact traversal service/port. It calculates a stable full-graph SHA-256 fingerprint,
  supports an optional exact root node, and returns deterministic dependent depth and edge reason.
- Add `system.dependencies` to the query catalog/dispatcher. Inputs are required `applicationId`,
  optional `id`, optional `transitive` (default true), and optional `limit` (default 100, 1–250).
  Without `id`, return a bounded graph inventory; with it, return direct or transitive dependents.
- Add no table, migration, application fixture, catalog record, activation record, cache, vector
  index, or AI prompt.

## Authoritative state and closed input

SQLite projection definitions and their exact component/projection input rows are authoritative.
The backend derives node hashes, edges, depth, reasons, coverage, counts, and graph fingerprint.
The caller may supply only the application ID, one canonical node ID, traversal flag, and result
limit. It cannot supply definitions, hashes, edges, consumers, compatibility conclusions,
application scope, principal, or activation state.

Canonical query node IDs are `component:<qualifiedTypeId>@<version>` with optional `#<RFC 6901
pointer>` and `projection:<qualifiedProjectionId>@<version>`. A whole-component root selects every
declared pointer for that exact type version. Returned nodes also carry their exact schema/content
hash separately.

## Behavior, result, and typed effects

Authorization for private-operator `Read` runs before application/node parsing or registry lookup.
The registry reads all immutable projection versions for the application and emits a stable graph:
each declared component mapping creates a `reads-component-field` edge to its projection; each
projection input creates a `depends-on-projection` edge from dependency to consumer. Duplicate
edges collapse. Traversal breadth is deterministic lexical order; the shortest dependent depth is
reported once per node.

The result includes application ID, full graph fingerprint and counts, indexed/deferred consumer
kinds, selected root, bounded nodes/edges or dependents, limit, and truncation. Counts and
fingerprint always describe the complete graph, not only returned details. Typed effects: none;
only normal query audit appends.

## Failure, replay, and rollback contract

Unauthorized requests return the shared private-operator denial before parsing. Invalid
application/node/limit input returns a closed typed error. Unknown applications and unknown exact
nodes return `APPLICATION_UNKNOWN` and `DEPENDENCY_NODE_UNKNOWN`. An unavailable registry/service
returns `DEPENDENCIES_UNAVAILABLE`; unexpected failures return `DEPENDENCIES_FAILED` without
database or exception detail. Empty registered applications return a valid empty inventory.

Equal persisted definitions produce identical ordering and fingerprint. A definition, exact input,
source pointer, mapping, or dependency change changes the full graph evidence. Cancellation or
failure creates no registry, application, source, active-manifest, or runtime-state change.

## Implementation sequence

1. Add snapshot/traversal contracts and focused deterministic field/projection/transitive/empty
   tests to the projection owner.
2. Implement read-only persisted graph extraction and pure traversal; keep existing registration
   and materialization transactions untouched.
3. Add the authenticated query adapter, capability contract, procedure/component metadata,
   denial-before-lookup test, and live three-verb protocol walk.
4. Run focused tests, fresh catalog validation, full shared/local-AI suites, warning-free build,
   and `git diff --check`; record the receipt and update owner status.

## Acceptance matrix

| Concern | Required evidence |
| --- | --- |
| Positive | Exact component field and projection roots return direct/transitive dependents with reasons. |
| Conservative root | A whole-component node selects every declared field read for its exact type/version. |
| Authorization | Missing/remote context denies before invalid application/node parsing or registry access. |
| Bounds | Detail limit truncates only details; complete counts/fingerprint remain unchanged. |
| Determinism | Equal definitions produce identical graph fingerprint, order, and shortest depths. |
| Coverage | Indexed and deferred consumer kinds are explicit; no source-code inference occurs. |
| No change | Projection/application/source/active/runtime state is unchanged; only query audit appends. |
| Surface | Capabilities, dispatcher, descriptions, procedure, guards, and three-tool walk agree. |

## Verification commands

- Focused projection-impact, authorization, protocol, guard, and bootstrap-contract tests.
- `dotnet run --project DantesRoleplay.Tools -- validate catalog`
- Full `DantesRoleplay.Tests` and local-AI suites.
- Warning-free solution build, live three-verb JSON-RPC walk, and `git diff --check`.

## Completion receipt and exit gate

Accepted evidence: [Slice 10E receipt](receipts/APPLICATION-KERNEL-SLICE-10E-RECEIPT.md).

The slice stopped before candidate parsing, dependency compatibility decisions, activation,
state-space administration, application migration, or AI orchestration.

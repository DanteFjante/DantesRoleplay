# Application kernel Slice 7 implementation — versioned structural projections

Status: **accepted**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Generic application kernel dependency plan](APPLICATION-KERNEL-DEPENDENCY-PLAN.md), F  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Persist immutable application-owned projection definitions and materialize bounded,
structural JSON read models from exact committed ECS component versions and other exact projections.  
Exclusions: Application activation/effective-manifest parsing; legacy projection or `IWorldStore`
integration; formula/default/conditional/aggregation logic; JavaScript execution; cache persistence;
effects/events/audit/protocol/authorization; catalog source import; application-specific IDs or rules.  
Allowed files/areas after confirmation: `src/system/projection-materialization/{domain,persistence,hosting,tests}/`,
`src/system/ecs/{domain,persistence,tests}/` only for one bounded batch-read port,
`src/system/schema-validation/{domain,persistence,tests}/` only through the accepted profile,
data-access mapping, one additive migration/model snapshot, focused tests, this document, its
receipt, and status/link-only plan/roadmap updates. Legacy state, mechanics, catalog, MCP, host,
local-AI, and application files are read-only.  
Stop point: Immutable projection contracts, one acyclic structural materializer, batch-read evidence,
schema-validated frozen outputs, and reverse impact results pass tests; stop before cache,
activation, effects, legacy parity, or public transport.

## Confirmation required

Approve this package before implementation. It replaces the Slice 2 in-memory projection record
with a richer internal contract; no protocol format or application declaration format is created.

1. Add five additive, generic SQLite tables, with a forward-only `Down` path and no legacy
   backfill: `system_projection_definition`, `system_projection_definition_version`,
   `system_projection_component_input`, `system_projection_dependency_input`, and
   `system_projection_mapping`. They store only qualified IDs, immutable versions/hashes, accepted
   output schema JSON/profile/hash, exact component/projection references, role bindings, pointers,
   structural mappings, and timestamps.
2. A trusted in-process definition request supplies owner application, qualified projection ID,
   output schema, component inputs, projection inputs, and mappings. The registry validates every
   supplied exact component/projection reference and computes normalized output-schema/hash,
   definition content hash, and next contiguous version. Callers cannot supply a version,
   profile/hash, graph success, or output-validation success. Equal canonical replay returns its
   original version; changed canonical content appends one version.
3. A component input has a unique input ID, an entity-role name, and an exact component type
   ID/version/hash. A projection input has a unique input ID, an exact projection ID/version/hash,
   and a closed mapping from each dependency entity-role name to one caller-supplied parent role.
   Inputs and projections must be owned by the same application in this slice. An application-base
   expansion is deferred until state-space contracts support it explicitly.
4. A mapping copies one RFC 6901 source pointer from one declared input to one RFC 6901 output
   target pointer. Output targets are either one root target (`""`, the only mapping) or object
   member paths; arrays may be copied as values but are not assembled element-by-element. Duplicate
   targets, missing input IDs, malformed pointers, unsupported output construction, source paths
   absent from an exact component/output schema, graph cycles, unknown references, and bounds fail
   registration without rows.
5. Fixed bounds are: at most 32 component/projection inputs total, 128 mappings, 16 transitive
   dependency levels, 64 role bindings per request, 256 distinct entity/component reads, and a
   1 MiB serialized output. These limits, plus Slice 5 validation limits, are closed host policy.
6. `IProjectionMaterializer` receives a state-space ID, exact projection ID/version/content hash,
   and a role-to-entity-ID binding. It constructs one stable topological plan, recursively binds
   dependent roles, deduplicates component reads, uses one bounded ECS batch read, structurally
   copies only declared JSON paths, validates every intermediate/final output against its exact
   output schema, serializes compact JSON, and returns immutable output plus exact source component
   revisions. It has no database/query capability in the returned result.
7. Projection outputs are ephemeral. This slice adds no cache; therefore no cached result can
   authorize a write or survive a source revision. The registry exposes deterministic forward and
   reverse edges as impact evidence only, not as automatic compatibility/migration authority.

## Confirmed decisions

- User approval, 2026-08-24: implement the confirmation package above as Slice 7.
- [Slice 0](APPLICATION-KERNEL-SLICE-0-IMPLEMENTATION.md) reserves application ownership, exact
  component versions, JSON values, structural-only mapping, acyclic dependencies, reverse impact,
  and non-authoritative derived results.
- [Slice 2](APPLICATION-KERNEL-SLICE-2-IMPLEMENTATION.md) proves basic pure ID/pointer/cycle
  validation; this slice supersedes its in-memory projection record with persisted output-schema and
  projection-input semantics.
- [Slice 5](APPLICATION-KERNEL-SLICE-5-IMPLEMENTATION.md) owns normalized JSON Schema contracts.
- [Slice 6](APPLICATION-KERNEL-SLICE-6-IMPLEMENTATION.md) owns committed, state-space-isolated
  canonical component values and exact versions; this slice reads them only.
- E6 and E7 retain dependent mechanic execution and uncommitted root-local state respectively.
  Ordinary projections here see committed canonical values only.

## Prerequisite evidence

- The current projection component is a pure scaffold with sources/dependencies/mappings but no
  persistence, output schema, input aliases, materializer, cache, or legacy consumer.
- The Slice 6 ECS port is the first application-scoped canonical state owner and can gain only the
  bounded batch read required by this slice.
- No active application manifest exists; projection registration is trusted in-process evidence and
  must not be treated as activation.

## Runtime artifacts after confirmation

- Closed versioned projection definition/input/mapping/reference/result contracts.
- SQLite projection registry with immutable replay, graph validation, and forward/reverse impact
  reads.
- One structural projection materializer and one ECS batch-read method restricted to declared
  state-space/entity/type inputs.
- One additive migration/model snapshot and no persistent projection output/cache table.

## Authoritative state and closed input

SQLite is authoritative for projection definitions and canonical ECS components. The component and
projection registries are authoritative for exact contract bytes/hashes. The schema validator is
authoritative for outputs. A caller can bind declared role names to entity IDs and request an exact
projection reference; it cannot add inputs/mappings, choose undeclared components, claim a source
revision, provide an intermediate output, bypass validation, or make a result canonical state.

## Behavior, result, and typed effects

Registration validates one complete definition graph before writing its immutable rows. Materializing
one projection computes dependencies first in deterministic topological order, reads only declared
components in one batch, copies declared values without calculation/coercion, validates/freeze each
output, and returns a compact JSON result with exact source revision evidence. Typed effects: none.
The projection registry owns definition transactions; materialization is read-only.

## Failure, replay, and rollback contract

- Unknown/wrong-application/stale type or projection references, malformed schemas/pointers,
  duplicate input/target, missing paths/roles/components, cycles, bound excess, and output schema
  failure create no definition row and return no partial output.
- Equal definition replay returns stored evidence; changed definitions append, never mutate.
- A missing/deleted component/entity or stale source contract rejects materialization; no state
  mutation, cache, or fallback read occurs.
- A source component revision changing after materialization only makes a later request recompute;
  no result is stored or reused.
- Migration failure rolls back and preserves all legacy and Slice 6 tables. Recovery is
  restore-from-backup, not an automatic destructive downgrade.

## Implementation sequence

1. Write definition graph, pointer/schema-path, role-binding, bounded batch-read, source-revision,
   materialization, and no-change tests first.
2. Replace the pure projection scaffold with closed versioned contracts and registry validation.
3. Add persistence/migration, then the bounded ECS batch-read seam and read-only materializer.
4. Verify fresh/upgrade migrations, frozen output, reverse impact, scope isolation, and zero cache
   behavior.
5. Run focused/full tests, write the receipt, and stop before Slice 8 or a public/legacy connection.

## Acceptance matrix

| Area | Required evidence |
| --- | --- |
| Registry | Owner-qualified definitions persist/reload; canonical replay/append works; invalid graph leaves no rows. |
| Graph | Exact component/projection refs, role bindings, missing paths, duplicate targets, cycles, bounds, and reverse impact are deterministic. |
| Materialization | Multi-component and dependent projections use one bounded batched read, copy only declared paths, validate/freeze all outputs, and return source revisions. |
| Isolation | Cross-state-space/application entity/type/projection access fails with no undeclared data in output. |
| Safety | No calculations, dynamic code, cache, state mutation, raw database access, or legacy data path exists. |
| Migration | Fresh/upgrade databases gain only projection tables; downgrade refuses destruction; model drift/catalog coverage pass. |
| Repository | Focused projection/ECS/schema/migration tests, build, full suite, and `git diff --check` pass. |

## Completion receipt and exit gate

Record evidence in `platform/application-kernel/receipts/APPLICATION-KERNEL-SLICE-7-RECEIPT.md`.
Do not begin Slice 8, activation, cache, mechanics/effects integration, legacy parity, catalog
import, protocol, authorization, or AI work.

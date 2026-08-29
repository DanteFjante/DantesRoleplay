# Application kernel Slice 6 implementation — application-scoped ECS state

Status: **accepted**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Generic application kernel dependency plan](APPLICATION-KERNEL-DEPENDENCY-PLAN.md), C / D / E  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Add a generic, state-space-isolated entity/component store that persists every bounded JSON
kind against an exact immutable application component-type version.  
Exclusions: Changes to legacy `entity`, `component_definition`, `component`, containment, or
relationship tables and `IWorldStore`; legacy backfill/dual-read parity; application activation or
upgrade; effects, events, audit, catalog import, protocol kinds, authorization, projections, AI,
aliases, and application-specific logic.  
Allowed files/areas after confirmation: `src/system/ecs/{domain,persistence,hosting,tests}/`,
`src/system/schema-validation/{domain,persistence,tests}/` only for generic value helpers,
data-access mapping, one additive EF migration/model snapshot, focused tests, this document, its
receipt, and status/link-only plan/roadmap updates. Existing state/world, catalog, MCP, host, and
application files are read-only.  
Stop point: The new ECS port persists and retrieves all allowed JSON kinds in isolated state spaces,
enforces exact registered contracts and optimistic revisions, and passes its no-change/migration
tests; stop before any legacy or public-surface connection.

## Confirmation required

Slice 0 confirms the semantic direction but defers the concrete state tables, write envelope, and
pre-activation binding policy. Approve this package before implementation:

The user approved this package on 2026-08-24. The state-space binding remains explicitly
pre-activation and trusted in-process only until its later activation verification gate.

1. Add only these generic, additive SQLite tables:
   - `system_state_space` — opaque state-space ID, application ID, positive application revision,
     64-character manifest fingerprint, and creation time;
   - `system_ecs_entity` — `(state_space_id, entity_id)`, name, positive entity revision,
     creation time, and optional deletion time; and
   - `system_ecs_component` — `(state_space_id, entity_id, qualified_type_id)`, positive exact
     type version, exact 64-character schema hash, JSON value, positive instance revision, and
     timestamps.
   The forward migration backfills nothing and never changes legacy world tables. Its `Down` path
   refuses destructive automatic downgrade.
2. Introduce internal ports `IStateSpaceRegistry` and `IEntityComponentStore`. A trusted in-process
   caller creates a state space with an exact `StateSpaceBinding`; the registry verifies the
   registered application revision and fingerprint shape, but cannot verify an active manifest
   because activation/effective-manifest persistence is intentionally later. The binding is
   immutable. Slice 8/activation must verify it against an activated manifest before migrated
   gameplay writes are enabled.
3. A component write carries only a state-space ID, entity ID, exact qualified type ID/version/hash,
   JSON value, and expected component revision. `0` means the component must be absent; a positive
   revision must exactly match an existing component. The backend resolves the stored type, verifies
   same-application ownership and exact hash, validates the value with
   `system-json-schema-2020-12/v1`, then writes. Callers cannot supply application ownership,
   schema text, validation success, an active type, or a database key.
4. `add` requires expected revision `0` and fails if the component exists. `set` writes an absent
   component at revision `1` or replaces a matching existing component at the next revision.
   `merge` follows the same revision rule but accepts only object input and an existing object,
   shallowly merges top-level properties, then validates the complete result. `remove` requires a
   matching positive revision. JSON `null` is a valid stored value when allowed and is never a
   remove request.
5. Reads and mutations always require the state-space ID. Entity IDs are unique only within that
   state space; a component cannot be read or written through another state space. Entity soft
   deletion and entity revision are internal generic evidence, but no containment, relationship,
   event, effect, or operation-record behavior is copied into this parallel store.
6. The schema validator parses values under its accepted bounds. Set/add retain the validated root
   JSON text without coercing numeric representation or JSON kind. Merge may serialize its new
   object result after the generic shallow merge. At most the profile's 32 diagnostics are exposed,
   with no raw values, paths, or parser exceptions in failures.

## Confirmed decisions

- [Slice 0](APPLICATION-KERNEL-SLICE-0-IMPLEMENTATION.md) fixes application/state-space identity,
  all-JSON component semantics, exact schema versions/hashes, object-only merge, null-versus-remove,
  and no-change failure.
- [Slice 2](APPLICATION-KERNEL-SLICE-2-IMPLEMENTATION.md) supplies immutable state-space bindings
  and qualified type/version contracts.
- [Slice 5](APPLICATION-KERNEL-SLICE-5-IMPLEMENTATION.md) supplies the application type registry
  and closed validator profile; it deliberately does not select active types or alter legacy state.
- Current `IWorldStore` and legacy world tables remain a compatibility owner. This slice must not
  expose, update, or backfill them.

## Prerequisite evidence

- The Slice 5 [receipt](receipts/APPLICATION-KERNEL-SLICE-5-RECEIPT.md) proves exact component type
  versions and the bounded validator profile, with no state write connection.
- `IWorldStore` currently stores only unscoped object JSON and mutates legacy definitions in place;
  it therefore cannot be reused as the authoritative application ECS port.
- No active application-manifest persistence exists yet, so the provisional trusted binding policy
  above is deliberate and must not be mistaken for application activation.

## Runtime artifacts after confirmation

- Closed state-space/entity/component views and write requests in the ECS domain.
- SQLite state-space and entity-component adapters behind the new ports, registered through the
  ECS component only.
- A generic state-space-scoped JSON value helper; it has no application branches or CLR type
  deserialization.
- One additive migration and model snapshot update.

## Authoritative state and closed input

SQLite is authoritative for new state spaces, entities, component instances, their exact type
references, values, and revisions. The type registry is authoritative for schema contract bytes and
hashes. The evaluator is authoritative for validation. A caller may choose opaque state/entity IDs,
name, exact known contract reference, value, and expected revision; it cannot choose an application
for an entity/component, type ownership, schema, hash truth, evaluator result, revision result, or
legacy mapping.

## Behavior, result, and typed effects

The store validates every request before opening a write transaction, then rechecks current
component/entity rows and writes one row atomically. It returns immutable snapshots containing exact
type/version/hash, raw validated value text, and revision. A state space binds to exactly one
application revision and manifest fingerprint; types owned by another application are rejected.
No typed effect, event, audit record, protocol response, or active-manifest transition is produced.
The state-space/entity-component registry is the sole transaction owner in this slice.

## Failure, replay, and rollback contract

- Unknown/deleted entities, unknown state spaces/types, cross-application type references,
  hash/version mismatches, malformed/oversized values, schema failure, invalid merge kinds,
  duplicate add, missing remove, and stale revisions create no rows or revision changes.
- Repeating an equal write only succeeds when its expected revision still matches; retries must
  read the returned revision. No implicit idempotency key or blind overwrite is introduced.
- Cross-state-space reads and writes return no foreign entity/component data.
- Migration/repository failures roll back the attempted transaction and preserve legacy tables.
- Recovery from the migration is restore-from-backup, never automatic deletion of state-space or
  component evidence.

## Implementation sequence

1. Write type/reference, all-JSON, merge, revision, scope, and no-change tests first.
2. Add closed domain contracts and pure value/identifier guards without changing legacy state.
3. Implement component-owned SQLite adapters and EF mapping; generate and inspect the additive
   migration with forward-only `Down` behavior.
4. Verify fresh and pre-slice databases, scope isolation, contract/schema enforcement, and all
   JSON kinds.
5. Run focused/full tests, record the receipt, update status, and stop before Slice 7 or any
   legacy/effect/protocol connection.

## Acceptance matrix

| Area | Required evidence |
| --- | --- |
| State space | Exact registered application revision persists; malformed binding, duplicate ID, and unknown application/revision fail without rows. |
| JSON values | Object, array, string, integer, non-integer number, boolean, and null round-trip exactly when their exact type schema permits them. |
| Contract | Unknown/cross-app type, mismatched version/hash, and invalid schema value fail with no component mutation. |
| Mutation | Add/set/merge/remove enforce expected revisions; merge rejects scalar/array/null inputs or existing values and validates the merged result. |
| Isolation | Same entity ID and type may exist independently in separate spaces; no cross-space read/write succeeds. |
| Migration | Fresh and upgrade databases gain only the three tables; legacy world rows/tables remain unchanged; downgrade refuses destruction. |
| Repository | Focused ECS/schema/migration tests, model-drift/catalog-coverage tests, build, full suite, and `git diff --check` pass. |

## Verification commands

    dotnet test DantesRoleplay.Tests\DantesRoleplay.Tests.csproj --no-restore --filter ApplicationScopedEcs
    dotnet test DantesRoleplay.Tests\DantesRoleplay.Tests.csproj --no-restore --filter ComponentTypeRegistry
    dotnet test DantesRoleplay.Tests\DantesRoleplay.Tests.csproj --no-restore --filter Migration
    dotnet build DantesRoleplay.slnx --no-restore
    dotnet test DantesRoleplay.Tests\DantesRoleplay.Tests.csproj --no-restore --no-build
    git diff --check

## Completion receipt and exit gate

Record evidence in `platform/application-kernel/receipts/APPLICATION-KERNEL-SLICE-6-RECEIPT.md`.
Do not begin Slice 7, application activation, legacy state migration, transaction/effect parity,
catalog import, protocol operations, or AI integration.

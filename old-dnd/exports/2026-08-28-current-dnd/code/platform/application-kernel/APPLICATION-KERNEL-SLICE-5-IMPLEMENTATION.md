# Application kernel Slice 5 implementation — versioned component types and bounded JSON Schema

Status: **accepted**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Generic application kernel dependency plan](APPLICATION-KERNEL-DEPENDENCY-PLAN.md), C / E  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Persist immutable application-owned component type versions and provide one bounded,
offline JSON Schema 2020-12 validation profile for later ECS writes.  
Exclusions: Legacy `component_definition` replacement/backfill; component-instance writes or schema
enforcement; state-space binding; catalog parsing/import; application activation; effects;
protocol kinds; aliases; and application-specific IDs, branches, or rules.  
Allowed files/areas after confirmation: `src/system/ecs/{domain,persistence,hosting,tests}/`, a new
`src/system/schema-validation/{domain,persistence,hosting,tests}/` component, data-access mapping,
one additive EF migration/model snapshot, focused tests, this document, its receipt, and
status/link-only plan/roadmap updates. Existing world/catalog state, MCP, application host, and
local-AI code are read-only.  
Stop point: The immutable type registry and evaluator profile pass its schema/value/resource-bound
tests; record the receipt and stop before connecting it to ECS state, catalog records, source
overlays, or application activation.

## Confirmation required

Slice 0 fixed the semantic direction but deferred the concrete registry schema and resource policy.
The user approved this proposal on 2026-08-24. The application-kernel plan still requires the named
Sol-level schema-security review before any migration or runtime implementation is created:

The Sol-level review completed on 2026-08-24. It confirmed this closed profile, offline reference
policy, additive persistence boundary, and forward-only recovery contract are implementation-safe.

1. Add generic append-only SQLite tables `system_component_type` (qualified type ID and owner
   application) and `system_component_type_version` (type ID, positive contiguous version, schema
   JSON, SHA-256 content hash, profile version `1`, and creation time). They do not modify or
   reference legacy `component_definition` or `component` rows. The forward migration backfills no
   types or values and its `Down` path refuses destructive automatic downgrade.
2. A type ID must use its owner application's prefix and every new version appends exactly one.
   Equal replay returns the stored version; changed content for an existing version and skipped
   versions fail without mutation. A later activation slice, not this one, selects an application's
   effective type version.
3. Schema profile `system-json-schema-2020-12/v1` accepts JSON Schema Draft 2020-12 documents
   using only `$schema` (when exactly the official 2020-12 meta-schema URI), `$defs`, same-document
   fragment `$ref`, `type`, `enum`, `const`, `allOf`, `anyOf`, `oneOf`, `not`, `properties`,
   `required`, `additionalProperties`, `minProperties`, `maxProperties`, `items`, `prefixItems`,
   `minItems`, `maxItems`, `uniqueItems`, `minLength`, `maxLength`, `minimum`, `maximum`,
   `exclusiveMinimum`, `exclusiveMaximum`, and `multipleOf`. Boolean schemas are allowed. Every
   other keyword—including external `$ref`, `format`, `pattern`, `patternProperties`, content
   keywords, and conditional/dependent/unevaluated keywords—rejects the type definition rather
   than being ignored.
4. Profile limits are: schema ≤ 64 KiB UTF-8, schema depth ≤ 32, schema nodes ≤ 2,000, `$defs` ≤
   128, fragment references ≤ 256, value ≤ 1 MiB UTF-8, value depth ≤ 64, value nodes ≤ 10,000,
   and at most 32 validation errors returned. The evaluator parses with bounded JSON options and
   has no network, filesystem, CLR type loading, dynamic code, or external reference resolver.
5. Type/schema fingerprints use SHA-256 of a deterministic compact JSON serialization preserving
   the parsed property/array order. Whitespace-only changes therefore replay; any other serialized
   change becomes a new immutable version. This is a version identity rule, not semantic
   equivalence inference.

## Confirmed decisions

- [Slice 0](APPLICATION-KERNEL-SLICE-0-IMPLEMENTATION.md) makes component values bounded generic
  JSON, makes schemas immutable versioned contracts, and prohibits external `$ref` resolution.
- [Slice 2](APPLICATION-KERNEL-SLICE-2-IMPLEMENTATION.md) establishes owner-qualified component
  IDs and contiguous immutable versions in a pure in-memory registry.
- [Slice 3](APPLICATION-KERNEL-SLICE-3-IMPLEMENTATION.md) establishes additive registry persistence
  and non-destructive rollback policy; this slice follows the same policy.
- Existing `component_definition` is explicitly unscoped, mutable legacy state and remains an
  untouched compatibility owner until a later migration/parity slice.

## Prerequisite evidence

- `JsonSchema.Net` is already a data-access dependency used by existing generic payload validation,
  but it currently has no application type registry or bounded profile owner.
- `src/system/ecs/domain/ComponentTypeContracts.cs` supplies the current pure registry seam.
- Slice 4's [receipt](receipts/APPLICATION-KERNEL-SLICE-4-RECEIPT.md) proves source overlays remain
  non-executable and does not authorize parsed component contracts.

## Runtime artifacts after confirmation

- An immutable `ComponentTypeDefinition`/version contract, SQLite registry adapter, and additive
  migration internal to the ECS component.
- A generic schema-profile validator with closed compile/evaluate results and bounded diagnostic
  output; it exposes no raw parser exception or host path.
- One profile identity string embedded into each stored type version and hash calculation.

## Authoritative state and closed input

SQLite becomes authoritative for new application component type versions. A trusted internal caller
supplies application ID, qualified type ID, and schema JSON; the registry computes normalized
schema, profile ID, hash, version/replay result, and database keys. Callers cannot supply a hash,
version, evaluator success, external resolver, or a legacy component definition mapping. The
validator receives closed schema/value bytes and fixed profile limits only.

## Behavior, result, and typed effects

The registry validates type ownership and schema profile before opening a write transaction. It
normalizes valid schema JSON, computes its hash, then appends the next contiguous version or
returns an exact prior replay. Type reads return immutable schema/version/hash contracts. Schema
evaluation reports `valid`, `invalid`, or `rejected` with at most 32 deterministic pointer/message
diagnostics. No evaluator result authorizes a state write in this slice. Typed effects: **none**.
Transaction owner: the component-type registry only.

## Failure, replay, and rollback contract

- Invalid owner/type IDs, unknown applications, malformed or oversized/deep schemas, unsupported
  keywords, external/missing/cyclic references, and profile-limit violations create no type/version
  row.
- Any JSON kind is valid input to the evaluator when its registered schema permits it; malformed,
  oversized, over-deep, or node-excess value input returns a bounded rejection with no throw or
  state mutation.
- Replaying identical normalized schema content returns its original version. Changed content at an
  old version, a skipped version, schema hash mismatch, or a failed concurrent insert leaves the
  registry unchanged.
- The migration is additive and intentionally forward-only. Existing components/definitions remain
  unchanged; recovery is restore-from-backup, never automatic deletion of type history.

## Implementation sequence after confirmation

1. Write profile compilation/evaluation, fingerprint, registry replay, and no-change tests first.
2. Implement the closed JSON Schema profile and resource guards using the existing JSON Schema
   library with no external resolver.
3. Add component-owned persistence, EF mapping, DI registration, and one additive migration.
4. Verify fresh/upgrade migration, profile/resource boundaries, and full repository behavior.
5. Write the receipt, update status once, and stop.

## Acceptance matrix

| Area | Required evidence |
| --- | --- |
| Registry | Owner-qualified type persists/reloads; contiguous append/replay succeeds; changed duplicate, skipped version, and cross-application type fail without rows. |
| Schema kinds | Object, array, string, integer, non-integer number, boolean, and null validate exactly when allowed. |
| Profile | Malformed JSON, unsupported keyword, external/missing fragment reference, oversized/deep/node-excess schema, and invalid schema value are rejected deterministically. |
| Resource limits | Oversized/deep/node-excess value yields bounded diagnostics and no exception/state mutation. |
| Offline | Tests prove no external resolver/network/filesystem path is accepted for `$ref`. |
| Migration | Fresh and pre-slice databases upgrade with no legacy component/table mutation; migration drift passes. |
| Repository | Focused tests, migration tests, build, full suite, and `git diff --check` pass. |

## Verification commands

```powershell
dotnet test DantesRoleplay.Tests\DantesRoleplay.Tests.csproj --no-restore --filter ComponentTypeRegistry
dotnet test DantesRoleplay.Tests\DantesRoleplay.Tests.csproj --no-restore --filter SchemaValidation
dotnet test DantesRoleplay.Tests\DantesRoleplay.Tests.csproj --no-restore --filter Migration
dotnet build DantesRoleplay.slnx --no-restore
dotnet test DantesRoleplay.Tests\DantesRoleplay.Tests.csproj --no-restore --no-build
git diff --check
```

## Completion receipt and exit gate

Record the result in `platform/application-kernel/receipts/APPLICATION-KERNEL-SLICE-5-RECEIPT.md`.
Do not begin Slice 6 or attach schema validation to legacy/new ECS writes, catalog import, source
documents, application activation, or a protocol kind.

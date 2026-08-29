# Application kernel Slice 2 implementation — pure contracts and in-memory validation

Status: **accepted**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Generic application kernel dependency plan](APPLICATION-KERNEL-DEPENDENCY-PLAN.md), C  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Add ruleset-neutral, persistence-free application-kernel contracts and deterministic
in-memory fakes for application/state-space identity, source precedence, component-type versions,
projection dependency validation, and catalog cursor binding.  
Exclusions: SQLite tables/migrations, filesystem scans, catalog import/materialization, JSON Schema
evaluation, ECS write integration, effects, protocol kinds/dispatch, application activation against
runtime state, legacy record migration, aliases, and any `dnd2024` branch in generic code.  
Allowed files/areas: New `domain/` and `tests/` files under `src/system/application-registry/`,
`source-registry/`, `ecs/`, `projection-materialization/`, and `catalog-navigation/`; this document,
its receipt, and status/link-only plan/roadmap updates. Existing code/catalog are read-only.  
Stop point: Contract/fake tests prove the stated pure invariants; write the receipt and stop before
registry persistence in Slice 3.

## Confirmed decisions

- [Slice 0](APPLICATION-KERNEL-SLICE-0-IMPLEMENTATION.md) fixes opaque application IDs, reserved
  `system`, immutable revisions, state-space pinning, any-JSON semantics, structural-only
  projections, trusted overlays, and manifest-bound catalog cursors.
- [Slice 1](APPLICATION-KERNEL-SLICE-1-IMPLEMENTATION.md) inventories the legacy boundary.
- [Legacy ownership ratification](LEGACY-OWNERSHIP-RATIFICATION.md) targets all currently unresolved
  gameplay records to `dnd2024` without allowing generic code to name it.
- Slice 2 creates no permanent runtime/public ID or schema. Its C# names are internal contracts;
  public serialization remains Slice 10 and persistence remains Slice 3.

## Prerequisite evidence

- `DantesRoleplay/DantesRoleplay.csproj` compiles `src/system/**/domain/*.cs` into the generic core
  assembly, and `DantesRoleplay.Tests/DantesRoleplay.Tests.csproj` compiles colocated tests.
- Existing `IWorldStore` is the legacy unscoped/mutable owner. It is not changed in this slice.
- Existing catalog and local-AI scanner are read-only evidence; neither is an application/source
  registry.

## Runtime artifacts

- Opaque identifier/value models and interfaces for application revisions/state spaces, source
  registrations, component type versions, projection definitions, and catalog cursors.
- In-memory implementations used only by focused contract tests.
- Deterministic validators for reserved/malformed IDs, duplicate source precedence, qualified-key
  ownership, component/projection dependency cycles, and cursor binding/tampering.

No database table, host registration, file scan, application activation, or protocol operation is
added.

## Authoritative state and closed input

All inputs are explicit in immutable request/value models. Fakes receive values only from the test;
they do not read files, environment, database, network, or application-specific data. Application
IDs use the accepted grammar and are opaque after validation. Every component/projection reference
uses an exact qualified ID/version plus RFC 6901 pointer; state spaces receive an exact immutable
application-manifest fingerprint.

## Behavior, result, and typed effects

- Application registration rejects `system`, malformed/duplicate IDs, unknown bases, and base
  cycles. Revisions append and compute stable fingerprints from canonical values.
- Source validation rejects paths outside the supplied allowed-root relation, malformed relative
  paths/globs, duplicate logical identity at equal precedence, and lower-trust attempted overrides.
- Component-type contracts reject an ID outside the owner namespace and any mutation of an existing
  version/hash.
- Projection validation rejects missing sources, invalid pointers, duplicate targets, hidden
  cross-application edges, non-structural operations, and cycles; it returns stable forward/reverse
  edges without reading state.
- A catalog cursor binds its immutable manifest/filter/sort/page-size/last-key payload and detects
  altered or changed binding values. It carries no host path or record payload.

No typed effects are produced; transaction owner is **none**.

## Failure, replay, and rollback contract

Every rejected input leaves the corresponding fake unchanged. Replaying an identical immutable
registration request yields its existing result; reusing an identity with changed canonical input
rejects. Fakes expose no mutable references to stored contracts. No source scan, cache, state
write, or authorization decision occurs.

## Implementation sequence

1. Add smallest opaque models/interfaces and canonical validation helpers.
2. Add in-memory registries/validators behind those interfaces.
3. Add focused boundary/no-change/determinism tests.
4. Run focused tests, solution build, and `git diff --check`.
5. Write receipt/update once and stop before Slice 3.

## Acceptance matrix

| Area | Required proof |
| --- | --- |
| IDs/revisions | Reserved, malformed, duplicate, unknown-base, cycle, and fingerprint-determinism tests. |
| Sources | Relative/glob/trust/precedence conflict tests with no-change assertions. |
| Component types | Namespace, exact version/hash, and immutable redefinition tests. |
| Projections | Pointer, duplicate target, hidden cross-app, cycle, bounds, and stable reverse-impact tests. |
| Cursors | Same binding round-trip plus tamper/stale binding failures. |
| Isolation | Opaque IDs, no application literal in generic source, and no filesystem/database access in fakes. |
| Repository | Focused tests, solution build, and diff check pass; no migration/catalog/protocol changes. |

## Verification commands

```powershell
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter ApplicationKernel
dotnet build DantesRoleplay.slnx --no-restore
git diff --check -- src/system/application-registry src/system/source-registry src/system/ecs src/system/projection-materialization src/system/catalog-navigation platform/application-kernel
```

## Completion receipt and exit gate

Evidence is recorded in [the Slice 2 receipt](receipts/APPLICATION-KERNEL-SLICE-2-RECEIPT.md).
Stop before any persistence, migration, filesystem, catalog, ECS, or protocol implementation.

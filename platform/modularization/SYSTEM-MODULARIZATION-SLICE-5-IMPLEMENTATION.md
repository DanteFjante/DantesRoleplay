# System modularization Slice 5 implementation — procedures physical component

Status: **accepted**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [System modularization dependency plan, Leaf 7](SYSTEM-MODULARIZATION-DEPENDENCY-PLAN.md#ordered-leaves)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Co-locate versioned procedure domain contracts, persistence, registration, and focused
store tests under the procedures component.  
Exclusions: Catalog file parsing/seeding, consumer verification, procedure semantics, namespaces,
APIs, migrations, MCP surface, game adapters, and local AI.  
Allowed files/areas: Procedure domain/store/focused-test source, the procedures component manifest,
architecture evidence, and this planning directory.  
Stop point: Legacy procedure domain/store/test paths are absent and focused behavior/build passes.

## Confirmed decisions

Compile-link conventions and the physical move pattern are verified by Slice 4. Procedure catalog
file/seeder code remains with the catalog component; story action verification remains a consumer.

## D&D 5e 2024 alignment

Not applicable. Procedure storage and retrieval are generic.

## External implementation reference

No Foundry implementation is relevant.

## Prerequisite evidence

- [Slice 4 receipt](SYSTEM-MODULARIZATION-SLICE-4-RECEIPT.md) verifies the move pattern.
- `ProcedureStoreTests` owns versioning/retrieval behavior.

## Runtime artifacts

None; existing types retain namespaces and assemblies.

## Authoritative state and closed input

Existing procedure contracts, versions, queries, and DbContext mappings are unchanged.

## Behavior, result, and typed effects

Physical placement only. Store ordering, version selection, content hashes, and retrieval remain
unchanged. There are no effects or transaction changes.

## Failure, replay, and rollback contract

Build and focused tests reject missing/duplicate source or behavior drift. Tests change no live data.

## Implementation sequence

Move domain files, store, and focused store tests; update manifest; run procedures/guard tests and
solution build; record receipt.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Positive | Procedure store tests pass. |
| Boundary | Seeder/file parsing and consumer verifier do not move. |
| Compatibility | Same types, namespaces, assemblies, DI, and EF mapping. |

## Verification commands

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --filter "FullyQualifiedName~ProcedureStoreTests|FullyQualifiedName~GuardTests"`
- `dotnet build DantesRoleplay.slnx --no-restore`
- `git diff --check`

## Completion receipt and exit gate

Evidence is recorded in [the Slice 5 receipt](SYSTEM-MODULARIZATION-SLICE-5-RECEIPT.md). Stop before another component move.

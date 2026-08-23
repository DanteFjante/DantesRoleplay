# System modularization Slice 6 implementation — snapshots physical component

Status: **accepted**  
Owner/roadmap: [Platform roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Modularization Leaf 7](SYSTEM-MODULARIZATION-DEPENDENCY-PLAN.md#ordered-leaves)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Co-locate snapshot package domain, persistence, hosting, and its integration tests.  
Exclusions: EF mappings/migrations, campaign evidence implementation, APIs/namespaces, catalog, MCP,
game semantics, and local AI.  
Allowed files/areas: Snapshot domain/store/test sources, snapshot manifest, and planning evidence.  
Stop point: Focused snapshot/guard tests and build pass from component-owned paths.

## Confirmed decisions

The compile-link move convention is verified by Slices 4–5. The snapshot test may exercise a game
producer as a consumer without moving that producer into the system component.

## D&D 5e 2024 alignment

Not applicable; snapshot packages are generic immutable evidence.

## External implementation reference

No Foundry reference is relevant.

## Prerequisite evidence

- [Slice 5 receipt](SYSTEM-MODULARIZATION-SLICE-5-RECEIPT.md).
- `SnapshotFeature1Tests` covers storage, immutability, registration, and campaign-consumer evidence.

## Runtime artifacts

None; existing types retain assemblies and namespaces.

## Authoritative state and closed input

Existing snapshot models/store contracts and DbContext mapping remain authoritative and unchanged.

## Behavior, result, and typed effects

Physical source placement only; package bytes, provenance, immutability, and DI remain unchanged.

## Failure, replay, and rollback contract

Build/focused tests reject missing source, duplicate inclusion, persistence, or registration drift.

## Implementation sequence

Move domain/store/integration test; mark manifest migrated; run focused guards/tests and build;
record receipt.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Positive | Snapshot integration tests pass. |
| Boundary | Campaign producer stays outside this system component. |
| Compatibility | Same assemblies, types, persistence, and registration. |

## Verification commands

- `dotnet test ... --filter "FullyQualifiedName~SnapshotFeature1Tests|FullyQualifiedName~GuardTests"`
- `dotnet build DantesRoleplay.slnx --no-restore`

## Completion receipt and exit gate

Evidence is recorded in [the Slice 6 receipt](SYSTEM-MODULARIZATION-SLICE-6-RECEIPT.md). Stop before another move.

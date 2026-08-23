# System modularization Slice 12 implementation — state physical component

Status: **accepted**  
Owner/roadmap: [Platform roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Modularization Leaf 7](SYSTEM-MODULARIZATION-DEPENDENCY-PLAN.md#ordered-leaves)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Co-locate generic entity/component/relationship/containment state, graph projection,
staged state, persistence, hosting, and focused generic tests.  
Exclusions: Knowledge, travel, journey, itinerary, small-world composition, catalog world fixtures,
game tests, DbContext/migrations, APIs/namespaces, MCP, and local AI.  
Allowed files/areas: Named generic World domain files, WorldStore/staged implementations,
WorldStore/Graph tests, manifest/evidence.  
Stop point: Generic state/graph/guard tests and build pass; game-facing World files stay quarantined.

## Confirmed decisions

The historical `World` namespace contains both the generic state kernel and game features. Only the
generic records/stores/projections move to the system state component; knowledge/travel/composition
remain game-adapter work.

## D&D 5e 2024 alignment

Not applicable; dynamic state hosting is generic.

## External implementation reference

No Foundry reference is relevant.

## Prerequisite evidence

- [Slice 11 receipt](SYSTEM-MODULARIZATION-SLICE-11-RECEIPT.md).
- `WorldStoreTests` and `GraphProjectionReaderTests` own generic state behavior; staged behavior is
  also exercised by existing consumers without moving those consumer tests.

## Runtime artifacts

None; types retain assemblies/namespaces and existing mapping.

## Authoritative state and closed input

Existing dynamic entity/component/relationship contracts, schemas, and store requests remain
unchanged.

## Behavior, result, and typed effects

Physical placement only. Validation, containment, projections, staged overlays, and persistence
semantics remain unchanged.

## Failure, replay, and rollback contract

Generic state tests retain invalid/no-change behavior; consumer tests remain available for later
full acceptance.

## Implementation sequence

Move generic domain/persistence/focused tests; leave named game features; update manifest; verify;
receipt.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Positive/negative | WorldStore and graph projection tests pass. |
| Boundary | Knowledge/travel/composition source stays outside system state. |
| Compatibility | Same assemblies, namespaces, mappings, and registration. |

## Verification commands

- `dotnet test ... --filter "FullyQualifiedName~WorldStoreTests|FullyQualifiedName~GraphProjectionReaderTests|FullyQualifiedName~GuardTests"`
- `dotnet build DantesRoleplay.slnx --no-restore`

## Completion receipt and exit gate

Evidence is recorded in [the Slice 12 receipt](SYSTEM-MODULARIZATION-SLICE-12-RECEIPT.md). Stop before another move.

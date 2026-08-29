# System modularization Slice 10 implementation — mechanics physical component

Status: **accepted**  
Owner/roadmap: [Platform roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Modularization Leaf 7](SYSTEM-MODULARIZATION-DEPENDENCY-PLAN.md#ordered-leaves)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Co-locate generic mechanic declarations/contracts, store, declared projection/composition,
Jint sandbox adapter, hosting, and focused generic tests.  
Exclusions: Catalog mechanic files/seeding, action runner, protocol adapter, game mechanics/tests,
APIs/namespaces, migrations, and local AI.  
Allowed files/areas: Mechanics domain/store/projection/composer, Jint engine, named generic tests,
RuleAccess compile link, manifest/evidence.  
Stop point: Generic mechanic/sandbox/guard tests and build pass.

## Confirmed decisions

The sandbox is infrastructure owned by mechanics but remains compiled into RuleAccess so its Jint
dependency does not enter the domain or DataAccess assemblies.

## D&D 5e 2024 alignment

Not applicable; no mechanic rule source or JavaScript moves.

## External implementation reference

No Foundry reference is relevant to physical placement.

## Prerequisite evidence

- [Slice 9 receipt](SYSTEM-MODULARIZATION-SLICE-9-RECEIPT.md).
- Mechanic store, projection, world integration, dependent composition, and sandbox tests own the
  generic behavior.

## Runtime artifacts

None; existing types retain namespaces and their current assemblies.

## Authoritative state and closed input

Existing mechanic declarations, requirements, store, projection, composition, and engine contracts
remain unchanged.

## Behavior, result, and typed effects

Physical placement only. Context materialization, sandbox limits, deterministic RNG, output/effect
validation, and composition remain unchanged.

## Failure, replay, and rollback contract

Existing sandbox, projection, composition, and integration tests retain rejection/determinism
coverage; build rejects missing/duplicate source.

## Implementation sequence

Add RuleAccess runtime compile convention; move domain/persistence/runtime/generic tests; update
manifest; verify; receipt.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Positive/negative | Store/projection/composition/sandbox suites pass. |
| Boundary | Jint remains only in RuleAccess; catalog/game sources stay outside. |
| Deterministic | Existing sandbox/composition tests retain coverage. |
| Compatibility | Same assemblies, namespaces, DI, and storage mapping. |

## Verification commands

- `dotnet test ... --filter "FullyQualifiedName~MechanicStoreTests|FullyQualifiedName~MechanicToWorldTests|FullyQualifiedName~ProjectionResolverTests|FullyQualifiedName~SandboxTests|FullyQualifiedName~E6DependentCompositionTests|FullyQualifiedName~GuardTests"`
- `dotnet build DantesRoleplay.slnx --no-restore`

## Completion receipt and exit gate

Evidence is recorded in [the Slice 10 receipt](SYSTEM-MODULARIZATION-SLICE-10-RECEIPT.md). Stop before another component move.

# System modularization Slice 4 implementation — operations/audit physical component

Status: **accepted**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [System modularization dependency plan, Leaf 7](SYSTEM-MODULARIZATION-DEPENDENCY-PLAN.md#ordered-leaves)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Prove one complete physical capability move by co-locating operations/audit domain,
persistence, hosting, and focused test source under its component directory.  
Exclusions: Namespace/API/model changes, database/migrations, runtime behavior, other components,
game adapters, catalog, MCP surface, and local AI.  
Allowed files/areas: Operations domain/store/test sources, affected project compile includes,
architecture inventory/guards, `src/system/operations-and-audit`, and this planning directory.  
Stop point: Old operation source paths are absent, linked component sources build in the same
assemblies, focused tests pass, and no behavior changes.

## Confirmed decisions

Slice 3 verified the component registration seam. This slice retains existing namespaces and
assemblies, using explicit project compile links as a migration bridge.

## D&D 5e 2024 alignment

Not applicable. Operation history and audit evidence are generic.

## External implementation reference

No Foundry implementation is relevant.

## Prerequisite evidence

- [Slice 3 receipt](SYSTEM-MODULARIZATION-SLICE-3-RECEIPT.md) proves registration is component-owned.
- `OperationLogTests` owns focused store behavior.
- Existing operation models and store have no ruleset-specific literal baseline entries.

## Runtime artifacts

None. Existing types compile from new physical paths into the same assemblies.

## Authoritative state and closed input

Unchanged existing operation request/model/store contracts and DbContext mappings.

## Behavior, result, and typed effects

File placement changes only. Namespaces, public types, service descriptors, SQL mapping, operation
ordering, and results remain byte-compatible.

## Failure, replay, and rollback contract

Duplicate compile inclusion, missing source, changed namespace, or altered store behavior fails the
build/focused tests. No runtime data is touched during the move.

## Implementation sequence

1. Add external domain/persistence/test compile conventions to the existing projects.
2. Move operation domain, store, and focused test source under the component directory.
3. Update source inventory ownership and architecture guards if paths require it.
4. Run guards, operation tests, solution build, and `git diff --check`.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Positive | Operation store tests pass from component-owned source. |
| Boundary | No operation implementation remains at a legacy source path. |
| Compatibility | Same namespaces, assemblies, DI lifetime, and EF mapping. |
| No change | No migration/catalog/MCP/local-AI changes. |

## Verification commands

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --filter "FullyQualifiedName~OperationLogTests|FullyQualifiedName~GuardTests"`
- `dotnet build DantesRoleplay.slnx --no-restore`
- `git diff --check`

## Completion receipt and exit gate

Evidence is recorded in [the Slice 4 receipt](SYSTEM-MODULARIZATION-SLICE-4-RECEIPT.md). Stop before moving a second
component.

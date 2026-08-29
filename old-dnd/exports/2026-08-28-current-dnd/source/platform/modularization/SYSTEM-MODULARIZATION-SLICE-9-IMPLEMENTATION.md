# System modularization Slice 9 implementation — effects/transactions physical component

Status: **accepted**  
Owner/roadmap: [Platform roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Modularization Leaf 7](SYSTEM-MODULARIZATION-DEPENDENCY-PLAN.md#ordered-leaves)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Co-locate typed effects, receipts, application, affected-entity simulation, registration,
and focused tests.  
Exclusions: DbContext/migrations, action orchestration, game effects/rules, APIs/namespaces, catalog,
MCP, and local AI.  
Allowed files/areas: Effects domain, EffectApplier/AffectedEntities, focused tests, manifest/evidence.  
Stop point: Effect/guard tests and build pass from component paths.

## Confirmed decisions

Effect application and affected-entity simulation share one generic transaction owner. DbContext
hosting remains in sqlite-hosting.

## D&D 5e 2024 alignment

Not applicable; the effect vocabulary is generic.

## External implementation reference

No Foundry reference is relevant.

## Prerequisite evidence

- [Slice 8 receipt](SYSTEM-MODULARIZATION-SLICE-8-RECEIPT.md).
- `EffectApplierTests` owns batch validation, no-change, and application behavior.

## Runtime artifacts

None; existing types retain namespaces/assemblies.

## Authoritative state and closed input

Existing typed effects, receipts, and world store/DbContext seams remain unchanged.

## Behavior, result, and typed effects

Physical placement only. Batch simulation, validation order, affected IDs, structural events, and
atomic application remain unchanged.

## Failure, replay, and rollback contract

Existing focused negative/no-change/rollback tests remain authoritative.

## Implementation sequence

Move domain/application/tests; update manifest; verify; receipt.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Positive/negative | EffectApplier tests pass. |
| Boundary | DbContext and action runner stay outside. |
| Rollback | Existing no-partial-state tests retain coverage. |
| Compatibility | Same types, assemblies, mapping, registration. |

## Verification commands

- `dotnet test ... --filter "FullyQualifiedName~EffectApplierTests|FullyQualifiedName~GuardTests"`
- `dotnet build DantesRoleplay.slnx --no-restore`

## Completion receipt and exit gate

Evidence is recorded in [the Slice 9 receipt](SYSTEM-MODULARIZATION-SLICE-9-RECEIPT.md). Stop before another move.

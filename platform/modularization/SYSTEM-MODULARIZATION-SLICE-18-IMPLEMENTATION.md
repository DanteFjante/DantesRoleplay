# System modularization Slice 18 implementation — Quest adapter quarantine

Status: **accepted**  
Owner/roadmap: [Platform roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Game-code eviction branch](SYSTEM-MODULARIZATION-DEPENDENCY-PLAN.md#dependency-tree)  
Ruleset alignment: **dnd2024-compatible**  
Source ID and locator: **not applicable; no D&D rule changes**  
Outcome: Move compiled Quest contracts/workflows/tests into explicit Quest game-adapter quarantine.  
Exclusions: Quest semantics/catalog mechanics, APIs/namespaces/assemblies, protocol, DbContext,
migrations, and local AI.  
Allowed files/areas: Quest domain/persistence/tests, source inventory, planning evidence.  
Stop point: Quest/guard tests and build pass at new paths.

## Confirmed decisions

Quest workflows are game-facing consumers of generic state/events/effects. Quarantine precedes any
later catalog conversion.

## D&D 5e 2024 alignment

No D&D rule meaning is implemented or changed.

## External implementation reference

No Foundry review is relevant to relocation.

## Prerequisite evidence

- [Character quarantine receipt](SYSTEM-MODULARIZATION-SLICE-17-RECEIPT.md).
- Quest feature tests own current behavior.

## Runtime artifacts

None; same types/assemblies/namespaces.

## Authoritative state and closed input

Existing Quest catalog/state/contracts remain unchanged.

## Behavior, result, and typed effects

Physical placement only; creation/lifecycle/summary effects and transactions remain unchanged.

## Failure, replay, and rollback contract

Existing Quest tests retain invalid/no-change behavior.

## Implementation sequence

Move Quest domain/persistence/tests; remove stale overrides; verify; receipt.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Positive/negative | Quest feature tests pass. |
| Boundary | Compiled Quest code lives under its adapter feature. |
| Compatibility | Same APIs, assemblies, effects, registration. |

## Verification commands

- `dotnet test ... --filter "FullyQualifiedName~QuestFeature|FullyQualifiedName~GuardTests"`
- `dotnet build DantesRoleplay.slnx --no-restore`

## Completion receipt and exit gate

Evidence is recorded in [the Slice 18 receipt](SYSTEM-MODULARIZATION-SLICE-18-RECEIPT.md). Stop before another game feature.

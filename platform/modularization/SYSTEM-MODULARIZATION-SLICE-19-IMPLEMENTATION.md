# System modularization Slice 19 implementation — Story adapter quarantine

Status: **accepted**  
Owner/roadmap: [Platform roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Game-code eviction branch](SYSTEM-MODULARIZATION-DEPENDENCY-PLAN.md#dependency-tree)  
Ruleset alignment: **ruleset-neutral consumer with game-facing semantics**  
Source ID and locator: **not applicable**  
Outcome: Move compiled Story plan contracts/orchestration/storage/tests into explicit Story
game-adapter quarantine.  
Exclusions: Story semantics, local-model implementation, worker/protocol host, APIs/namespaces,
DbContext/migrations, and catalog.  
Allowed files/areas: Story domain/persistence/tests, source inventory, planning evidence.  
Stop point: Story/guard tests and build pass at new paths.

## Confirmed decisions

Story orchestration is a game-facing consumer. It remains outside local AI; later extraction gives
it only generic local-AI contracts.

## D&D 5e 2024 alignment

No D&D rule is implemented or changed.

## External implementation reference

No Foundry review is relevant to relocation.

## Prerequisite evidence

- [Quest quarantine receipt](SYSTEM-MODULARIZATION-SLICE-18-RECEIPT.md).
- Existing Story plan suites cover persistence, policies, read/action boundaries, handoff, and
  orchestration.

## Runtime artifacts

None; same types/assemblies/namespaces.

## Authoritative state and closed input

Existing Story plan contracts/store and SQLite state remain unchanged.

## Behavior, result, and typed effects

Physical placement only; leases, steps, model verification, action boundaries, and persistence
remain unchanged.

## Failure, replay, and rollback contract

Existing Story suites retain stale/invalid/no-write/action failure coverage.

## Implementation sequence

Move Story domain/persistence/tests; remove stale overrides; verify; receipt.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Positive/negative | Story plan suites pass. |
| Boundary | Story remains a game adapter, not local-AI implementation. |
| Compatibility | Same APIs, assemblies, storage, workers, registration. |

## Verification commands

- `dotnet test ... --filter "FullyQualifiedName~StoryPlan|FullyQualifiedName~StorytellingFeature1Tests|FullyQualifiedName~GuardTests"`
- `dotnet build DantesRoleplay.slnx --no-restore`

## Completion receipt and exit gate

Evidence is recorded in [the Slice 19 receipt](SYSTEM-MODULARIZATION-SLICE-19-RECEIPT.md). Stop before another game feature.

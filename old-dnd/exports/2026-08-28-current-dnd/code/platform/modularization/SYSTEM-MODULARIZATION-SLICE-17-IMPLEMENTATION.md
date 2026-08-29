# System modularization Slice 17 implementation — Character adapter quarantine

Status: **accepted**  
Owner/roadmap: [Platform roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Game-code eviction branch](SYSTEM-MODULARIZATION-DEPENDENCY-PLAN.md#dependency-tree)  
Ruleset alignment: **dnd2024-owned, relocation only**  
Source ID and locator: Existing character slice owners retain their recorded SRD locators; no rule
meaning is used or changed here.  
Outcome: Move compiled Character contracts/resolvers/tests into explicit Character game-adapter
quarantine while preserving the exact ruleset-violation baseline for later catalog replacement.  
Exclusions: Rule/behavior rewrites, catalog mechanics, APIs/namespaces/assemblies, protocol,
DbContext/migrations, and local AI.  
Allowed files/areas: Character domain/resolvers/tests, literal inventory paths, planning evidence.  
Stop point: Character/guard tests and build pass at new paths.

## Confirmed decisions

Quarantine is transitional and does not ratify D&D rule logic in C#. Each resolver remains scheduled
for individual catalog-owned replacement.

## D&D 5e 2024 alignment

No rule meaning, source locator, calculation, eligibility, or effect changes. This slice only moves
existing files and exact literal-baseline paths.

## External implementation reference

No Foundry review is needed for behavior-neutral relocation; later rule-eviction slices must use
their existing SRD/Foundry evidence.

## Prerequisite evidence

- [Campaign quarantine receipt](SYSTEM-MODULARIZATION-SLICE-16-RECEIPT.md).
- Character feature tests cover the moved compiled workflows.

## Runtime artifacts

None; types retain namespaces/assemblies.

## Authoritative state and closed input

Existing character/catalog state and request contracts remain unchanged.

## Behavior, result, and typed effects

Physical placement only; validations, plans, effects, and participation checks remain unchanged.

## Failure, replay, and rollback contract

Existing Character tests and exact literal ratchet retain coverage.

## Implementation sequence

Move domain/resolvers/tests; update literal paths and stale overrides; verify; receipt.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Positive/negative | Character feature suites pass. |
| Boundary | All compiled Character code lives in quarantine. |
| Literal ratchet | Same occurrences at new paths, no growth. |
| Compatibility | Same APIs, assemblies, effects, and registration. |

## Verification commands

- `dotnet test ... --filter "FullyQualifiedName~CharacterFeature|FullyQualifiedName~CharacterPlaytestInterfaceTests|FullyQualifiedName~GuardTests"`
- `dotnet build DantesRoleplay.slnx --no-restore`

## Completion receipt and exit gate

Evidence is recorded in [the Slice 17 receipt](SYSTEM-MODULARIZATION-SLICE-17-RECEIPT.md). Stop before another game feature or rule eviction.

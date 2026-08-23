# System modularization Slice 22 implementation — Local routing adapter quarantine

Status: **accepted**  
Owner/roadmap: [Platform roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Game-code eviction branch](SYSTEM-MODULARIZATION-DEPENDENCY-PLAN.md#dependency-tree)  
Ruleset alignment: **ruleset-neutral infrastructure consumer with game-action semantics**  
Source ID and locator: **not applicable**  
Outcome: Move the local model-assisted game-action route proposal contract, coordinator, and focused
tests into a game adapter quarantine.  
Exclusions: Generic local-AI contracts/providers, behavior, APIs/namespaces/assemblies,
registrations, Information work, and other Action capability code.  
Allowed files/areas: `LocalActionRouting`, `LocalRouteProposalCoordinator`, its focused test,
inventory/evidence.  
Stop point: Local-route/guard tests and build pass.

## Confirmed decisions

The model provider is generic infrastructure. Proposing game procedure/mechanic/action routes from
campaign context is a consumer adapter and must depend toward local AI, never the reverse.

## D&D 5e 2024 alignment

No D&D rule is implemented or changed.

## External implementation reference

No Foundry review is relevant.

## Prerequisite evidence

- [Travel quarantine receipt](SYSTEM-MODULARIZATION-SLICE-21-RECEIPT.md).
- Existing local-route suite covers service registration, validated output, and fallback.

## Runtime artifacts

None; same types, assemblies, namespaces, registration, and provider interaction.

## Authoritative state and closed input

Existing proposal request, catalog records, world facts, and JSON-schema output remain unchanged.

## Behavior, result, and typed effects

Physical placement only; the coordinator remains read-only and produces the same bounded proposal.

## Failure, replay, and rollback contract

Existing invalid-input, unavailable-provider, malformed-output, and deterministic-fallback behavior
is unchanged.

## Implementation sequence

Move contract/coordinator/test; remove stale overrides; verify; receipt.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Positive/negative | Local route suite passes. |
| Boundary | Game routing consumes generic model contracts outside system roots. |
| Compatibility | Same APIs, assemblies, registration, and fallback. |

## Verification commands

- `dotnet test ... --filter "FullyQualifiedName~LocalRouteProposalCoordinatorTests|FullyQualifiedName~GuardTests"`
- `dotnet build DantesRoleplay.slnx --no-restore`

## Completion receipt and exit gate

Record Slice 22 receipt and stop before extracting local AI.

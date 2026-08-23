# System modularization Slice 2 implementation — component ownership scaffold

Status: **accepted**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [System modularization dependency plan, Leaf 2](SYSTEM-MODULARIZATION-DEPENDENCY-PLAN.md#lowest-ready-leaf)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Materialize the confirmed component directory convention as machine-readable ownership
manifests and enforce its dependency direction before implementations move.  
Exclusions: Production source moves, namespaces, runtime/DI behavior, local-AI implementation,
catalog, migrations, database, and MCP surface.  
Allowed files/areas: `src/**/component.json`, `DantesRoleplay.Tests/GuardTests.cs`, this planning
directory, and the Slice 2 receipt.  
Stop point: Every target directory has one valid manifest and its dependency graph passes focused
guards; no production source moves.

## Confirmed decisions

The user's 2026-08-23 instruction confirms the dependency plan's component convention and direction.
The scaffold uses capability directories under `src/system`, an application composition directory,
and a separate game-adapter tree. Directory names are development ownership labels, not runtime IDs.

## D&D 5e 2024 alignment

Not applicable. No rule is implemented or moved.

## External implementation reference

No Foundry implementation is relevant to an ownership-manifest slice.

## Prerequisite evidence

- [Slice 1 receipt](SYSTEM-MODULARIZATION-SLICE-1-RECEIPT.md) proves the current source boundary is
  classified and ruleset coupling cannot grow unnoticed.
- The dependency plan defines the allowed direction: application -> optional game adapters and
  system hosting; game adapters -> public system contracts; system -> system/building blocks only;
  local AI -> no game adapter.

## Runtime artifacts

None. Component manifests are architecture metadata consumed by tests only.

## Authoritative state and closed input

Every immediate capability directory below `src/system`, `src/applications`, and
`src/game-adapters/dantes-roleplay` contains exactly one `component.json`. A manifest has closed
fields: name, classification, status, owns, and mayDependOn.

## Behavior, result, and typed effects

The guard rejects duplicate names, missing/extra fields, unknown classifications/statuses,
undeclared dependencies, system-to-game dependencies, application cycles, or a local-AI dependency
on any game adapter. There is no runtime result, effect, or transaction.

## Failure, replay, and rollback contract

Malformed or inconsistent manifests fail deterministically with the component path. Tests do not
modify manifests or runtime state.

## Implementation sequence

1. Add manifests for the target system, application, and game-adapter capability directories.
2. Extend `GuardTests` with closed-manifest and acyclic dependency checks.
3. Run focused guards, solution build, and `git diff --check`.
4. Record the receipt and stop before production code moves.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Positive | Every target capability has one closed manifest. |
| Negative | Unknown dependency/classification/status and graph cycles are rejected. |
| Boundary | No system component depends on a game adapter. |
| Local-AI boundary | Its manifest depends on building blocks only. |
| Compatibility | Production build output is unchanged. |

## Verification commands

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --filter FullyQualifiedName~GuardTests`
- `dotnet build DantesRoleplay.slnx --no-restore`
- `git diff --check`

## Completion receipt and exit gate

Evidence is recorded in [the Slice 2 receipt](SYSTEM-MODULARIZATION-SLICE-2-RECEIPT.md). Mark the dependency leaf verified,
and stop before moving implementation files or splitting service registration.

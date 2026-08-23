# System modularization Slice 1 implementation — architecture boundary ratchet

Status: **accepted**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [System modularization dependency plan, Leaf 1](SYSTEM-MODULARIZATION-DEPENDENCY-PLAN.md#lowest-ready-leaf)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Classify every production C# source area and make existing compiled ruleset coupling a
non-increasing, exact legacy baseline before source files move.  
Exclusions: Directory moves, namespaces, runtime behavior, DI, catalog, migrations, database,
configuration, MCP kinds, and local-AI implementation.  
Allowed files/areas: This document, one machine-readable inventory below this directory,
`DantesRoleplay.Tests/GuardTests.cs`, the dependency plan status, and the completion receipt.  
Stop point: Focused guard tests and solution build pass; no production source changes.

## Confirmed decisions

The user confirmed implementation of the modularization plan on 2026-08-23. This slice exercises
only the plan's already-ready, non-semantic Leaf 1. It introduces no permanent runtime identifier or
public contract.

## D&D 5e 2024 alignment

Not applicable. The slice detects ruleset coupling but implements no rule.

## External implementation reference

No Foundry implementation is relevant to a source-classification and architecture-test slice.

## Prerequisite evidence

- `ARCHITECTURE.md` prohibits ruleset IDs, formulas, and outcome logic in C#.
- `GuardTests.The_kernel_contains_no_game_vocabulary` already owns source-level architecture
  enforcement, but deliberately strips comments and strings.
- Current production C# contains exact `dnd2024` and `source.dnd2024` literals in character and
  campaign files, so a truthful ratchet must baseline rather than claim immediate compliance.
- The dependency plan records the current project and composition-root conflicts.

## Runtime artifacts

None. The inventory and tests are development evidence only.

## Authoritative state and closed input

The repository filesystem at test time is the only input. Production `.cs` files exclude `bin`,
`obj`, generated designer files, and generated migrations from literal analysis. The inventory owns
root category defaults, path overrides, and exact legacy literal occurrences.

## Behavior, result, and typed effects

- Every production source file under the named production roots resolves to one category.
- Every exact compiled ruleset literal occurrence is listed by relative path and count.
- A new source root, unclassified file, new literal, moved literal, or changed count fails.
- Removing a legacy occurrence requires reducing the inventory in the same change.
- There are no typed effects, persistence changes, transactions, or runtime results.

## Failure, replay, and rollback contract

Malformed inventory, duplicate paths, unknown categories, missing roots, unclassified files, stale
overrides, and literal drift fail with the affected path. Re-running against identical source is
deterministic. A failing test changes no repository or runtime state.

## Implementation sequence

1. Add the machine-readable inventory.
2. Extend the existing architecture guard to validate classification and exact literal baseline.
3. Run focused guards, build the solution, and run `git diff --check`.
4. Record a completion receipt and mark Leaf 1 verified.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Positive | Every current production C# file receives a declared category. |
| Negative | Synthetic/unlisted ruleset literal or stale baseline would fail exact comparison. |
| Boundary | Tests inspect production roots only and do not classify tests as runtime code. |
| Deterministic | Sorted relative paths and exact counts produce stable results. |
| Compatibility | Existing vocabulary, three-verb, and dispatcher guard tests remain unchanged. |
| No change | No production source, catalog, database, migration, or MCP file changes. |

## Verification commands

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --filter FullyQualifiedName~GuardTests`
- `dotnet build DantesRoleplay.slnx --no-restore`
- `git diff --check`

## Completion receipt and exit gate

Evidence is recorded in [the Slice 1 receipt](SYSTEM-MODULARIZATION-SLICE-1-RECEIPT.md). Update the dependency-plan node once,
and stop before scaffolding or moving source.

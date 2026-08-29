# System modularization Slice 3 implementation — per-component composition

Status: **accepted**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [System modularization dependency plan, component composition](SYSTEM-MODULARIZATION-DEPENDENCY-PLAN.md#dependency-tree)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Give each currently implemented capability one internal service-registration entry point
under its ownership directory while retaining the existing aggregate public registration facade.  
Exclusions: Moving implementations, changing namespaces/public signatures, changing service
lifetimes or availability, implementing local AI, catalog changes, migrations, database changes,
and MCP surface changes.  
Allowed files/areas: Component `hosting/*.cs` files, the DataAccess project file and registration
facade, architecture inventory/guards, this planning directory, and focused registration tests.  
Stop point: Existing aggregate registration resolves the same services and focused/full build
evidence passes; implementations remain in their current files.

## Confirmed decisions

The component dependency graph and directory convention are verified by Slice 2. Component entry
points are internal during migration. The aggregate `AddDantesRoleplayDataAccess` and authenticated
registration methods retain their current public signatures.

## D&D 5e 2024 alignment

Not applicable. Registration placement carries no rule meaning.

## External implementation reference

No Foundry implementation is relevant to dependency injection composition.

## Prerequisite evidence

- [Slice 2 receipt](SYSTEM-MODULARIZATION-SLICE-2-RECEIPT.md) verifies manifests and allowed
  dependencies.
- Existing local route, snapshot, development audience, and protocol tests exercise service
  availability through the aggregate facade.
- `DataAccessServiceCollectionExtensions.cs` currently owns all unrelated registrations in one
  method, providing the exact move set.

## Runtime artifacts

No new runtime capability, ID, schema, table, or operation. Internal registration classes are the
only new production types.

## Authoritative state and closed input

The existing connection string, database provider, and knowledge retrieval options remain the
closed input to the public aggregate facade. It validates them and registers the DbContext before
delegating to component entry points.

## Behavior, result, and typed effects

Every existing descriptor is registered once with the same lifetime and implementation. SQLite-only
retrieval/model descriptors remain conditional exactly as before, temporarily owned by the game
adapter composition until local AI is extracted. Authenticated game services remain opt-in.

## Failure, replay, and rollback contract

Existing invalid-provider/options failures remain unchanged. Missing or duplicate descriptors fail
focused resolution/descriptor tests. Registration performs no I/O and changes no persistent state.

## Implementation sequence

1. Add component-local internal registration entry points.
2. Include their source through the DataAccess project and delegate from the aggregate facade.
3. Extend architecture inventory/guards for production source under `src`.
4. Run component guards, focused DI consumers, solution build, and full suite if focused evidence
   reveals no mismatch.
5. Record receipt and stop before moving implementations.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Positive | Existing aggregate facade resolves system and game-adapter services. |
| Conditional | SQLite-only retrieval descriptors remain present only for SQLite. |
| Boundary | Registration source resides in the owning component directory. |
| Compatibility | Public facade signatures and descriptor lifetimes remain unchanged. |
| No change | No catalog/database/MCP behavior or local-AI implementation changes. |

## Verification commands

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --filter "FullyQualifiedName~GuardTests|FullyQualifiedName~LocalRouteProposalCoordinatorTests|FullyQualifiedName~SnapshotFeature1Tests|FullyQualifiedName~DevelopmentKnowledgeAudienceTests"`
- `dotnet build DantesRoleplay.slnx --no-restore`
- `git diff --check`

## Completion receipt and exit gate

Evidence is recorded in [the Slice 3 receipt](SYSTEM-MODULARIZATION-SLICE-3-RECEIPT.md). Mark composition verified, and stop
before moving implementation files.

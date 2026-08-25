# System modularization Slice 24 implementation — stale game-adapter unlink

Status: **accepted**  
Owner/roadmap: [Platform roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Game-code eviction](SYSTEM-MODULARIZATION-DEPENDENCY-PLAN.md#dependency-tree), protocol/composition links  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable; no game rule is implemented, changed, or replaced**  
Outcome: Restore a buildable generic host by removing live C# references, registrations, mappings,
and dispatch routes to game-adapter types that are no longer compiled. Keep the legacy source files
and migration history on disk for a later, separately confirmed replacement/removal slice.  
Exclusions: Game rule replacement, catalog changes, database migration/deletion, application
registration, protocol redesign, creation of a new public operation kind, and deletion of legacy
source or test files.  
Allowed files/areas: Generic composition and protocol source under `DantesRoleplay.DataAccess`,
`DantesRoleplay.MCPServer`, and `src/system/actions`; project compile exclusions for retained
game-adapter files/tests; the active Slice 2 status; this document and its receipt.  
Stop point: The generic host builds without direct Campaign, Character, Quest, Story, Knowledge,
Journey, or Itinerary references; write a receipt and stop before catalog-owned feature replacement.

## Confirmed decisions

- The user explicitly authorized removing the stale live references, while retaining the files.
- Existing database migrations are historical evidence and are not edited or applied in this slice.
- The generic MCP surface continues to expose only generic system capabilities; former game routes
  are no longer advertised, dispatched, registered, or compiled into the generic host.

## Prerequisite evidence

- [System modularization dependency plan](SYSTEM-MODULARIZATION-DEPENDENCY-PLAN.md) identifies
  compiled game workflows/protocol adapters as the next conflicting leaf.
- The current source tree has no compiled `src/game-adapters` implementation, while DataAccess,
  MCP, and ActionRunner still import or implement its removed types.
- Application-kernel Slice 2 has a passing core build but cannot run its shared test project until
  those stale references are detached.

## Runtime artifacts

No catalog record, schema, migration, game identifier, or public operation kind is created.
The existing `orient`, `query`, and `commit` verbs remain; their generic kinds are the only ones
registered by this host after the unlink.

## Authoritative state and closed input

No request or state shape changes. The only runtime change is composition: generic stores and
handlers are selected; unavailable game-adapter implementation files are excluded from the live
build and never resolved from dependency injection.

## Behavior, result, and typed effects

- Data access maps and registers generic component records only.
- Action execution owns its generic transaction and no longer accepts a Story-specific participant.
- MCP query/commit dispatch handles the remaining generic kinds only; a former game kind is an
  ordinary unknown-kind rejection with no state change.
- Old game-specific source and tests remain physically present but are excluded from the relevant
  generic project compilation until an application adapter owns them.

## Failure, replay, and rollback contract

Unknown/removed game kinds produce the existing deterministic `UNKNOWN_KIND` envelope. Generic
query/commit validation and transactions remain unchanged. This slice never mutates a database,
does not generate a migration, and does not delete a file.

## Implementation sequence

1. Remove stale game-adapter imports, EF model wiring, service registrations, worker wiring, and
   Story participant coupling.
2. Retain but exclude direct game-only MCP helpers and tests from generic project compilation.
3. Reduce the generic protocol catalog and dispatchers to matching generic kinds.
4. Build the solution, run focused generic/application-kernel tests, and record the remaining
   intentional legacy files in the receipt.

## Acceptance matrix

| Area | Required proof |
| --- | --- |
| Composition | No generic production source imports Campaign, Character, Quest, Story, Knowledge, Journey, or Itinerary namespaces. |
| EF | DbContext has no current game-plan entity mapping; migrations remain untouched. |
| Protocol | Advertised query/commit kinds exactly match the generic dispatchers. |
| Compatibility | `orient`, `query`, and `commit` still register and generic kinds execute. |
| Safety | A removed game kind rejects without a write; generic action has no Story callback. |
| Repository | Solution build, focused generic tests, and diff check pass. |

## Verification commands

```powershell
dotnet build DantesRoleplay.slnx --no-restore
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter ApplicationKernel
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter "FullyQualifiedName~GuardTests"
git diff --check
```

## Completion receipt and exit gate

Evidence is recorded in [the Slice 24 receipt](SYSTEM-MODULARIZATION-SLICE-24-RECEIPT.md).
Stop before migrating/deleting legacy records or implementing an application adapter/catalog
replacement.

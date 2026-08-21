# Feature 23 Slice 1 receipt — bounded containment action projection

Status: **Verified**
Completed: 2026-08-20

## Outcome

Implemented the generic, opt-in action projection needed before inventory rules can safely inspect nested containment. The kernel remains game-neutral: it does not know about items, weight, capacity, or D&D.

`RoleRequirement` now supports:

- `contentsDepth`: optional depth from 1 through 4 when `includeContents` is true; omission preserves the legacy direct-child view.
- `contentComponentIds`: a distinct, non-empty, maximum-12 allow-list of components visible on contained nodes.

Nested nodes expose only id, name, slot, declared descendant components, and declared nested contents. They never expose root components, ancestry, relationships, or undeclared components.

## Safety and compatibility

- Recursive/content-component requests fail closed at 100 projected contained nodes or on corrupt containment cycles. No partial projection reaches JavaScript.
- Traversal runs as bounded shared set queries by depth; descendant component data is loaded in one allow-listed set query.
- Existing `includeContents: true` with neither new field retains its identity-only direct-child JavaScript shape; the new shared 100-node safety limit applies uniformly.
- `MechanicStore` rejects invalid combinations before a mechanic revision is written, and component validation now includes declared descendant component ids.

## Changed artifacts

- `DantesRoleplay/Mechanics/MechanicModels.cs`
- `DantesRoleplay.DataAccess/ProjectionResolver.cs`
- `DantesRoleplay.DataAccess/MechanicStore.cs`
- `DantesRoleplay.Tests/ProjectionResolverTests.cs`
- `DantesRoleplay.Tests/MechanicStoreTests.cs`
- `catalog/procedures/mechanics/procedure.mechanic.projection.md`

## Verification

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter "FullyQualifiedName~ProjectionResolverTests|FullyQualifiedName~MechanicStoreTests"` — passed, 52 tests.
- `roleplay validate catalog` — valid: 171 records. It reported 31 pre-existing near-duplicate warnings and did not touch live data.
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore` — passed, 460 tests.
- `git diff --check` — passed for the Slice 1 files.

No D&D component, mechanic, event, fixture, migration, or persistent catalog import was created. This receipt intentionally stops before Slice 2's definition identity/versioning boundary.

# Feature 23 Slice 10 receipt — fixed item activities and grants

Implemented the first closed item-activity seam. `dnd2024.item-activity` attaches immutable
`consume-and-grant-item` descriptors to a definition. The one activity mechanic consumes the
descriptor-stated quantity from a compatible physical stack and atomically creates, references,
and places exactly the descriptor-stated ordinary item in the source stack's direct container.

Callers provide only an activity id and a new entity id. They cannot choose the granted definition,
name, slot, container, component payload, quantity, or effect list. The slice adds no source
fixture and does not claim an SRD item has a use action.

## New permanent vocabulary

- `dnd2024.item-activity`
- `procedure.mechanic.dnd2024.item-activity`
- `mechanic.dnd2024.item-activity.use`

## Verification

- `dotnet test DantesRoleplay.Tests\DantesRoleplay.Tests.csproj --no-restore --filter FullyQualifiedName~CatalogFeature23Slice10Tests`
  passed **3 tests**.
- `roleplay validate catalog` validated **226 records** with **4 advisory near-duplicate warnings**
  and no validation errors. No live data was touched.
- The focused tests prove partial and final source-stack consumption, fixed descriptor-controlled
  placement, grant-definition mismatch refusal, direct-content refusal, rollback, and schema
  rejection of an arbitrary effect list.

Slice 11 remains the final read-model, fixture, and feature-acceptance boundary.

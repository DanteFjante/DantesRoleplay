# Feature 23 Slice 11 receipt — bounded inventory read model and acceptance

Implemented `mechanic.dnd2024.inventory.read`, a deterministic, effect-free physical-item
inspection below an explicit custody root. It returns visible item identity, definition kind,
quantity, equipment state, slot, depth, and separately discloses visible non-item contents.

The view is explicitly bounded to four containment levels and always reports
`mayOmitDeeperContents: true`; it never claims an exhaustive inventory, creates an inventory
array, infers ownership, or exposes a mutable derived total. Burden/carrying, currency value, and
equipment remain their existing narrow consumer seams.

## New permanent vocabulary

- `procedure.mechanic.dnd2024.inventory-read`
- `mechanic.dnd2024.inventory.read`

## Verification

- `CatalogFeature23Slice11Tests` passed: the same custody fixture proves nested physical
  inspection, explicit boundedness, non-item disclosure, held-state inspection, currency value,
  and carrying-capacity consumption.
- `dotnet test DantesRoleplay.Tests\DantesRoleplay.Tests.csproj --no-restore --filter FullyQualifiedName~CatalogFeature23`
  passed **19 tests**.
- `roleplay validate catalog` validated **228 records** with **0 warnings** and no live data
  touched.
- `dotnet test DantesRoleplay.Tests\DantesRoleplay.Tests.csproj --no-restore` passed **498 tests**.
- `git diff --check` passed for the Slice 11 implementation files.

Feature 23 is accepted. No persistent/live catalog import was performed.

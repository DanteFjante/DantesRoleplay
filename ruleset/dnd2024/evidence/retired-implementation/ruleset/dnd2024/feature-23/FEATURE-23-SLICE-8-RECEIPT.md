# Feature 23 Slice 8 receipt — held and worn equipment state

Implemented the closed `dnd2024.equipment-state` component with `held`, `worn`, and explicit
`unequipped` states. Immutable item definitions can now declare the modes they permit; the seeded
dagger permits `held` and the backpack permits `worn`.

`mechanic.dnd2024.item.equip` requires direct containment by the selected holder, a separate
non-stack item, and definition eligibility. `mechanic.dnd2024.item.unequip` preserves containment
and records `unequipped`. The transfer mechanic rejects a held or worn item until it is unequipped.
The read mechanic returns only the item definition, state, and direct containment context.

## Verification

- `dotnet test DantesRoleplay.Tests\\DantesRoleplay.Tests.csproj --no-restore --filter "FullyQualifiedName~CatalogFeature23" -v minimal`
  passed **13 tests** across Feature 23 slices.
- `roleplay validate catalog` validated **219 records** with **0 warnings**.
- `CatalogFeature23Slice8Tests` covers direct eligible holding and wearing, explicit unequip,
  transfer refusal while equipped, nested/wrong-holder/wrong-mode/stack refusal, and read-only
  state projection.

No persistent/live catalog import was performed.

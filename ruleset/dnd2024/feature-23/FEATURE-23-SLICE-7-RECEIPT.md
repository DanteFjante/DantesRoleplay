# Feature 23 Slice 7 receipt — ordinary transfer and capacity admission

Implemented the normal physical-item transfer boundary. `mechanic.dnd2024.item.transfer` now
requires the named item, its direct source, and its destination. It validates the source custody,
visible self/descendant movement, a container's permitted kinds, direct exact weight limit, and
item-count limit (including every unit represented by a fungible stack) before emitting its one
`containment.move` effect.

The earlier create/place and direct-move mechanics are retained only as explicitly named
administrative fixture/bootstrap helpers. Normal player intents (`move`, `stow`, `take`, `pick up`,
`retrieve`, and `give`) resolve to the transfer mechanic, so they cannot bypass admission.

## Verification

- `dotnet test DantesRoleplay.Tests\\DantesRoleplay.Tests.csproj --no-restore --filter "FullyQualifiedName~CatalogFeature23" -v minimal`
  passed **11 tests** across Feature 23 slices.
- `roleplay validate catalog` validated **210 records** with **0 warnings**.
- `CatalogFeature23Slice7Tests` verifies normal routing, direct and nested custody, root transfer,
  whole-stack preservation plus split/merge interaction, source mismatch, self-descendant refusal,
  permitted-kind refusal, item-count refusal, and no mutation after every rejection.

No persistent/live catalog import was performed.

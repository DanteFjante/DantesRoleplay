# Feature 23 Slice 9 receipt — physical currency value

Implemented a bounded, effect-free currency reader. It walks the selected custody root's declared
contents, resolves each physical item instance to its exact immutable definition, and derives the
positive stack quantities into a copper-piece total, coin count, and deterministic denomination
breakdown.

`mechanic.dnd2024.currency-value.read` creates no wallet, balance, exchange, spending route,
transfer, price, or change-making behavior. The five source-cited immutable denomination
definitions remain the sole conversion authority, and an unquantified/corrupt currency instance
fails closed instead of producing a false total.

## New permanent vocabulary

- `procedure.mechanic.dnd2024.currency-value`
- `mechanic.dnd2024.currency-value.read`

## Verification

- `dotnet test DantesRoleplay.Tests\DantesRoleplay.Tests.csproj --no-restore --filter FullyQualifiedName~CatalogFeature23Slice9Tests`
  passed **2 tests**.
- `roleplay validate catalog` validated **222 records** with **5 advisory near-duplicate warnings**
  and no validation errors. No live data was touched.
- The focused tests prove an empty custody root has no implicit wallet, mixed nested physical
  stacks derive 284 copper pieces exactly, and a currency instance lacking its mandatory quantity
  is rejected without mutation.

Slice 10 remains the next planned boundary for typed item activities and effect grants.

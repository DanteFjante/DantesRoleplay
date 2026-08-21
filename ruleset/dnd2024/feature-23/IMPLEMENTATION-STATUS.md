# Feature 23 implementation status

Updated: 2026-08-20

## Verified slices

### Slice 1 — bounded containment projection

Implemented generic, declared nested-content projection with bounded depth, component allow-lists,
cycle/overflow refusal, and compatibility for the legacy direct identity-only content shape.

### Slice 2 — immutable item definitions

Implemented `dnd2024.item-definition` and source-backed v1 definitions for backpack, pouch,
quiver, hempen rope, dagger, and five currency denominations.

### Slice 3 — physical instances and direct custody

Implemented `dnd2024.item-instance` plus record, create/place, move, and read mechanics. An
instance stores only an immutable definition reference; containment is its location/custody.

### Slice 4 — quantities and stacks

Implemented `dnd2024.item-quantity` and fungible stack create/record, split, merge, and consume
mechanics. Quantity is positive; a final consume deletes the stack rather than storing zero.

### Slice 5 — measures and recursive burden

Implemented exact rational recursive burden reading. Added generic declared component references
to the mechanics projection so an item instance can resolve its immutable definition without
copying static data onto campaign entities.

### Slice 6 — creature Size and carrying capacity

Implemented `dnd2024.creature-size`, one-time Size recording, and read-only SRD carrying,
drag/lift/push derivation for Tiny through Gargantuan. No Encumbered speed state was added.

### Slice 8 — held and worn equipment state

Implemented `dnd2024.equipment-state` with the closed `held`, `worn`, and explicit
`unequipped` states. `mechanic.dnd2024.item.equip` permits only an item directly contained by its
named holder and only a mode declared on its immutable definition. Fungible stacks and nested
items fail closed. `mechanic.dnd2024.item.unequip` changes no containment; normal transfer refuses
an equipped item until it is explicitly unequipped. `mechanic.dnd2024.item.equipment.read` is the
narrow effect-free consumer seam.

No slot grid, hand count, armor calculation, weapon attack integration, ammunition behavior, or
action/resource cost was added.

### Slice 9 — physical currency value

Implemented `mechanic.dnd2024.currency-value.read`, an effect-free bounded inspection of physical
currency stacks. It derives exact copper value, coin count, and a denomination breakdown from
their immutable definitions and positive quantities. It creates no wallet, balance, exchange,
spending, or market behavior; an unquantified currency instance fails closed.

### Slice 10 — fixed item activities and grants

Implemented the closed `dnd2024.item-activity` descriptor and
`mechanic.dnd2024.item-activity.use`. The supported activity consumes its fixed stack quantity
and creates/places one exact descriptor-declared ordinary item atomically. Callers cannot author
effects, alter the grant target or definition, or supply a payload; source-backed gameplay
activities remain future content/consumer work.

### Slice 11 — bounded inventory read model and acceptance

Implemented `mechanic.dnd2024.inventory.read`, which returns the bounded physical item tree as a
flat deterministic inspection with visible non-item contents disclosed separately. The result
always marks possible omission beyond depth four instead of claiming a complete inventory. It
remains read-only and leaves carrying, currency, and equipment to their verified consumer seams.

## Verified slice

### Slice 7 — transfer and ordinary capacity admission

Implemented `procedure.mechanic.dnd2024.item-transfer` and
`mechanic.dnd2024.item.transfer`. A transfer names the physical item, its actual direct source,
and its destination. It validates direct custody, visible descendant cycles, permitted item kinds,
exact direct weight capacity, and item-count capacity before returning its one containment-move
effect. A non-item destination remains an unconstrained custody root in this slice; carrying and
ancestor-capacity admission have not been inferred.

The old create/place and move mechanics are now explicitly administrative fixture/bootstrap
helpers, not player-facing routes. Whole-stack transfer preserves count and stack key; partial
transfer remains deliberately deferred to a later quantity extension.

## Verification performed

- All 19 focused Feature 23 tests pass.
- Catalog validation passes with zero warnings and no errors.
- The complete repository test project passes: 498 tests.
- No persistent/live catalog import was performed.

## Key artifacts

- `FEATURE-23-DEPENDENCY-PLAN.md`
- `FEATURE-23-SLICE-1-RECEIPT.md` through `FEATURE-23-SLICE-11-RECEIPT.md`
- `catalog/mechanics/ruleset/dnd2024/core/data/item-transfer/`
- `catalog/mechanics/ruleset/dnd2024/core/data/equipment-state/`
- `catalog/mechanics/ruleset/dnd2024/core/data/currency-value/`
- `catalog/mechanics/ruleset/dnd2024/core/data/item-activity/`
- `catalog/mechanics/ruleset/dnd2024/core/data/inventory-read/`
- `DantesRoleplay/Mechanics/MechanicModels.cs`
- `DantesRoleplay.DataAccess/ProjectionResolver.cs`
- `DantesRoleplay.RuleAccess/JintMechanicEngine.cs`

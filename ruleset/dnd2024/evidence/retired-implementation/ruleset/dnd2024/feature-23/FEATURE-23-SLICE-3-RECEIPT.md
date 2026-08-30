# Feature 23 Slice 3 receipt — physical item instances and direct custody

Completed: 2026-08-20

## Outcome

Implemented campaign-local physical item identity and direct custody without an inventory array or owner field. `dnd2024.item-instance` contains exactly one immutable `definitionId`; the definition entity supplies static facts, and ordinary containment supplies the item’s only location.

## New permanent vocabulary

- `procedure.mechanic.dnd2024.item-instance`
- `dnd2024.item-instance`
- `mechanic.dnd2024.item-instance.record`
- `mechanic.dnd2024.item-instance.create-and-place`
- `mechanic.dnd2024.item-instance.move`
- `mechanic.dnd2024.item-instance.read`

The record mechanic attaches an initial reference once. The create-and-place mechanic produces the atomic effect sequence entity.create → component.add → containment.move. Move reuses the existing single-parent, cycle-safe containment writer. Read is effect-free and reports the definition id and direct containment context.

## Verification

- `CatalogFeature23Slice3Tests` passed: 2 action-runner tests cover create, record, read, move, duplicate record/create rejection, definition-as-instance rejection, invalid id rejection, and no partial state after rejection.
- `roleplay validate catalog` passed: 203 records, no warnings, and no live data touched.
- `git diff --check` passed for the Slice 3 files.

No quantity, stack, capacity, nesting admission, equipped state, currency transaction, ownership, or derived burden mechanic was introduced. Slice 4 remains unstarted.

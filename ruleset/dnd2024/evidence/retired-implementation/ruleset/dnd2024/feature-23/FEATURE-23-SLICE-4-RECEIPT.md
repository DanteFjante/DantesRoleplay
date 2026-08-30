# Feature 23 Slice 4 receipt — fungible quantities and stack algebra

Completed: 2026-08-20

## Outcome

Implemented a positive-count physical stack for definitions whose immutable data declares
`stackPolicy: "fungible"`. `dnd2024.item-quantity` stores only the count and a mechanic-derived
compatibility key. In this slice, the key exactly equals the referenced immutable definition id;
callers never select it.

The slice adds atomic create-and-place, record, split, merge, and consume mechanics. A split
creates a second same-definition stack and reduces its source without permitting either count to
be zero. A merge names both source and retained target explicitly, requires the same direct
container, adds the source count to the target, then deletes the source. Final consumption deletes
the stack entity. A stack with direct contents cannot be split, merged, or wholly consumed, so a
stack deletion cannot hide separately live contained entities.

## New permanent vocabulary

- `procedure.mechanic.dnd2024.item-quantity`
- `dnd2024.item-quantity`
- `mechanic.dnd2024.item-stack.create-and-place`
- `mechanic.dnd2024.item-stack.record`
- `mechanic.dnd2024.item-stack.split`
- `mechanic.dnd2024.item-stack.merge`
- `mechanic.dnd2024.item-stack.consume`

## Verification

- `CatalogFeature23Slice4Tests` passed: 3 tests cover schema rejection, create/record, split,
  deterministic merge, partial/final consumption, count conservation, incompatible-definition and
  direct-container refusal, direct-content refusal, and rejected-operation rollback.
- The Feature 23 focused group passed: 7 tests across Slices 2–4.
- `roleplay validate catalog` passed: 200 records, 42 mechanics, 60 procedures, 42 components,
  9 event types, 2 subscriptions, and 45 entities; 0 warnings; no live data touched.
- `git diff --check` passed for Slice 4 files.

No capacity admission, recursive burden calculation, carry limit, inter-container transfer,
equipment state, currency value transaction, ownership, magic-item state, or inventory read model
was introduced. Slice 5 remains the next planned boundary.

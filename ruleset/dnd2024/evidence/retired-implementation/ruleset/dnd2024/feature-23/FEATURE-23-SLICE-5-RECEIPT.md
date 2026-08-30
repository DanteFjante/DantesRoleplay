# Feature 23 Slice 5 receipt — physical measures and recursive burden

Completed: 2026-08-20

## Outcome

Implemented the read-only `mechanic.dnd2024.item-burden.read` capability. It derives an exact
rational-pound total from an explicit root's bounded containment subtree, using immutable
definition mass and mutable item quantity. A separate item counts as one; a fungible item requires
a valid positive quantity with a stack key equal to its exact definition id.

The slice adds a generic, declared component-reference projection seam. A requirement now names a
component's entity-id field and the exact components visible on its target. The resolver fetches
only those targets and exposes them as `ctx.references`; it does not grant a mechanic arbitrary
world lookup. The burden rule uses that seam to resolve `item-instance.definitionId` to the single
immutable `item-definition` component without copying static facts onto campaign entities.

## Verification

- `CatalogFeature23Slice5Tests` passed: nested Ari → backpack → pouch → 50 copper pieces resolves
  to exactly 7/1 lb; the action has no effects; an unmarked contained entity is rejected rather
  than treated as zero mass.
- Feature 23 focused group passed: 8 tests across Slices 2–5.
- `roleplay validate catalog` passed: 202 records with no errors and no live data touched. It
  reported two existing-style near-duplicate advisory warnings for the new burden procedure and
  mechanic; they do not indicate a catalog validity failure.

Capacity admission, remaining-capacity totals, creature Size/carry limits, and inter-container
transfer remain unimplemented. Slice 6 is next.

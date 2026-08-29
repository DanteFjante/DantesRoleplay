# D&D 2024 component convergence — item runtime core receipt

Status: **accepted**  
Accepted: 2026-08-28  
Implementation: `prototype/dnd2024/planning/DND2024-ITEM-RUNTIME-CORE-SOL-SLICE-4A-IMPLEMENTATION.md`

## Delivered boundary

- Activated `dnd2024.core.definition-link` as the write-once runtime-to-definition owner and
  `dnd2024.item.quantity` as the positive safe-integer fungible count owner.
- Migrated item record/read/create/place/move; stack record/create/split/merge/consume; transfer;
  inventory, burden, capacity, and currency readers; equipment eligibility/read; item activity; and
  character-created starting Gold Pieces to the target shapes.
- Removed duplicated `stackKey` state. Stack compatibility now derives from the shared immutable
  definition link, while generic containment remains the sole custody, nesting, and slot authority.
- Preserved count conservation and atomicity: split creates a sibling beside its source, merge
  updates the target and deletes the source, and final consumption deletes the item rather than
  storing zero.
- Retired `dnd2024.item-instance` and `dnd2024.item-quantity` without aliases, dual reads, or dual
  writes. Existing mechanic IDs remain stable.
- Character cash creation now atomically creates one contained definition-linked GP stack using the
  target quantity and retains its replay and injected-failure rollback behavior.
- Added no C# rule logic, MCP/public operation, relationship vocabulary, live-state migration, or
  companion-interface dependency.

## Verification

- `roleplay validate catalog`: passed; 144 records validated, with the 21 pre-existing
  near-duplicate warnings and no live-data writes.
- Focused item/inventory and character-cash tests: passed, 24/24.
- Complete `Dnd2024AbilityCheckTests`: passed before final acceptance; the final full suite also
  contains the added closed-schema proof.
- Prototype record audit: 2,329/2,329 planned records, zero missing or duplicate IDs, unresolved
  references, component errors, or archetype-composition errors.
- Prototype tests: passed, 107/107.
- Full `DantesRoleplay.slnx` test suite: passed, 1,411 core tests plus 21 Local AI tests;
  1,432/1,432 total.
- `git diff --check`: no whitespace errors attributable to this slice.
- No protocol walk was required because this slice changed no MCP operation or dependency surface.

## Deliberate exclusions

The still-monolithic static `dnd2024.item-definition`, metric price/mass/capacity and burden
migration, decomposed equipment state and complete slot vocabulary, decomposed activities, attacks
and ammunition expenditure, attunement, durability, magic-item state, live-state migration,
companion UI, and new gameplay remain outside this receipt. Static item-definition decomposition is
the next ordered sub-slice of convergence leaf 7.

# D&D code-adoption Slice 8F receipt — inventory and carrying readers

Date: 2026-08-25  
Status: **accepted**  
Boundary: Parent 8 / 8F four classified effect-free readers

## Delivered

- Recovered bounded inventory inspection, physical currency value, exact rational item burden, and
  SRD carrying-capacity derivation.
- Declared every containment, item-state, definition-reference, ability, Size, and child-mechanic
  dependency. Carrying composes burden and accepts no caller-provided cached total.
- Kept all views stateless and effect-free; visible corrupt or incompatible state and arithmetic
  overflow fail closed.

## Verification

- Reader-focused activated-path scenarios — passed, 3/3.
- Full activated D&D suite — passed, 56/56.
- All D&D JavaScript syntax checks — passed, 47/47.
- Core catalog validation — passed, 144 records with 21 existing advisory warnings; fresh D&D
  preview/activation passed in every focused case and no live data was touched.
- Full repository suite — passed, 1,058/1,058 plus 20/20 local-AI tests.

## Evidence and exclusions

Tests prove deterministic visible inventory/equipment, explicit boundedness, mixed-denomination
coin totals, separate/fungible exact mass, child-composed burden, Medium capacity, no effects, and
incompatible-stack refusal. Stored inventory, wallets, cached burden/capacity, commerce, exchange,
encumbrance Conditions, movement mutation, magic exceptions, unbounded traversal, migrations,
public operations, live state, and archive mutation remain excluded.

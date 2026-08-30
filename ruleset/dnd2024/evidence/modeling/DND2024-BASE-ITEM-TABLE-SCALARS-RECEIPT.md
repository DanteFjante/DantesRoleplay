# DND2024-BASE-ITEM-TABLE-SCALARS V1 completion receipt

Status: **complete**
Implementation document: `DND2024-BASE-ITEM-TABLE-SCALARS-IMPLEMENTATION.md`
Source: `source.dnd2024.srd-5.2.1`; Adventuring Gear p. 95, Tack p. 100, and Food, Drink, and Lodging pp. 101–102

## Delivered boundary

- Enriched all 84 existing fixed-price item-definition records with exact CP, SP, or GP prices.
- Added exact pound weights to all 65 rows where the source states a weight; the 19 dash-weight rows
  omit physical weight instead of storing a fabricated zero.
- Preserved fractional weights as exact rationals, including the 58 1/2-pound Entertainer's Pack
  and 1/2-pound Mirror and Sack.
- Preserved the source-qualified 5-pound full Waterskin value.
- Added a closed fact module, complete materialization checks, and guarded repair of only the former
  source/version shells.

## Verification

| Check | Result |
| --- | --- |
| Closed scalar matrix | 84 of 84 exact prices; 65 stated weights; 19 omitted weights |
| Converter replay | 123 base-equipment records validated with no divergence |
| Normalization replay | 0 files changed |
| References | unresolved candidate count unchanged at 735 |
| Structural debt | unchanged at 660 component and 6 archetype errors |
| `npm test` | 57 passed, 0 failed |
| `git diff --check -- prototype/dnd2024` | passed |

## Deliberate exclusions

This receipt does not accept variable-price families, capacity, pack contents, item actions,
damage, conditions, lighting, fuel, consumable effects, mounts, vehicles, services, lifestyles,
crafting behavior, schema changes, new IDs, or catalog synchronization.

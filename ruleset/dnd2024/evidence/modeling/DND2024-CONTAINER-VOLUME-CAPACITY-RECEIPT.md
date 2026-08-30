# DND2024-CONTAINER-VOLUME-CAPACITY V1 completion receipt

Status: **complete**
Implementation document: `DND2024-CONTAINER-VOLUME-CAPACITY-IMPLEMENTATION.md`
Source: `source.dnd2024.srd-5.2.1`; equipment descriptions on PDF pp. 96-100

## Delivered boundary

- Added every source-stated reusable gear-container volume: 14 limits across 13 existing records.
- Stored exact reduced rational litres with no presentation rounding or imperial gameplay-unit
  references.
- Preserved separate Barrel capacities for 40 gallons of liquid and 4 cubic feet of dry goods.
- Replaced the unused singular `maximumVolume` component property with a closed
  `volumeCapacities` map supporting unconditional, liquid, and dry-goods limits.
- Preserved Backpack, Basket, Pouch, and Sack maximum-weight limits alongside their volume limits.
- Added a closed source-fact/conversion helper, guarded generator upgrade, exact-value coverage,
  unsupported-unit rejection, schema boundary tests, and replay checks.
- Reviewed Foundry dnd5e 6.0.x's dedicated container records. Its weight-typed single-capacity
  representation was not adopted because it cannot preserve the SRD Barrel distinction.

## Source matrix

| Record | Source capacity | Stored content key |
| --- | --- | --- |
| Backpack | 1 cubic foot | `any` |
| Barrel | 40 gallons liquid; 4 cubic feet dry goods | `liquid`; `dryGoods` |
| Basket | 2 cubic feet | `any` |
| Bottle, Glass | 1.5 pints | `any` |
| Bucket | 0.5 cubic foot | `any` |
| Chest | 12 cubic feet | `any` |
| Flask | 1 pint | `any` |
| Jug | 1 gallon | `any` |
| Pot, Iron | 1 gallon | `any` |
| Pouch | 0.2 cubic foot | `any` |
| Sack | 1 cubic foot | `any` |
| Vial | 4 fluid ounces | `any` |
| Waterskin | 4 pints | `any` |

## Verification

| Check | Result |
| --- | --- |
| Focused capacity/schema suite | 5 passed, 0 failed |
| Base-equipment generator | 123 records validated; guarded materialization and second replay passed |
| Normalization replay | 0 record files changed |
| Record inventory | 2,329 planned and 2,329 materialized |
| Structural audit | 0 missing, duplicate, unplanned, filename-mismatched, unresolved, component-invalid, or archetype-invalid findings |
| Imperial gameplay measurements | 0 references |
| Full `npm test` | 80 passed, 0 failed |
| Whitespace validation | passed |

## Deliberate exclusions

This receipt does not accept packaged consumable quantities such as Ink or Perfume, ammunition and
item-count storage, containment behavior, inventory transactions, UI rounding, canonical catalog
migration, reviewed synchronization, or the remaining equipment-fidelity cohorts.

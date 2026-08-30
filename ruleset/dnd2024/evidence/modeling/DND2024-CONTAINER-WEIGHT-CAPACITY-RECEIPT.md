# DND2024-CONTAINER-WEIGHT-CAPACITY V1 completion receipt

Status: **complete**
Implementation document: `DND2024-CONTAINER-WEIGHT-CAPACITY-IMPLEMENTATION.md`
Source: `source.dnd2024.srd-5.2.1`; Backpack and Basket p. 96, Pouch and Sack p. 99

## Delivered boundary

- Added exact maximum-weight container capabilities to Backpack (30 lb.), Basket (40 lb.), Pouch
  (6 lb.), and Sack (30 lb.).
- Extended shared normalization to rationalize container mass and future volume measurements.
- Preserved all existing price, physical weight, identity, citation, and archetype data.
- Added guarded recognition of the prior scalar-only records and focused exact-value tests.

## Verification

| Check | Result |
| --- | --- |
| Container weight matrix | 4 of 4 exact limits |
| Converter replay | 123 base-equipment records validated with no divergence |
| Normalization replay | 0 files changed |
| References | unresolved candidate count unchanged at 735 |
| Structural debt | unchanged at 660 component and 6 archetype errors |
| `npm test` | 61 passed, 0 failed |
| `git diff --check -- prototype/dnd2024` | passed |

## Deliberate exclusions

No volume-unit records currently exist, so this receipt does not accept cubic-foot, gallon, pint,
or ounce capacities. It also excludes the other containers, content restrictions, containment
effects, schemas, new IDs, and catalog synchronization.

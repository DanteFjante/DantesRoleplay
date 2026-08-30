# DND2024-LARGE-VEHICLE-TABLE V1 completion receipt

Status: **complete**
Implementation document: `DND2024-LARGE-VEHICLE-TABLE-IMPLEMENTATION.md`
Source: `source.dnd2024.srd-5.2.1`, `Equipment > Mounts and Vehicles > Airborne and Waterborne Vehicles`, PDF p. 101

## Delivered boundary

- Repaired all seven large-vehicle profiles from the SRD table.
- Corrected Galley cargo from 3,000 to 300,000 pounds, Rowboat speed from 1 to 1 1/2 mph, and
  Warship speed from 2 to 2 1/2 mph.
- Added exact Armor Class and maximum Hit Points to every large vehicle.
- Added exact damage thresholds to Galley, Keelboat, Longship, Sailing Ship, and Warship while
  omitting the source-dash threshold for Airship and Rowboat.
- Preserved all five drawn-vehicle records unchanged and added guarded recognition of the former
  seven-record output.

## Verification

| Check | Result |
| --- | --- |
| Large-vehicle matrix | 7 of 7 exact profiles and durability bases |
| Converter replay | 12 vehicle records validated with no divergence |
| Normalization replay | 0 files changed |
| References | unresolved candidate count unchanged at 735 |
| Structural debt | unchanged at 660 component and 6 archetype errors |
| `npm test` | 60 passed, 0 failed |
| `git diff --check -- prototype/dnd2024` | passed |

## Deliberate exclusions

This receipt does not accept drawn-vehicle placeholder profiles, vehicle prices, strong-wind/current
movement rules, control activities, crew/passenger runtime entities, attacks, damage application,
schema changes, new IDs, or catalog synchronization.

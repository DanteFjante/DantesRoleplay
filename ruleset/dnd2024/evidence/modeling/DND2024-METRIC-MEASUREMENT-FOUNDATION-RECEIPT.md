# DND2024-METRIC-MEASUREMENT V1 receipt — exact prototype foundation

Status: **accepted**
Implementation: `DND2024-METRIC-MEASUREMENT-FOUNDATION-IMPLEMENTATION.md`
Ruleset alignment: **dnd2024-compatible repository policy**

## Delivered boundary

- Added the confirmed meter, kilometer, kilogram, liter, and volume-unit category identities.
- Added exact reduced-rational conversion for foot-to-meter, mile-to-kilometer, and
  pound-to-kilogram measurements.
- Made measurement normalization recursive, dimension-aware, overflow-checked, immutable to its
  caller, and idempotent.
- Migrated all 549 existing imperial gameplay measurements: 345 distances to metres, 12 travel
  distances to kilometres, and 192 masses to kilograms.
- Preserved all 12 semantic time measurements without unit conversion.
- Replayed all current measurement-producing generators successfully and added a repository-wide
  rejection test for imperial gameplay measurement references.

## Verification evidence

- Vocabulary materialization: 271 records.
- Measurement-producing generator replay: species 9, creatures 330, base equipment 123, tools 37,
  tool variants 14, weapons 38, consumables 3, mounts 8, and vehicles 12.
- Idempotent normalization: 0 files changed on the final pass.
- Record audit: 2,293 records; 2,275 planned inventory records; 0 missing inventory IDs; 0 duplicate
  IDs; 0 filename mismatches; unresolved/reference and placeholder debt unchanged except for the
  five planned vocabulary additions.
- Full prototype suite: 71 passed, 0 failed.
- Whitespace validation: passed for the prototype tree.

## Deliberate exclusions

No source-stated volume capacities were added. No canonical catalog schema or record changed, no
database synchronization ran, no UI rounding was introduced, imperial import/source vocabulary was
deleted, and no D&D rule or outcome changed. Those remain subsequent dependency-tree leaves.

# DND2024-ARMOR-TABLE-SCALARS V1 completion receipt

Status: **complete**
Implementation document: `DND2024-ARMOR-TABLE-SCALARS-IMPLEMENTATION.md`
Source: `source.dnd2024.srd-5.2.1`, `Equipment > Armor > Armor table`, PDF p. 92

## Delivered boundary

- Enriched all thirteen existing armor records with exact GP prices and pound weights from the SRD
  Armor table.
- Reused the existing item price, item physical, gold-piece, pound, and rational-value owners.
- Added a complete closed armor-fact map and a guarded repair path that recognizes only the former
  source/version-only armor shells.
- Added focused assertions for every armor row while preserving IDs, archetypes, source citations,
  and all non-armor base-equipment outputs.

## Verification

| Check | Result |
| --- | --- |
| SRD Armor-table matrix | 13 of 13 exact prices and weights |
| Converter replay | 123 base-equipment records validated with no divergence |
| Normalization replay | 0 files changed |
| References | unresolved candidate count unchanged at 735 |
| Structural debt | unchanged at 660 component and 6 archetype errors |
| `npm test` | 53 passed, 0 failed |
| `git diff --check -- prototype/dnd2024` | passed |

## Deliberate exclusions

This receipt does not accept or implement Armor Class calculations, Dexterity contribution,
Strength requirements or Speed penalties, Stealth disadvantage, armor-training consequences,
don/doff timing, equipment-slot semantics, activities, schema changes, new IDs, or catalog
synchronization. Those require later rule-owner and reference-resolution slices.

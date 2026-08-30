# DND2024 live location-owned map media — Slice 1 receipt

Status: **accepted 2026-08-30**
Ruleset alignment: ruleset-neutral presentation metadata

## Delivered boundary

- Added the closed `game.core.world.map.visual` component and schema for exact Player and DM
  asset-key variants.
- Defined nested region containment so Thalos may contain countries/regions and those regions may
  contain cities and other locations.
- Kept map ownership, hierarchy, anchors, audience variants, and presentation assets separate.
- Documented fail-closed audience selection and validation constraints in the World procedures.

## Evidence

- `CatalogWorldMapVisualTests`: **16 passed, 0 failed**.
- Fresh catalog validation: **145 records valid**, with 23 pre-existing near-duplicate warnings.
- The live component registration accepted version 1 with schema hash
  `920CA45514E00845369868B0128052D766993472DC6BE403853C31709BE2CDBB`.

## Deliberate exclusions

No live World records, website projection, asset bytes, travel/discovery behavior, geometry, or
D&D mechanic was added in this slice.

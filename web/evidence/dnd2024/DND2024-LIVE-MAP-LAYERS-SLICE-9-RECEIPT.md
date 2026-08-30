# DND2024 live scoped maps Slice 9 receipt — location-kind layer controls

Status: **accepted 2026-08-30**

## Delivered boundary

The connected map projection now places each already-authorized marker in a deterministic Region,
Settlement, Sites & interiors, or Other places layer derived from the canonical World location kind.
Each map declares only categories present in that audience's projected features. A reusable React
layer control shows the per-layer marker count and independently toggles each category without
changing the envelope or game state. Hiding the selected marker's layer clears its detail; selecting
an annotation for a hidden marker makes that marker's layer visible again.

During verification, the cropped Region-map membership path was also corrected to prefer the exact
containment delivered by Slice 8, retaining the authored table only as the documented fallback.

## Evidence

- Focused connected-map, containment, and layer-state tests: 60 passed, 0 failed.
- Full prototype suite: 151 passed, 0 failed.
- Production prototype build: passed.
- Live `GET http://localhost:5173/api/hub?perspective=dm`: Thalorien projected 24 markers across
  `Regions: 9`, `Settlements: 5`, and `Sites & interiors: 10`, with 9 Region maps and ready status.

## Deliberate exclusions

No location kind, map coordinate, World or Campaign state, schema, migration, route, faction
territory, campaign note, access policy, or D&D mechanic changed.

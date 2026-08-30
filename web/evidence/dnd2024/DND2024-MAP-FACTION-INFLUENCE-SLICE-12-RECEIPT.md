# DND2024 atlas Slice 12 receipt — projected faction influence highlighting

Status: **accepted 2026-08-30**

## Delivered boundary

The scoped map workspace now resolves already-projected faction territories to markers by exact
`territory.id === feature.locationId` equality. A reusable faction control selects one influence or
none, highlights only exact markers on the active scope, and identifies the recorded faction in the
selected-place detail. It explicitly describes points as recorded presence rather than borders or
exclusive control. Names are never used as a fallback and the control is absent when no exact
projected target exists.

## Evidence

- Focused faction/map state tests: 23 passed, 0 failed.
- Full prototype suite: 159 passed, 0 failed.
- Production prototype build: passed.
- Current live Thalorien data has no projected faction territories, so the control correctly emits
  no name, count, or placeholder on the live map.

## Deliberate exclusions

No polygon, border, control inference, relationship read, faction/World/Campaign write, geography,
route, schema, migration, or D&D mechanic changed.

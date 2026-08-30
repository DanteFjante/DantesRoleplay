# DND2024 live scoped maps Slice 8 receipt — exact containment hierarchy

Status: **accepted 2026-08-30**

## Delivered boundary

The live adapter now reads each location's exact application-state containment independently from
its location component. The connected projection carries the optional container ID and prefers the
nearest projected containing Region. Missing or malformed containment still falls back to the
existing safe inference, and a containment failure cannot erase valid location detail.

No game state, map coordinates, city semantics, schema, migration, route, catalog record, or D&D
mechanic changed.

## Evidence

- Focused containment/Region tests: 49 passed, 0 failed.
- Full prototype suite: 148 passed, 0 failed.
- Production prototype build: passed.
- Live `GET http://localhost:5173/api/hub?perspective=dm`: the current `dnd2024-main` Thalorien
  envelope grouped Brackenford and the other mapped places under their exact containing Regions.
- Read paths remain independent: exact containment and component detail are fetched separately,
  with partial success preserved.

## Deliberate exclusions

Location-kind layers, campaign-note overlays, canonical map-document authority, city hierarchy,
generated imagery, and every world-state mutation remain outside this receipt.

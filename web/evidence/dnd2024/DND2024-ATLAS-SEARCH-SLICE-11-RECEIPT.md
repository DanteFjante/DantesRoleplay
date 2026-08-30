# DND2024 atlas Slice 11 receipt — cross-scope place search

Status: **accepted 2026-08-30**

## Delivered boundary

The scoped map workspace now contains a reusable atlas search over the already-projected maps and
features. It matches visible names and details, collapses repeat representations by exact projected
location ID, prefers the active scope and otherwise the closest available scope, and atomically
opens the declared map with its marker selected. Blank, absent, and no-match states reveal nothing
outside the ready envelope and perform no server or state write.

## Evidence

- Focused map-state and connected-envelope tests: 39 passed, 0 failed.
- Full prototype suite: 158 passed, 0 failed.
- Production prototype build: passed.

## Deliberate exclusions

No fuzzy entity resolution, discovery, reveal, World/Campaign state, geography, coordinates, route,
faction overlay, list mode, schema, migration, or D&D mechanic changed.

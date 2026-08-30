# DND2024 live scoped maps Slice 10 receipt — authorized campaign-note overlays

Status: **accepted 2026-08-30**

## Delivered boundary

The connected projection now turns each nonempty server-authorized knowledge-location group into a
deterministic campaign note on every projected map scope that contains the same exact named World
feature. The note reuses the accepted campaign-overlay UI and carries only label, prose, context,
map ID, and feature ID; it cannot place or alter geography. Unknown, ambiguous, unplaced, empty, or
unavailable targets are dropped without a placeholder or leak.

A real Player seat receives actor-authorized notes and a DM perspective receives GM-authorized
notes. A local DM's Player-preview emits no live notes because the underlying server request still
has GM authority and therefore cannot prove those notes are actor-safe.

## Evidence

- Focused connected-map, note, placement, and map-state tests: 42 passed, 0 failed.
- Full prototype suite: 154 passed, 0 failed.
- Production prototype build: passed.
- Live connected envelope: 2 DM annotations resolved to 2 visible features across 2 map scopes;
  local Player-preview emitted 0 annotations; both envelopes remained ready.

## Deliberate exclusions

No knowledge reveal/write, World or Campaign state, geometry, map layer, faction territory, schema,
migration, route, access grant, or D&D mechanic changed. Perspective-bound knowledge issuance
remains server work rather than a browser-side claim.

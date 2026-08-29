# Feature 16 — Slice 1 receipt

Date: 2026-08-21

## Delivered behavior

- Added the closed, creature-owned `dnd2024.temporary-hit-points` component. Its absence is the
  only representation of no Temporary Hit Point buffer; zero is not representable.
- Added `mechanic.dnd2024.temporary-hit-points.write` and its governing contract.
- A first grant adds a buffer; an existing buffer is explicitly kept with zero effects or replaced
  with one set effect; expiry removes it.
- The transition does not heal, change maximum Hit Points, declare an event, or model damage
  absorption. Those remain Feature 16's later slices.

## Evidence

- Focused Feature 16 fresh-import coverage: 1 passed, 0 failed.
- `roleplay validate catalog`: 316 records valid in a fresh disposable database; 41 existing
  near-duplicate warnings; no live data touched.
- Full suite: 615 passed, 0 failed, 0 skipped.

No persistent catalog import was performed.

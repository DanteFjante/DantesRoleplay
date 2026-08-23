# Feature 16 — Slice 2 receipt

Date: 2026-08-21

## Delivered behavior

- Added `mechanic.dnd2024.healing.apply` and its governing contract.
- Healing accepts one positive safe-integer amount, raises current Hit Points only up to maximum,
  and reports the amount lost to that clamp.
- Added the closed `dnd2024.healing.received` event on every successful healing action, including a
  zero-applied action at maximum.
- Temporary Hit Points, conditions, maximum Hit Points, and death state remain untouched.

## Evidence

- Focused Feature 16 coverage: 2 passed, 0 failed.
- `roleplay validate catalog`: 322 records valid in a fresh disposable database; 45 existing
  near-duplicate warnings; no live data touched.
- Full suite: 630 passed, 0 failed, 0 skipped.

No persistent catalog import was performed.

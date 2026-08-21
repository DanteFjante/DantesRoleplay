# Feature 15 — Slice 2 receipt

Date: 2026-08-21

## Delivered behavior

- Added `dnd2024.damage-mitigation`, a closed creature component containing canonical Resistance,
  Immunity, and Vulnerability lists with fixed SRD attribution.
- Added `mechanic.dnd2024.damage-mitigation.write`, which records or corrects the complete state in
  one effect, rejects corrupt state, and canonicalizes input order.
- A damage type can appear in multiple lists, as the later resolver must apply their SRD interaction
  rather than making legitimate state unrepresentable.

## Evidence

- Focused Feature 15 tests: 2 passed, 0 failed.
- Catalog validation: valid in a fresh disposable database; no live data touched. The validator
  reported only near-duplicate warnings.
- Full suite: 587 passed, 0 failed, 0 skipped.
- `git diff --check`: passed.

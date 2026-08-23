# Feature 15 — Slice 3 receipt

Date: 2026-08-21

## Delivered behavior

- Added `mechanic.dnd2024.damage.resolve`, an effect-free profile reader for stored mitigation and
  Petrified condition state.
- Missing mitigation and condition components remain distinguishable from known-empty state.
- The governing contract records the required arithmetic: Immunity, then one Resistance halving,
  then Vulnerability doubling. Stored and Petrified resistance produce two reasons but halve once.

## Evidence

- Focused Feature 15 tests: 3 passed, 0 failed.
- `roleplay validate catalog`: valid in a fresh disposable database; no live data touched. The
  validator reported only near-duplicate warnings.
- Full suite: 589 passed, 0 failed, 0 skipped.
- `git diff --check`: passed.

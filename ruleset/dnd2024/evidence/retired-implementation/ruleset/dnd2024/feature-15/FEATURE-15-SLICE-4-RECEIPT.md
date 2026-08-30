# Feature 15 — Slice 4 receipt

Date: 2026-08-21

## Delivered behavior

- Revised `mechanic.dnd2024.weapon-damage.apply` to compose both the frozen weapon-damage result
  and `mechanic.dnd2024.damage.resolve` profile.
- Applies the SRD order atomically: Immunity, then one Resistance halving (including Petrified),
  then Vulnerability doubling. Hit Points still receive exactly one complete `component.set`.
- Added the registered, closed `dnd2024.damage.dealt` event. It records raw and final damage,
  mitigation facts, Hit Point before/after values, maximum, critical status, and overkill.
- Updated the weapon damage and attack readers to validate the expanded, authoritative
  `dnd2024.weapon-profile` shape, preserving compatibility with the catalog's current weapons.
- Updated event and Feature 10 test setup so catalog-defined gameplay events are registered and
  structural-ledger checks remain scoped to structural event types.

## Evidence

- Focused Feature 8, 9, 10, 15, and event-ledger tests: 23 passed, 0 failed.
- `roleplay validate catalog`: 304 records valid in a fresh disposable database; 34 existing
  near-duplicate warnings; no live data touched.
- Full suite: 598 passed, 0 failed, 0 skipped.
- `git diff --check`: passed (only existing line-ending notices were reported).

No persistent catalog import was performed.

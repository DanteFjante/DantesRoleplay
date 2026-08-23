# Feature 14, Slice 2 — Exhaustion D20 Test penalty receipt

Date: 2026-08-21  
Status: **Verified**

## Delivered boundary

The shared condition state-effects resolver now reports the source-free Exhaustion entry and
returns exactly one auditable modifier, `condition:exhaustion (level <n>)`, valued at `-2 × n`.
Ability checks, saving throws, weapon attacks, and Initiative each append that modifier once to
their existing modifier list and calculate the total from that list.

The modifier never becomes a circumstance: it cannot alter roll mode, dice count, selected die, or
natural-roll classification. Weapon attacks deliberately use only the attacker's modifier; an
exhausted defender does not reduce the attacker's total. Automatic and voluntary saving-throw
failure retains null total and does not report the modifier.

## Evidence

- `CatalogFeature14Tests.Exhaustion_applies_one_flat_penalty_to_each_d20_test_owner` proves a
  level-three `−6` adjustment for all four owners, unchanged rolls and roll mode, no target leak,
  automatic-failure behavior, and natural-20 precedence.
- Feature 13/14 focused regressions: **11 passed, 0 failed**.
- Catalog validation succeeded (270 records); warnings are existing overlap reports from unrelated
  concurrent catalog work. No live data was touched.
- Isolated serial full suite: **557 passed, 0 failed, 0 skipped**.
- `git diff --check` completed without whitespace errors; line-ending notices are workspace-wide.

## Next boundary

Slice 3 restores an active participant's movement allowance as
`max(0, movementMaximumFeet - 5 × exhaustionLevel)` without changing the recorded maximum.

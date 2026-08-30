# Feature 14, Slice 1 — Exhaustion state and lethal event receipt

Date: 2026-08-21  
Status: **Verified**

## Delivered boundary

`dnd2024.conditions` now permits exactly one source-free Exhaustion entry with integer `level` 1
through 6, while preserving the existing 100-entry source-instance capacity for all other
conditions. `mechanic.dnd2024.conditions.write` now owns closed `exhaust` and `recover` modes;
normal `apply` and `clear` reject Exhaustion.

Reaching level 6 emits one `dnd2024.exhaustion.reached-lethal` event with the creature id, constant
level, and SRD locator. It does not apply death state. The state-effects resolver accepts valid
Exhaustion state without treating it as corrupt; Slice 2 owns the resulting numeric modifier.

## Correction recorded

The prior plan proposed increasing `entries.maxItems` to 15. That would have regressed Feature 13:
non-Exhaustion instances are source-scoped and may legitimately occupy the existing 100 entries;
Petrified and Poisoned also cannot coexist. The capacity stays at 100, with one additional semantic
rule—not a lower storage limit—for Exhaustion.

## Evidence

- `CatalogFeature14Tests` covers level-by-level gain, full recovery, re-reaching level 6, exact
  event payload, invalid inputs, source rejection, absent/corrupt state, resolver compatibility,
  and the 100-entry capacity boundary.
- Focused Feature 13/14 catalog regressions: **9 passed, 0 failed**.
- Focused Feature 14 tests: **3 passed, 0 failed**.
- Catalog validation: **266 records valid**; one warning remains for unrelated
  `procedure.campaign.session` overlap. No live data was touched.
- Isolated serial full suite: **548 passed, 0 failed, 0 skipped**.
- `git diff --check` passed; reported line-ending notices are pre-existing workspace settings.

## Next boundary

Slice 2 will expose `-2 × Exhaustion level` through the shared state-effects resolver and append it
once to each ability check, saving throw, weapon attack, and Initiative total. It must not change
roll mode, dice selection, or natural-roll classification.

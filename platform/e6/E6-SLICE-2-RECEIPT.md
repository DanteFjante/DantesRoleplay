# E6 Slice 2 receipt — root proposal aggregation

Status: **Accepted 2026-08-21.**

## Delivered

- Added a composition-only proposal envelope for child effects, declared events, and
  notifications.
- Preserved depth-first child execution order: recursive descendants, child output, later
  siblings, then the root parent output.
- Merged that envelope into the existing top-level action dry-run/apply/audit path, retaining one
  transaction and rollback boundary.
- Kept each child output in `ctx.children` unchanged and frozen; aggregation does not create a
  state, effect, event, or notification input for another child.
- Added generic tests for independent/recursive ordering and all-or-nothing rollback.

## Not delivered

No game mechanic was migrated. No child effect is applied early, no staged actor state or virtual
projection exists, and there is no arbitrary child-result/effect query capability.

## Verification

| Check | Result |
| --- | --- |
| E6 focused composition tests | Passed: 8 tests |
| Existing action-runner and mechanic-store tests | Passed: 41 tests |
| `roleplay.cmd validate catalog` | Validated 355 records with 56 advisory overlap warnings; no live data touched |
| Full repository suite | Passed: 652 tests, 0 failures |
| Focused diff check | Passed |

No persistent catalog import or live campaign change was performed.

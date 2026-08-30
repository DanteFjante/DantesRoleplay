# Feature 17 — Slice 3 receipt

Date: 2026-08-21

## Delivered behavior

- Added a single guard mechanic and two global structural-event subscriptions: one for condition
  additions and one for replacements.
- The guard validates the full closed `dnd2024.conditions` list before it commits: source reference,
  vocabulary, entry shape, canonical order, uniqueness, Exhaustion level, and Petrified/Poisoned
  incompatibility.
- It rejects malformed condition proposals with stable denial codes and rolls back the full batch.
  Changes to another component do not invoke it.
- Condition-source identity remains historical provenance. The existing normal writer validates a
  source when it is applied; the guard does not require an old source entity to remain present.

## Evidence

- Focused Feature 13, 14, and 17 regressions with the guard active: 15 passed, 0 failed.
- `roleplay validate catalog`: 373 records valid in a fresh disposable database; 63 existing
  near-duplicate warnings; no live data touched.
- Full-suite attempt did not reach a stable result because of concurrent, unrelated work: four
  non-Feature-17 failures (bootstrap feedback coverage and knowledge-search fixture expectations)
  occurred before the test host crashed. The Feature 17 slice is therefore verified in scope, with
  repository-wide acceptance still pending a stable shared baseline.

No persistent catalog import was performed.

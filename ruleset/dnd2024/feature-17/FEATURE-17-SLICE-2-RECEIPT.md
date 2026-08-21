# Feature 17 — Slice 2 receipt

Date: 2026-08-21

## Delivered behavior

- Added the closed `dnd2024.death-state` component: successes, failures, Stable, terminal death,
  and an SRD source reference.
- Added the component's schema, governing contract, and an administrative `begin`, `correct`, and
  `end` writer.
- The writer preserves the zero-HP policy, Hit Points, Temporary Hit Points, and conditions; it
  creates no event and performs no roll.
- Tallies are bounded to `0..2`; Stable requires zero tallies; Stable and dead cannot coexist; a
  dead state cannot be cleared or removed by this feature.

## Evidence

- Focused Feature 17 regression coverage: 2 passed, 0 failed.
- `roleplay validate catalog`: 365 records valid in a fresh disposable database; 60 existing
  near-duplicate warnings; no live data touched.
- Full suite: 655 passed, 2 failed, 0 skipped. Both failures are outside Feature 17 and arise from
  concurrently changed world fixtures: `CatalogWorldFeature4Tests.Fresh_import_contains_scoped_fact_rumour_secret_and_three_clues_without_copying_truth`
  (knowledge-link invariant) and `CatalogWorldFeature7Tests.Imported_contract_publishes_all_four_recipes_through_the_public_graph_query_without_world_writes`
  (expected 15 vs actual 23 recipes).

No persistent catalog import was performed.

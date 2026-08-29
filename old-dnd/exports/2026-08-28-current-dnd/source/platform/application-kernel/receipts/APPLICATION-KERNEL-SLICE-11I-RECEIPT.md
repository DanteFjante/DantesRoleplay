# Application kernel Slice 11I receipt — legacy projection-adoption classification

Status: **accepted**  
Completed: 2026-08-24  
Accepted implementation: [Slice 11I](../APPLICATION-KERNEL-SLICE-11I-IMPLEMENTATION.md)

## Delivered

- Classified the exact 14 ratified `dnd2024` mechanics through the runtime-owned
  `MechanicRequirements` contract and proved every declared component belongs to the 33 accepted
  application component schemas.
- Proved the requirement documents contain only the existing roles, event, and child-composition
  declarations and that the authored catalog contains no structural projection source.
- Extended the fresh-host registration/preview/activation proof to require an empty `dnd2024`
  projection dependency graph and zero application-qualified rows across every projection table.
- Registered no projection definition. The legacy mechanic projection remains the owner of exact
  component visibility; no projection ID, mapping, derived value, D&D rule, migration, or runtime
  production branch was invented.
- No live database was read or written.

## Evidence

- Focused classification and fresh-host activation checks: 2 passed, 0 failed.
- Generic projection-materialization and game-boundary checks: 14 passed, 0 failed.
- Full shared suite: 692 passed, 0 failed.
- Standalone local-AI suite: 20 passed, 0 failed.
- Catalog validation: 144 records valid; 21 unchanged advisory near-duplicate warnings; no live
  data touched.
- Isolated-output solution build: passed with 0 warnings and 0 errors.
- `git diff --check`: passed.

## Deliberate exclusions and next gate

This slice closes only the required native/donor projection-adoption leaf, with an evidence-based
empty set. Legacy state-space identity and backfill, non-empty migration, application action
execution, remaining catalog adapters, and final parity remain separate gates. State adoption must
not infer ownership or rewrite live data without its own confirmed migration boundary.

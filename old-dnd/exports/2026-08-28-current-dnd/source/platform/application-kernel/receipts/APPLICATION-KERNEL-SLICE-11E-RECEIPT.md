# Application kernel Slice 11E receipt — bounded string constraints and final legacy contracts

Status: **accepted**  
Completed: 2026-08-24  
Accepted implementation: [Slice 11E](../APPLICATION-KERNEL-SLICE-11E-IMPLEMENTATION.md)

## Delivered

- Added immutable `system-json-schema-2020-12/v2` support for resource-bounded anchored patterns
  and asserted `date-time` values while retaining least-profile selection for every v1 schema.
- Preserved the exact v1 profile/hash for original-keyword schemas and evaluated component and
  projection values against their stored profile.
- Restricted patterns to the small non-branching grammar required by the catalog, with pattern
  count/length/repetition and unbounded-input limits; unsafe regex and every other format reject.
- Added one atomic SQLite migration allowing v1 and v2 rows. Existing rows are not rewritten and a
  downgrade containing v2 history fails transactionally instead of deleting it.
- Removed only the top-level non-validating `title` annotations from checkpoint and recap schemas;
  all pattern/date-time and structural constraints remain intact.
- Extended disposable fresh-host MCP evidence from 30 to all 32 `dnd2024.game.core.*` version-1
  registrations. `dnd2024.stats` and every state table remain absent.

## Evidence

- Focused schema, component registry/administration, migration drift, and fresh-host MCP checks:
  50 passed, 0 failed.
- Full shared suite: 685 passed, 0 failed.
- Standalone local-AI suite: 20 passed, 0 failed.
- Catalog validation: 144 records valid; 17 existing advisory near-duplicate warnings; no live
  data touched.
- Isolated-output solution build: passed with 0 warnings and 0 errors.
- `git diff --check`: passed.

## Deliberate exclusions and next gate

This slice does not register `stats`, write/backfill component values, create or migrate a legacy
state space, enable default-host application registration, adopt projections/mechanics/aliases,
or implement vectors/local/remote AI orchestration. Slice 11's remaining work starts with the next
single classified adoption owner, not another schema-profile expansion.

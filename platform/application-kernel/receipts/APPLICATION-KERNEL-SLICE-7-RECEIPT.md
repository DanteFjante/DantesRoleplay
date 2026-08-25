# Application kernel Slice 7 receipt — versioned structural projections

Status: **accepted**  
Completed: 2026-08-24  
Accepted implementation: [Slice 7](../APPLICATION-KERNEL-SLICE-7-IMPLEMENTATION.md)

## Delivered

- Added immutable, application-owned projection definitions with exact component and projection
  references, closed role bindings, structural RFC 6901 copy mappings, output schemas, content
  hashes, version replay/append behavior, and deterministic forward/reverse impact evidence.
- Added one bounded ECS batch-read seam and a read-only materializer. It builds dependent
  projections before their consumers, deduplicates declared component locations, validates each
  output against its exact schema, and returns compact output with component revision evidence.
- Enforced same-application state-space ownership, fixed input/mapping/role/depth/read/output
  bounds, no dynamic code, no calculation/default/branch semantics, and no persisted cache.
- Added the five additive projection tables, a forward-only migration policy, dependency injection,
  model snapshot, catalog-coverage classifications, and focused fixtures.

## Evidence

- Revalidated 2026-08-24: local-reference/composition-aware schema paths, append/replay/no-row
  failures, 16-level dependency bound, multi-component single-batch reads, missing component/role,
  output validation, and cross-state-space isolation now have direct focused evidence.
- Focused remediated projection suite: 10 passed, 0 failed; projection/ECS/schema/migration/catalog
  group: 43 passed, 0 failed.
- Focused ECS/projection tests: 7 passed, 0 failed.
- Final cross-slice full shared suite: 530 passed, 0 failed.
- Standalone local-AI suite: 19 passed, 0 failed.
- Solution build: passed with 0 warnings and 0 errors.
- Fresh catalog validation: 144 records valid; 17 existing advisory warnings; no live data touched.
- `git diff --check`: passed; Git emitted line-ending notices only.

## Deliberate exclusions

No activation/effective-manifest integration, cache, effects/events/audit integration, legacy
projection or `IWorldStore` path, public transport, authorization, catalog import, application
content, or AI work was added. Slice 8 owns write-path parity and legacy adoption only after its
own confirmation gate.

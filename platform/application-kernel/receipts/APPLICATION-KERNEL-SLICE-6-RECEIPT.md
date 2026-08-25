# Application kernel Slice 6 receipt — application-scoped ECS state

Status: **accepted**  
Completed: 2026-08-24  
Accepted implementation: [Slice 6](../APPLICATION-KERNEL-SLICE-6-IMPLEMENTATION.md)

## Delivered

- Added a parallel, application-scoped ECS store with immutable state-space bindings, entities, and
  component instances. It does not read, alter, or backfill legacy world tables.
- Added exact component type/version/schema-hash references, bounded schema validation before
  writes, all-JSON-kind storage, object-only shallow merge, null-versus-remove semantics, and
  optimistic component/entity revisions.
- Enforced state-space isolation and same-application type ownership. A state space records an
  exact registered application revision and manifest fingerprint, but remains trusted in-process
  pre-activation evidence until the later activation/upgrade boundary verifies effective manifests.
- Added the additive `ApplicationScopedEcs` migration, model mapping, DI registrations, catalog
  coverage classifications, and a forward-only downgrade policy.

## Evidence

- Focused application-scoped ECS tests: 5 passed, 0 failed.
- Focused component-type registry tests: 3 passed, 0 failed.
- Focused migration tests: 7 passed, 0 failed.
- Solution build: passed with 0 warnings and 0 errors.
- Full shared suite: 500 passed, 0 failed.
- Standalone local-AI suite: 19 passed, 0 failed.
- `git diff --check`: passed; Git emitted line-ending notices only.

## Deliberate exclusions

No legacy `IWorldStore` caller uses this store yet. No legacy backfill, application activation,
state-space upgrade, effects/events/audit parity, catalog parser/import, protocol kind,
authorization behavior, projection materialization, application content, or AI integration was
added. Slice 7 must own projections only after its own confirmed implementation boundary.

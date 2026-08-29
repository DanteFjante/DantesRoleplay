# Application kernel Slice 2 receipt — pure contracts and in-memory validation

Status: **accepted**  
Completed: 2026-08-23

## Delivered

- Added ruleset-neutral, persistence-free contracts and deterministic in-memory validation for
  application IDs/registrations, source precedence, component type versions, derived-projection
  dependency graphs, and catalog cursors.
- Enforced reserved `system`, lowercase application IDs, owner-qualified component/projection IDs,
  immutable component versions, relative source specifications, trust/precedence conflicts,
  structural projection mappings, acyclic dependencies, and authenticated manifest-bound cursors.
- Added defensive copying/read-only graph and revision results, preventing callers from mutating
  stored fake state through input or output collections.
- Added component manifests for the five new kernel component directories; no production host,
  database, catalog, filesystem scanner, state write, or protocol kind was changed by this slice.

## Evidence

- Focused `ApplicationKernel` suite: 7 passed, 0 failed.
- Solution build: passed with 0 warnings and 0 errors.
- Full shared suite: 462 passed, 0 failed.
- Generic-source vocabulary scan found no application/game literal in the five new domain areas.
- Slice-scoped `git diff --check`: passed (line-ending notices only).

## Deliberate exclusions

- No application/source/projection data is authoritative or persisted yet.
- No migration, filesystem scan, source winner materialization, JSON Schema validation, ECS write
  integration, effect, protocol dispatch, activation, state-space, alias, or legacy migration was
  added.
- Slice 3 is the next persistence boundary and requires its own active implementation document.

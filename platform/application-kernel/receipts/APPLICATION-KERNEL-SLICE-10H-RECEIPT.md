# Application kernel Slice 10H receipt — empty-state upgrade compatibility

Status: **accepted**  
Completed: 2026-08-24  
Accepted implementation: [Slice 10H](../APPLICATION-KERNEL-SLICE-10H-IMPLEMENTATION.md)

## Delivered

- Added authenticated `commit(kind: "system.state-space.upgrade")` to the existing three-verb
  protocol. Private-operator `Modify` authorization runs before payload parsing or service access.
- Required the exact current binding fingerprint and exact current activation fingerprint. Dry run
  derives the target application revision, binding revision, fingerprints, state counts, and
  compatibility evidence; callers cannot assert compatibility or provide migration instructions.
- Allowed an upgrade only when the state space contains zero entities and zero components. Any
  persisted state returns `MIGRATION_REQUIRED` without changing current or historical bindings.
- Added binding revision/update evidence to `system_state_space` and immutable
  `system_state_space_binding_revision` history through one additive forward-only migration.
- Made current-binding mutation, retained baseline history, next-revision history, and successful
  operation audit one transaction. Injected audit failure and post-dry-run drift roll everything
  back.
- Preserved the immutable application identity and exact historical replay. Existing Slice 10G
  creation tokens and every upgrade token return their original binding after later activation or
  upgrade changes, including state spaces first created before binding history existed.
- Extended the existing exact application query with binding revision/update evidence and extended
  capability discovery, dispatcher examples, operating procedure, component metadata, denial
  tests, and the live protocol walk without adding another public tool or query kind.

## Evidence

- Focused state-space, ECS, authorization, catalog-coverage, migration, and live protocol checks:
  30 passed, 0 failed.
- The live protocol walk proved second activation, missing-dry-run rejection, exact dry run,
  upgrade, replay, historical creation replay, query-back at binding revision 2, remote denial,
  two immutable binding records, and zero entities/components.
- Full shared suite after a clean rebuild: 622 passed, 0 failed.
- Standalone local-AI suite: 19 passed, 0 failed.
- Catalog validation: 144 records valid; 17 existing near-duplicate warnings; no live data touched.
- Solution build: passed with 0 warnings and 0 errors.
- Entity Framework model drift check: no changes since the new migration. The installed EF tool
  emitted only its existing patch-version advisory.

## Deliberate exclusions and next gate

This slice does not infer compatibility for persisted state, accept caller-authored compatibility
or migration data, migrate or backfill legacy entities/components, change application identity,
import declared records, execute application mechanics, enable remote MCP, or implement AI
orchestration. Slice 10 is complete. Slice 11 may register and activate `dnd2024`, classify its
current definitions, and propose its separately confirmed legacy-state migration/adoption boundary.

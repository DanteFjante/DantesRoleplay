# Application kernel Slice 11B receipt — component-type registration and legacy-schema preflight

Status: **accepted**  
Completed: 2026-08-24  
Accepted implementation: [Slice 11B](../APPLICATION-KERNEL-SLICE-11B-IMPLEMENTATION.md)

## Delivered

- Added the confirmed, authenticated `commit(kind: "system.component-type.register")` command
  without adding another tool or query kind. Its closed payload contains only `requestToken`,
  `applicationId`, `qualifiedTypeId`, `schemaJson`, and `expectedSchemaHash`.
- Added a generic component-type administration component. It derives profile, normalized schema,
  hash, version, and outcome from authoritative SQLite state; callers cannot author any of them.
- Made the generic component-type registry participate in an existing transaction while retaining
  standalone transaction behavior for earlier callers. Type-version registration and its successful
  operation audit now commit or roll back together.
- Proved exact dry-run-before-commit, immutable replay, version append, stale-hash rejection,
  old-schema rollback rejection, authorization-before-parse, and injected audit rollback through
  focused and live-MCP coverage.
- Updated capability discovery, the public commit description, component metadata, and operating
  procedure guidance so the only new public kind is discoverable and correctly documented.
- Preflighted all current `game.core.*` schema sidecars under the confirmed
  `dnd2024.game.core.*` mapping without writing component-type records: 2 are profile-compatible,
  21 reject unsupported keywords, and 9 reject as invalid JSON. `dnd2024.stats` remains absent
  because no schema was inferred.

## Evidence

- Focused component-type, ECS, authorization, catalog protocol, and live MCP checks: 21 passed,
  0 failed.
- Full shared suite: 666 passed, 0 failed. This includes EF model-drift and migration-upgrade
  coverage.
- Standalone local-AI suite: 20 passed, 0 failed.
- Catalog validation: 144 records valid; 17 existing advisory near-duplicate warnings; no live
  data touched.
- Solution build: passed with 0 warnings and 0 errors.
- `git diff --check`: passed.

## Deliberate exclusions and next gate

This slice does not expand or translate the bounded schema profile, write any legacy `dnd2024`
component contract, infer a schema for `stats`, import/backfill values, bind or migrate a state
space, publish catalog aliases/projections, execute application mechanics, or implement AI
orchestration. A future confirmed slice must choose reviewed catalog-schema rewrites or a deliberate
schema-profile expansion before component-contract adoption can write legacy types.

# Application kernel Slice 8A remediation — truthful receipts and rollback safety

Status: **accepted**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Generic application kernel dependency plan](APPLICATION-KERNEL-DEPENDENCY-PLAN.md), F / Slice 8A  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Authorization: User request on 2026-08-24 to fix Slice 7, Slice 8, and other validation problems.  
Outcome: Correct the accepted Slice 8A boundary where component removal fabricated a result revision,
and prove malformed input and exceptional rollback behavior are safe.  
Stop point: Stop after focused/full verification and durable receipt evidence. Do not begin Slice 8B,
mechanic execution, public protocol, events, legacy parity/adoption, migration, or AI work.

## Confirmed corrections

- A persisted/tombstoned entity or component value may report its resulting revision. A hard-removed
  component has no resulting revision: its receipt reports `Revision = null` and records the
  successfully removed prior revision separately as `RemovedRevision`.
- Any exception not translated into a typed effect problem—including an audit-write failure—must
  roll back and dispose the database transaction, clear tracked mutations, and then propagate.
  It must not leave state that a later `SaveChanges` call can accidentally persist.
- Batch audit metadata and nullable runtime inputs receive conservative bounds/null-safe handling.
  These are generic host limits, not application rules.

## Allowed implementation

- `src/system/ecs-effects/{domain,persistence,tests}/`
- this implementation document and the existing Slice 8A completion receipt
- link/status-only updates to the dependency plan if required

No new permanent ID, schema meaning, database table, migration, protocol command, catalog content,
or game-specific behavior is allowed.

## Acceptance evidence

- Direct tests cover consecutive ordered revisions, component hard-removal versus entity tombstone
  receipts, malformed and oversized batches, schema failure, deleted state, exact-reference and
  state-space/application isolation, cancellation, audit failure, dry run, and atomic rollback.
- Focused ECS/ECS-effect/schema/migration tests pass.
- Shared and local-AI suites, build, and `git diff --check` pass.
- The Slice 8A receipt records the corrected semantics and final command evidence.

## Result

Accepted 2026-08-24. Twelve direct ECS-effect tests and the combined ECS/projection/schema/migration
group pass. The full shared suite, standalone local-AI suite, warning-free solution build, fresh
catalog validation, and whitespace validation also pass. The accepted behavior remains inside the
Slice 8A stop point.

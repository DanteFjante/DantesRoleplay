# Application kernel Slice 8A receipt — atomic ECS effects and audit

Status: **accepted**  
Completed: 2026-08-24  
Accepted implementation: [Slice 8A](../APPLICATION-KERNEL-SLICE-8-IMPLEMENTATION.md)

## Delivered

- Added a separate `ecs-effects` system component with a closed, ruleset-neutral vocabulary for
  entity create/delete and component add/set/merge/remove.
- Exact component version/hash references, bounded batches, schema enforcement through the ECS
  owner, and optimistic revisions prevent callers from bypassing canonical state contracts.
- Added one atomic applier that executes ordered effects through the application-scoped ECS store.
  Success audit stages inside the root transaction; any later effect or audit failure rolls the
  complete batch back. Failure attempts are audited after rollback.
- Dry-run uses the real mutation path, returns prospective revisions, rolls the transaction back,
  clears tracked rolled-back state, and records non-consuming audit evidence.
- Corrected component-removal receipts: a hard-deleted component has no fabricated result
  revision, so the receipt carries `Revision = null` and the removed prior revision separately.
- Unexpected failures, including an unavailable audit writer, roll back, dispose, clear both ECS
  and partially tracked audit rows, and propagate without allowing a later save to commit them.
- Closed batch validation now bounds intent/procedure metadata and safely audits nullable runtime
  inputs. Unknown/exact-contract failures are no longer mislabeled as revision conflicts.
- The audit identity is `system.ecs.effects`. No endpoint, migration, event type, legacy adapter,
  or application mechanic contract was added.

## Evidence

- Revalidated 2026-08-24 through
  [the Slice 8A remediation](../APPLICATION-KERNEL-SLICE-8A-REMEDIATION.md).
- Focused ECS-effect tests: 12 passed, 0 failed.
- Focused ECS-effect/ECS/projection/schema/migration tests: 27 passed, 0 failed.
- Full shared suite: 530 passed, 0 failed.
- Standalone local-AI suite: 19 passed, 0 failed.
- Solution build: passed with 0 warnings and 0 errors.
- Fresh catalog validation: 144 records valid; 17 existing advisory warnings; no live data touched.
- `git diff --check`: passed; Git emitted line-ending notices only.

## Deliberate exclusions

Application action/mechanic registration, selection, projection consumption, sandbox execution,
event declaration/routing, public protocol, authorization, `dnd2024` adoption, and legacy parity
remain separate confirmed slices. Legacy and ECS state are never dual-written.

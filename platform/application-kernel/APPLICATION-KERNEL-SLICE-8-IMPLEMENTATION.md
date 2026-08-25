# Application kernel Slice 8A implementation — atomic ECS effects and audit

Status: **accepted**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Generic application kernel dependency plan](APPLICATION-KERNEL-DEPENDENCY-PLAN.md), F  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Apply a bounded list of generic entity/component ECS effects atomically inside one state space and record success/failure audit evidence.  
Exclusions: Legacy reads or writes, D&D-specific identifier or state translation, application activation, mechanic registration/execution, public protocol, projection execution, event declarations/routing, cache, or AI work.  
Allowed files/areas: `src/system/ecs-effects/{domain,persistence,hosting,tests}/`, existing ECS contracts only if a generic seam is required, data-access registration, this document, its receipt, and link/status-only dependency-plan updates. No migration or catalog content is allowed.  
Stop point: Generic ECS effects transact and audit with rollback/no-change evidence; stop before application mechanic execution, events, legacy parity, or public transport.

## Prerequisite evidence

- [Slice 6](APPLICATION-KERNEL-SLICE-6-IMPLEMENTATION.md) supplies application-scoped canonical
  ECS values but no generic ECS mutation/effect envelope.
- [Slice 7](APPLICATION-KERNEL-SLICE-7-IMPLEMENTATION.md) reads those values structurally and
  deliberately has no transaction, effect, audit, or legacy consumer.
- The existing `ActionRunner`, `EffectApplier`, `IWorldStore`, and `IProjectionResolver` own the
  old world tables and legacy mechanic requirements. They cannot consume an application state
  space or application projection without a new application execution contract.
- Application activation and declared-record parsing remain deferred; there is no authoritative
  application mechanic/action record to execute against an ECS state space.

## Confirmed ownership decisions

User confirmation, 2026-08-24, resolves the blocker as follows:

1. Slice 8A introduces a closed generic application-ECS effect vocabulary: entity create/delete
   and component add/set/merge/remove. Component effects require exact type version/hash and every
   destructive/update effect requires an optimistic expected revision. The legacy `Effect` record
   is not reused.
2. Application action/mechanic registration, selection, sandbox execution, projections, and event
   declaration remain a separate later leaf with their own authority contract.
3. `dnd2024` legacy parity belongs to application adoption and is read-only comparison evidence.
   The kernel will never dual-write legacy and ECS state.
4. The in-process audit operation identity is `system.ecs.effects`. It is not a protocol command or
   an application action ID.

## Runtime artifacts

- `IApplicationEcsEffectApplier` with a closed batch request, fixed maximum of 128 effects, dry-run
  option, typed problems, and per-effect receipts.
- Atomic application through the accepted `IEntityComponentStore` under one database transaction.
- Existing operation history records one `system.ecs.effects` success in the root transaction, or
  one failure after rollback. Audit-write failure may never leave ECS mutations committed.
- No new database table, migration, event type, endpoint, or catalog record.

## Authoritative state and closed input

SQLite ECS state, exact component type contracts, and state-space application binding are
authoritative. The caller supplies one state-space ID, intent/audit text, and at most 128 ordered
effects. Entity create supplies ID/name. Entity delete supplies expected entity revision.
Component mutations supply entity ID, exact component reference, and expected component revision;
add requires zero, set/merge/remove require the current positive revision. Only add/set/merge carry
JSON data. Callers cannot claim result revisions, bypass schema validation, select another state
space during a batch, write legacy state, or provide audit success.

## Behavior, result, and typed effects

The applier validates closed shapes, opens one transaction, applies effects in authored order so a
later effect may depend on an earlier one, and stages the success audit before commit. Any failure
rolls back the complete batch and clears tracked rolled-back state before recording one failure
audit outside the transaction. Dry-run executes the same path then rolls back and records a
non-consuming dry-run audit. The result contains applied/dry-run status, operation ID, ordered
per-effect receipts, and stable typed problems. No rules, formulas, branching, or JavaScript run.

## Failure, replay, and rollback contract

Malformed/oversized effects, unknown/deleted entities, unknown/cross-application/stale component
contracts, schema-invalid JSON, stale revisions, duplicates rejected by underlying ECS invariants,
and cancellation commit no ECS rows. Equal requests are not idempotent unless their expected
revisions make the repeated operation a no-op failure; the audit records each attempt. A failure to
stage the success audit rolls back the ECS transaction. No compensating legacy write exists.

## Acceptance matrix

| Area | Required evidence |
| --- | --- |
| Atomicity | Multi-effect success commits all effects plus one audit; a late failure commits no effect and one failure audit. |
| Ordering | Create-then-add and consecutive revision-bound component updates work in one batch. |
| Validation | Shape, bounds, schema, exact type/hash, application/state-space, deleted entity, and stale revision failures are typed. |
| Dry run | Uses the real mutation path, returns receipts, rolls back all ECS state, and records non-consuming audit evidence. |
| Isolation | A batch cannot name another state space or use another application's component type. |
| Compatibility | Existing legacy effect/action behavior and tables remain byte-for-byte untouched. |
| Repository | Focused ECS-effect tests, existing ECS/action tests, build, full suite, and `git diff --check` pass. |

## Remaining sequencing

| Leaf | Scope | Depends on | Stop point |
| --- | --- | --- | --- |
| 8A | Generic ECS effect/applier transaction and audit seam | Slice 6, generic effect confirmation | **Accepted 2026-08-24.** No application mechanic execution and no legacy adapter. |
| 8B | Application execution contract and sandbox invocation | activation/declared application records | No `dnd2024` migration or public transport. |
| 8C | Read-only `dnd2024` legacy/ECS parity fixture | 8A, 8B, application adoption | No dual write; mismatches block adoption. |

## Completion receipt and exit gate

Record evidence in `receipts/APPLICATION-KERNEL-SLICE-8A-RECEIPT.md`. Do not begin application
mechanic execution, event integration, legacy parity/adoption, public protocol, or AI work.

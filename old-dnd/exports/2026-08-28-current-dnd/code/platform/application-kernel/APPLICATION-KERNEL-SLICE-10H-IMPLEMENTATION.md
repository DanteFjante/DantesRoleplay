# Application kernel Slice 10H implementation — empty-state upgrade compatibility

Status: **accepted 2026-08-24**; [receipt](receipts/APPLICATION-KERNEL-SLICE-10H-RECEIPT.md)  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Application-kernel E/H state-space upgrade](APPLICATION-KERNEL-DEPENDENCY-PLAN.md)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Expose authenticated `system.state-space.upgrade` through `commit`; atomically move one
existing empty state space to the exact current activation while retaining immutable binding,
compatibility, replay, and audit evidence.  
Exclusions: Non-empty migration, caller-authored migration/compatibility overrides, schema or
consumer compatibility inference, application activation, document parsing/import, entity or
component mutation, legacy backfill assignment, directory mutation, remote MCP, vectors/models,
AI orchestration, and game behavior.  
Allowed files/areas: state-space-administration and ECS state-space contracts/persistence, one
additive forward-only migration/model snapshot, data-access composition/model, MCP commit/query
surface and tests, system-use procedure/component metadata, this document/receipt, and status-only
roadmap/dependency updates.  
Stop point: Stop when an authenticated exact dry run can atomically upgrade only an empty state
space to the unchanged exact active fingerprint, query back its incremented immutable binding,
replay historical create/upgrade receipts, and roll back on failure; do not migrate non-empty data.

## Confirmed decisions

- Slice 0 reserves `commit(kind: "system.state-space.upgrade")`, requires explicit auditable
  compatibility or migration evidence, and forbids application activation from silently upgrading
  existing state spaces.
- Slice 10E says declared dependency coverage is incomplete, so this slice cannot certify general
  schema/consumer compatibility. Zero entities and zero components are closed compatibility
  evidence: no persisted application state can require reinterpretation or migration.
- Slice 10G creates empty state spaces bound to exact active evidence and deliberately defers
  upgrades. Its globally unique application binding remains immutable; only its activation binding
  may advance through this transaction.
- On 2026-08-24 the user said “Continue” after Slice 10H was named as upgrade/compatibility. This
  confirms the already reserved public kind and the additive binding-revision/history schema needed
  for durable replay. It does not authorize non-empty migration policy.

## External implementation reference

No Foundry dnd5e review applies because this slice implements no game behavior. No external code or
licensed content is reused.

## Prerequisite evidence

- [Slice 10G receipt](receipts/APPLICATION-KERNEL-SLICE-10G-RECEIPT.md) proves authorized exact-
  activation creation, immutable application binding, empty-state isolation, replay, and atomic
  audit.
- [Slice 10F receipt](receipts/APPLICATION-KERNEL-SLICE-10F-RECEIPT.md) proves immutable active
  revisions/fingerprints and explicit incomplete dependency coverage.
- [Slice 6 receipt](receipts/APPLICATION-KERNEL-SLICE-6-RECEIPT.md) proves state-space isolation and
  authoritative application-scoped entity/component persistence.

## Runtime artifacts

- Extend the `state-space-administration` component with upgrade request/context/preview/receipt
  contracts and service methods. Add no second administration owner.
- Add binding revision/current-update fields to `system_state_space` and one immutable
  `system_state_space_binding_revision` history table. New public creations write revision 1;
  upgrading an older/directly-created row first retains its current binding as a baseline.
- Add `system.state-space.upgrade` to the existing commit catalog/dispatcher. The exact payload is
  `{requestToken, stateSpaceId, applicationId, activeFingerprint, expectedBindingFingerprint}`.
- Keep exact authenticated `system.applications` as the confirmation query, now exposing binding
  revision and update timestamp. Add no query kind, tool, migration script input, or state payload.

## Authoritative state and closed input

SQLite state-space/entity/component rows own current runtime state and isolation. Immutable binding
history owns historical replay evidence. SQLite activation current/history owns the only eligible
target. The application registry owns application revision identity; the operation log owns exact
dry-run, request-token, authorization, and audit evidence.

The caller supplies only request token, existing state-space ID, its immutable application ID, exact
target active fingerprint, and exact current derived binding fingerprint. It cannot supply target
application revision/fingerprint, binding revision/fingerprint, entity/component counts, dependency
coverage, compatibility result, migration code/data, principal, timestamps, or effects.

## Behavior, result, and typed effects

Private-operator `Modify` authorization runs before JSON parsing or service access. Dry run verifies
the existing application binding, expected current binding, different exact current activation,
zero entities, and zero components, then derives the next binding revision/fingerprint without
mutation. Commit requires the exact successful dry run and unchanged source/target/count evidence.

Commit retains the prior binding if absent from history, appends one immutable next binding revision
with `empty-state-compatible` evidence, atomically changes only the state-space current activation
binding/revision/update timestamp, and writes one successful audit operation. Application identity
never changes. Exact token replay returns its historical post-upgrade binding even after later
upgrades; creation replay likewise uses revision-1 history rather than current state.

## Failure, replay, and rollback contract

Closed failures cover malformed/extra fields, invalid IDs/hashes, unknown state space/application,
cross-application request, stale expected binding, missing/stale/same target activation, non-empty
state requiring migration, missing/stale dry run, token conflict, unavailable service, and
unexpected transaction failure. Internal data and migration guesses never appear.

Any state/count/activation/binding drift after dry run, audit/history/current-row failure,
cancellation, or injected exception rolls back new history, current binding, and success audit.
Failed adapters may append only ordinary failure audit. Non-empty rejection changes no current or
historical binding and never partially migrates entities/components.

## Implementation sequence

1. Extend state-space contracts/model/history/migration and creation replay; add focused baseline,
   empty-upgrade, non-empty, stale, multi-upgrade replay, and rollback tests.
2. Add authorization-first upgrade adapter, capability/dispatcher example, query summary fields,
   procedure/component metadata, and denial-before-parse tests.
3. Extend the live three-verb walk with a second activation, upgrade without dry run, exact dry run,
   commit/replay/query-back, non-empty rejection evidence, and unchanged three-tool surface.
4. Run focused tests, catalog validation, full shared/local-AI suites, warning-free build, migration
   drift checks, and `git diff --check`; record the receipt and close Slice 10 status.

## Acceptance matrix

| Concern | Required evidence |
| --- | --- |
| Positive | Empty space upgrades to exact current activation and increments binding revision. |
| Compatibility | Zero entities/components is recorded; any state returns `MIGRATION_REQUIRED`. |
| Authorization | Missing/remote context denies before invalid JSON parsing or service access. |
| Exactness | Target activation, expected binding, state counts, or derived evidence drift rejects. |
| Replay | Create and each upgrade token return their historical binding after later upgrades. |
| Rollback | Audit/history/current failure leaves current binding and history unchanged. |
| Isolation | Application ID and every entity/component remain unchanged. |
| Boundary | No migration inference/input, compatibility override, game, or AI behavior. |
| Surface | Capabilities, dispatcher, docs, guards, and three-tool walk agree. |

## Verification commands

- Focused state-space administration/upgrade, ECS isolation/effects, activation, authorization,
  migration, catalog coverage, guard, bootstrap-contract, and live MCP tests.
- `dotnet run --project DantesRoleplay.Tools -- validate catalog`
- Full `DantesRoleplay.Tests` and local-AI suites.
- Warning-free self-contained solution build, model-drift check, and `git diff --check`.

## Completion receipt and exit gate

Record acceptance in `receipts/APPLICATION-KERNEL-SLICE-10H-RECEIPT.md`, mark this document accepted,
and close the single Slice 10 owner status. Stop before non-empty migration, declared-record import,
legacy adoption/backfill, runtime application execution, or AI orchestration.

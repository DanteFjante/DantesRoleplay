# Application kernel Slice 10G implementation — exact-activation state-space creation

Status: **accepted** — [completion receipt](receipts/APPLICATION-KERNEL-SLICE-10G-RECEIPT.md)  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Application-kernel E/H state-space creation](APPLICATION-KERNEL-DEPENDENCY-PLAN.md)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Expose authenticated `system.state-space.create` through `commit`; atomically create one
empty isolated runtime state space bound to the exact currently active application overlay and
return durable replay/audit evidence.  
Exclusions: State-space upgrade or migration, compatibility decisions, application activation,
application-document parsing/import, entity/component creation, runtime effects, legacy backfill,
directory mutation, remote MCP, vectors/models, AI orchestration, and game behavior.  
Allowed files/areas: a new state-space-administration system component, the existing ECS
state-space transaction seam, application-activation reads, data-access composition only, MCP
commit/application-query surface and tests, system-use procedure/component metadata, this
document/receipt, and status-only roadmap/dependency updates.  
Stop point: Stop when an authenticated exact dry run can create one empty state space against the
unchanged exact active fingerprint, query back that immutable binding, replay its token, and roll
back all state/audit evidence on failure; do not add upgrade or compatibility behavior.

## Confirmed decisions

- Slice 0 reserves `commit(kind: "system.state-space.create")`, defines a state space as an isolated
  runtime instance bound to one exact application revision/effective manifest, and requires
  authorization, dry run, idempotency, audit, and an atomic state-space transaction.
- Slice 6 supplies the generic immutable SQLite state-space binding and application-scoped ECS
  isolation. Its pre-activation in-process creation seam is retained for internal/tests, while this
  slice becomes the authenticated public creation authority.
- Slice 10F supplies the exact current activation fingerprint and explicitly grants no state-space
  authority itself. Creation consumes that fingerprint without rescanning or changing activation.
- On 2026-08-24 the user said “Continue” after Slice 10G was named as state-space creation. This
  confirms implementation of the already reserved public kind within the existing state-space
  schema; no new migration or schema meaning is introduced.

## External implementation reference

No Foundry dnd5e review applies because this slice implements no game behavior. No external code or
licensed content is reused.

## Prerequisite evidence

- [Slice 6 receipt](receipts/APPLICATION-KERNEL-SLICE-6-RECEIPT.md) proves immutable application-
  revision/manifest bindings, state-space isolation, and the existing additive SQLite schema.
- [Slice 10F receipt](receipts/APPLICATION-KERNEL-SLICE-10F-RECEIPT.md) proves immutable exact active
  application evidence, authorization-first administration, replay, and transactional audit.
- [Slice 10C receipt](receipts/APPLICATION-KERNEL-SLICE-10C-RECEIPT.md) proves the shared closed
  administrative payload, exact dry-run, operation-token, and authorization patterns.

## Runtime artifacts

- Add a `state-space-administration` component with closed request/context/preview/receipt
  contracts and one service over activation reads, the ECS state-space registry, and operation log.
- Add `system.state-space.create` to the existing commit catalog/dispatcher. The exact payload is
  `{requestToken, stateSpaceId, applicationId, activeFingerprint, expectedFingerprint}`;
  `expectedFingerprint` must be null because creation requires absence.
- Bind the row's immutable manifest fingerprint to the exact active activation fingerprint and its
  application revision. Return a separately derived binding fingerprint for concurrency/query
  evidence without adding stored or caller-authored authority.
- Extend exact authenticated `system.applications` results with bounded state-space summaries. Add
  no new query kind, table, migration, state-space content, or public tool.

## Authoritative state and closed input

SQLite activation current/history owns the selected application overlay. SQLite application
registry owns the matching application revision. SQLite `system_state_space` owns the immutable
runtime binding. The operation log owns dry-run, replay, authorization, and audit evidence.

The caller supplies only a 32-character lowercase hexadecimal request token, bounded state-space
ID, application ID, exact current activation fingerprint, and null expected fingerprint. It cannot
supply application revision/fingerprint, preview/dependency evidence, binding fingerprint,
principal, timestamps, initial entities/components, migration data, compatibility claims, or
effects.

## Behavior, result, and typed effects

Private-operator `Modify` authorization runs before JSON parsing, ID validation, activation lookup,
or database access. Dry run validates absence and derives the binding from the current active
application without mutation. Commit requires a successful dry run for the exact canonical payload
and unchanged derived activation/application evidence.

The state-space ID is globally unique and creation requires absence. A successful commit writes one
empty immutable state-space binding and one successful operation audit in the same transaction.
Exact request-token replay returns the original binding even if another application activation is
now current. Token reuse for any other request conflicts. A new token cannot adopt an existing ID.

The returned/queryable summary contains state-space ID, application ID/revision/fingerprint, active
activation fingerprint, derived binding fingerprint, and creation timestamp. It exposes no source
paths/content and performs no ECS entity/component write.

## Failure, replay, and rollback contract

Closed errors cover malformed/extra fields, invalid IDs/hashes, non-null creation expectation,
unknown application, missing/stale activation, existing state-space ID, missing/stale dry run,
request-token conflict, unavailable service, and unexpected transaction failure. Internal
exception/database details never appear.

Activation or registration drift after dry run, duplicate ID, audit failure, state-space write
failure, cancellation, or injected exception changes no state-space or successful audit evidence.
Failed adapters may append only their ordinary failure audit. Creation never changes active
applications, source registrations/scans, projection definitions, external files, or legacy state.

## Implementation sequence

1. Add state-space administration contracts/service/composition and refactor the existing registry
   to participate safely in an ambient transaction; add focused create/replay/stale/rollback tests.
2. Add authorization-first commit adapter, capability/dispatcher example, exact application-query
   state-space summaries, procedure/component metadata, and denial-before-parse tests.
3. Extend the live three-verb walk with create-without-dry-run, dry run, commit, replay, query-back,
   stale/duplicate/remote failures, and empty-state evidence.
4. Run focused tests, catalog validation, full shared/local-AI suites, warning-free build, migration
   drift checks, and `git diff --check`; record the receipt and update owner status.

## Acceptance matrix

| Concern | Required evidence |
| --- | --- |
| Positive | Exact current activation dry-runs, creates an empty isolated state space, and queries back. |
| Authorization | Missing/remote context denies before invalid JSON parsing or service access. |
| Exactness | Activation/application drift after dry run rejects without creation. |
| Absence | Existing ID or non-null expected fingerprint rejects without adopting/rebinding it. |
| Replay | Exact token returns the original binding after active drift; token reuse conflicts. |
| Rollback | Audit or persistence failure leaves no state-space or success operation row. |
| Isolation | New spaces contain no entities/components and do not change any other state space. |
| Boundary | No upgrade, compatibility, migration, executable import, game, or AI behavior. |
| Surface | Capabilities, dispatcher, examples, docs, guards, and three-tool walk agree. |

## Verification commands

- Focused state-space administration, ECS isolation, activation, authorization, migration, guard,
  bootstrap-contract, and live MCP tests.
- `dotnet run --project DantesRoleplay.Tools -- validate catalog`
- Full `DantesRoleplay.Tests` and local-AI suites.
- Warning-free solution build, model-drift check, and `git diff --check`.

## Completion receipt and exit gate

Record acceptance in `receipts/APPLICATION-KERNEL-SLICE-10G-RECEIPT.md`, mark this document accepted,
and update the single Slice 10 owner status. Stop before state-space upgrade/compatibility, legacy
backfill, application-document import, runtime execution, or AI orchestration.

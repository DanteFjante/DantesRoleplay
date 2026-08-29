# Application kernel Slice 10F implementation — exact-preview application activation

Status: **accepted** — [completion receipt](receipts/APPLICATION-KERNEL-SLICE-10F-RECEIPT.md)  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Application-kernel D/H activation](APPLICATION-KERNEL-DEPENDENCY-PLAN.md)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Expose authenticated `system.application.activate` through `commit`; atomically retain and
activate the exact valid source-overlay preview fingerprint, redacted winner manifest, dependency
graph evidence, and durable replay receipt without creating or upgrading state spaces.  
Exclusions: Application-document parsing/import, executable authority, schema compatibility claims,
deferred mechanic/procedure/event/subscription/catalog dependency indexing, directory mutation,
state-space administration, runtime component writes, remote MCP, vectors/models, application
migration, and game behavior.  
Allowed files/areas: a new application-activation system component, application registry reads only,
application-preview and projection-impact ports, data-access composition/model plus one additive
migration/snapshot, MCP commit/query surface/adapters/tests, system-use procedure/component metadata,
this document/receipt, and status-only roadmap/dependency updates.  
Stop point: Stop when an authenticated exact dry-run can atomically switch one application's active
redacted overlay revision and audit receipt, stale/invalid/replayed/failing requests produce no
partial activation, and no state-space or executable/catalog state changes.

## Confirmed decisions

- Slice 0 reserves `commit(kind: "system.application.activate")`, requires exact successful preview
  activation, trusted authorization, expected fingerprint, idempotency, dry run, audit, and one
  atomic application-manifest switch.
- Slices 4 and 10D define the candidate application manifest as the deterministic source-overlay
  winners/shadows/problems plus the stronger full preview fingerprint. Activation retains that
  accepted boundary; it does not invent a file-internal application declaration schema.
- Slice 10E exposes exact component-field/projection dependency evidence and explicitly defers other
  consumer kinds. Activation records that graph fingerprint and incomplete coverage; it is not a
  compatibility certificate and does not make source files executable.
- On 2026-08-24 the user said “Continue” after Slice 10F was named as exact-preview activation. This
  confirms the reserved public kind and the additive generic activation persistence required to
  make the switch durable.

## External implementation reference

No Foundry dnd5e review applies because this slice implements no game behavior. No external code or
licensed content is reused.

## Prerequisite evidence

- [Slice 10D receipt](receipts/APPLICATION-KERNEL-SLICE-10D-RECEIPT.md) proves valid deterministic
  previews bind application/source/file/candidate evidence and expose only redacted metadata.
- [Slice 10E receipt](receipts/APPLICATION-KERNEL-SLICE-10E-RECEIPT.md) proves stable declared
  component-field/projection graph fingerprints with explicit incomplete consumer coverage.
- [Slice 10C receipt](receipts/APPLICATION-KERNEL-SLICE-10C-RECEIPT.md) proves authorization-first
  administration, exact dry-run recovery, operation-ID idempotency, atomic audit, and rollback.

## Runtime artifacts

- Add an `application-activation` component with closed request/context/result contracts, a reader,
  and one SQLite transaction owner over preview, dependency evidence, activation history, active
  pointer, retained source/winner evidence, receipt, and operation audit.
- Add additive generic tables for immutable activation revisions, retained source summaries,
  retained winner documents, one current pointer per application, and immutable operation-linked
  activation receipts. `Down` refuses deletion of durable activation/audit evidence.
- Add `system.application.activate` to the commit catalog/dispatcher. The exact JSON payload is
  `{requestToken, applicationId, previewFingerprint, expectedActiveFingerprint}`. Fingerprints are
  uppercase SHA-256 values; expected active fingerprint is null only when no activation exists.
- Require `dryRun: true` for the exact canonical payload before mutation. Dry run reruns preview and
  dependency inspection but changes no activation row. Commit reruns both inside its transaction;
  external file or registry drift therefore returns `PREVIEW_STALE`.
- Extend authenticated `system.applications` results with the current activation summary, if any.
- Add no application content, catalog record, active executable record, state-space, vector index,
  AI prompt, or directory write.

## Authoritative state and closed input

SQLite application/source registrations select the application and source stack. The preview
service derives current application/source/document/candidate fingerprints and redacted winners.
The dependency service derives the complete currently indexed graph fingerprint. SQLite activation
history/current pointer is authoritative only for which exact overlay evidence is active.

The caller supplies only a 32-character lowercase hexadecimal request token, application ID, exact
preview fingerprint, and expected current activation fingerprint/null. It cannot supply winner
documents, source evidence, application revision, dependency graph, activation revision/fingerprint,
coverage, principal, timestamps, content, canonical paths, compatibility claims, or state spaces.

## Behavior, result, and typed effects

Authorization for private-operator `Modify` runs before JSON parsing, fingerprint validation,
preview, or database access. Preview and commit both require a valid preview whose fingerprint
equals the request. The service derives an activation fingerprint over the exact preview evidence,
retained source/winner metadata, dependency graph fingerprint, and fixed coverage version.

The expected active fingerprint must exactly match current state. A different derived activation
appends one immutable revision and atomically changes the current pointer; an equal activation is
`unchanged` and appends no activation revision. Both outcomes receive one operation-linked receipt.
An exact request-token replay returns the original immutable receipt even after later activations.

The retained winner manifest includes only logical identity, source ID, trust, precedence, relative
logical path, media type, content hash, length, and text flag. It stores no content or canonical
path. Typed effects: only activation persistence plus its successful audit operation in one
transaction; no ECS or runtime effect.

## Failure, replay, and rollback contract

Unauthorized requests deny before payload parsing. Closed errors cover malformed/extra fields,
unknown application, invalid candidate, stale preview fingerprint, stale expected active
fingerprint, missing exact dry run, request-token conflict, unavailable service, and unexpected
transaction failure. Scanner/exception/path details never appear.

Any invalid preview, preview drift between dry run and commit, active concurrency mismatch, audit
failure, receipt failure, cancellation, or injected exception rolls back activation revision,
source/winner evidence, current pointer, receipt, and success audit together. Failed adapter calls
may append only their ordinary failure audit. Activation never mutates registrations, source scan
receipts, projection definitions, catalog content, state spaces, or external files.

## Implementation sequence

1. Add activation contracts, persistence mapping, migration, composition, and focused
   dry-run/commit/replay/stale/rollback tests.
2. Add authorization-first commit adapter, closed capability payload/example, application active
   summary, procedure/component metadata, and denial-before-parse tests.
3. Extend the live three-verb walk with valid preview-to-activation, query-back, exact replay,
   stale/remote/invalid failures, and no-state-space evidence.
4. Run focused tests, fresh catalog validation, full shared/local-AI suites, warning-free build,
   migration drift checks, and `git diff --check`; record the receipt and update owner status.

## Acceptance matrix

| Concern | Required evidence |
| --- | --- |
| Positive | Exact valid preview dry-runs, activates, and queries back retained redacted evidence. |
| Exactness | File/source/application/dependency drift after dry run rejects without active change. |
| Authorization | Missing/remote context denies before invalid JSON parsing or service access. |
| Concurrency | Null/exact current expectation is required; stale expectation changes nothing. |
| Replay | Exact token replay returns the original revision/receipt; token reuse conflicts. |
| Rollback | Audit or persistence failure leaves no activation/current/receipt success row. |
| Boundary | Activation says dependency coverage is incomplete and grants no executable authority. |
| Isolation | No state-space, registry, scan-receipt, catalog, projection, or external-file mutation. |
| Surface | Capabilities, dispatcher, examples, docs, guards, and three-tool walk agree. |

## Verification commands

- Focused application-activation, preview, dependency, authorization, migration, guard, and
  bootstrap-contract tests.
- `dotnet run --project DantesRoleplay.Tools -- validate catalog`
- Full `DantesRoleplay.Tests` and local-AI suites.
- Warning-free solution build, live three-verb JSON-RPC walk, model-drift checks, and
  `git diff --check`.

## Completion receipt and exit gate

Record acceptance in `receipts/APPLICATION-KERNEL-SLICE-10F-RECEIPT.md`, mark this document
accepted, and update the single Slice 10 owner status. Stop before application-document import,
state-space creation/upgrade, compatibility certification, application migration, or AI
orchestration.

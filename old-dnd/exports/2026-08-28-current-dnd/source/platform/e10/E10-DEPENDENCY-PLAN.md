# E10 dependency plan — durable system feedback

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Slices 1–3A are implemented and accepted. Slice 3A provides local reversible retention
staging only. Remote Slices 3B–3C remain gated by accepted E9 receipts.**
Last updated: 2026-08-21

Prototype prioritization and the future re-entry conditions are recorded in
[E10-FUTURE-DEVELOPMENT.md](E10-FUTURE-DEVELOPMENT.md). E9 and E10 remote slices are intentionally
deferred while playable game development is the priority.

## Execution rule

Slices 1–3A have landed. Any further implementation must stop before deletion, authentication,
authorization, or remote exposure unless the relevant Slice 3B/3C gates below are completed and
explicitly approved.

## Target capability

While exercising the running system, an LLM can leave one durable, concise report when behavior is
broken, misleading, unexpectedly difficult, or worth improving. A developer can retrieve the
report later with enough bounded evidence to reproduce it without relying on the original chat.

This is platform diagnostic state. It is not world state, a game event, a notification, an
operation-log substitute, or an instruction that changes system behavior.

### Included

- Append-only reports for a defect, friction, documentation mismatch, suggestion, or positive
  observation.
- Server-assigned identity/time, bounded structured content, caller-declared impact, related
  operation and frozen procedure-version references, exact retry behavior, and a standard
  operation audit.
- Bounded report reads and explicit discovery through `orient` and the capability catalog.
- A later local developer workflow for acknowledgement, disposition, notes, and export.

### Excluded

- Automatic issue filing, email/chat delivery, GitHub integration, background monitoring, model
  evaluation/scoring, sentiment analysis, crash dumps, stack-trace collection, transcripts,
  chain-of-thought, arbitrary attachments, screenshots, credentials, or hidden world snapshots.
- Letting a report veto, retry, repair, or roll back another operation; changing game state;
  converting feedback directly into a catalog or source-code edit; or treating a report as proof
  that its claim is true.
- Remote-user identity, access control, retention policy, or multi-tenant isolation. Those depend
  on E9 and deployment policy before this surface may be exposed beyond the current trusted local
  test host.

## Existing owners and why a new record is justified

| Existing owner | What it already proves | Why it does not own feedback |
| --- | --- | --- |
| `Operation` / `IOperationLog` | Every MCP call, its observable outcome, cited/read procedures, and action evidence. | An operation says what happened. It has no report category, expected behavior, reproduction, lifecycle, or stable report identity; overloading failures would also lose feedback about successful-but-confusing behavior. |
| `KNOWN_ISSUES.md` | Reviewed repository findings available to developers. | A runtime LLM cannot inspect or write repository files, and a chat finding must be reviewed before becoming repository truth. |
| Notifications | A committed rule's bounded message to a later reader. | Feedback is authored by the testing agent, is not game causality, and must not enter event/subscription processing. |
| World entities/components/events | Persistent game and campaign facts. | A complaint about the host must survive game snapshot/restore without becoming lore, mechanic input, or player-visible state. |

The proposed owner is a dedicated platform `SystemFeedbackReport` aggregate and store. SQLite is
authoritative for live reports. Repository files remain authoritative for any later fix, catalog
contract, or reviewed entry promoted into `KNOWN_ISSUES.md`; promotion is an explicit human step.

## Slice 1 semantic and public boundary — accepted

The following names and meanings were confirmed for Slice 1 and are implemented runtime
capabilities:

| Boundary | Proposed decision |
| --- | --- |
| Procedure | `procedure.system.feedback`, governing when and how an agent reports, what evidence is useful, privacy limits, duplicate/retry behavior, and recovery. |
| MCP write | One new `commit(kind: "feedback")` kind with only `operation: "submit"` in Slice 1. This extends `commit`; it does not add a fourth tool. |
| MCP read | One new `query(kind: "feedback")` kind for an exact id or bounded newest-first list. This extends `query`; it is not folded into `history`. |
| Durable id | Server-generated `feedback.<32 lowercase hex>`; callers cannot choose or overwrite it. |
| Categories | Closed values: `defect`, `friction`, `documentation`, `suggestion`, `positive`. |
| Impact | Closed caller observation: `blocked`, `degraded`, `minor`, `none`. It is evidence for triage, not developer-assigned severity or priority. |
| Retry identity | Caller-generated `feedback-request.<32 lowercase hex>` token, unique per intended report. It is not the durable report id and conveys no reporter identity. |
| Initial lifecycle | Every accepted report is immutable and `open`. Slice 1 has no edit, delete, acknowledge, resolve, dismiss, or bulk operation. |
| Context references | At most eight existing operation ids and eight existing procedure ids, stored through indexed reference rows rather than comma-separated or arbitrary JSON. References describe what the caller associated; they do not prove causation. |
| Restore/catalog behavior | Reports are outside catalog import/export and game snapshot capture/restore. A world restore cannot erase or recreate them. Developer feedback export is added only in Slice 2. |

This table remains the Slice 1 regression boundary. Slice 2 approval does not alter its report
identity, submission semantics, or MCP kinds. Moving feedback outside the local database still
requires the Slice 3 decision record below.

## Slice 1 implementation contract — implemented reference

This section is the executable handoff for Slice 1. It deliberately resolves implementation choices
that the feature semantics should not leave to inference.

### Start and stop rule

This section records the exact contract used to implement Slice 1 and remains the regression
reference. A Slice 2 pass follows the separate Slice 2 contract below; it must not reinterpret this
section as authority to recreate or redesign Slice 1, and it has no authority over Slice 3.

Terra preserves unrelated working-tree changes, reads the current files before patching, and does
not refactor adjacent tools or schemas merely to make feedback look uniform. If current code has
changed so one instruction below no longer fits, Terra amends this plan or reports the conflict
instead of inventing a second path.

### Exact Slice 1 domain shape

Create namespace `DantesRoleplay.SystemFeedback` under a new `DantesRoleplay/SystemFeedback/`
folder. Keep the entity, enums, public requests/views/results, and interfaces in this owner. Use
these types and meanings:

| Type | Required Slice 1 members |
| --- | --- |
| `SystemFeedbackCategory` | `Defect`, `Friction`, `Documentation`, `Suggestion`, `Positive`; persisted and exposed as the lowercase values in the public contract. |
| `SystemFeedbackImpact` | `Blocked`, `Degraded`, `Minor`, `None`; persisted and exposed as lowercase. |
| `SystemFeedbackState` | `Open` only. Do not pre-add later lifecycle values. |
| `SystemFeedbackReport` | `Id`, `RequestToken`, `PayloadFingerprint`, `Category`, `Impact`, `Summary`, `Observed`, nullable `Expected`, `CreatedAt`, `State`, `SubmissionOperationId`, and collections for steps and references. |
| `SystemFeedbackStep` | Integer database key, `ReportId`, zero-based `Ordinal`, and `Text`. Reproduction order is meaningful and must be preserved. |
| `SystemFeedbackOperationReference` | Integer database key, `ReportId`, zero-based `Ordinal`, and canonical 32-character `OperationId`. |
| `SystemFeedbackProcedureReference` | Integer database key, `ReportId`, zero-based `Ordinal`, `ProcedureId`, and the positive `ProcedureVersion` resolved at first acceptance. |
| `SystemFeedbackSubmitRequest` | The closed public fields from the JSON example below, except `operation`, which is MCP dispatch rather than domain data. Keep category and impact as strings until exact lowercase validation converts them to entity enums. |
| `SystemFeedbackView` | The immutable report fields, ordered steps, ordered operation ids, and `{ id, version }` procedure references. Category, impact, and state are lowercase strings in this public view. Never return EF entities or rely on default enum JSON serialization. |
| `ISystemFeedbackService` | `SubmitAsync(request, intent, proceduresUsed, cancellationToken)` and `FindAsync(id, category, impact, state, from, to, limit, cancellationToken)`. Submission results always include their audit operation id; reads do not. |

Do not add reporter identity, application/build version, database migration name, assignment,
priority, disposition, comments, attachments, or arbitrary metadata in Slice 1. Build provenance can
be added later only when the host has one authoritative build identifier; a collection of assembly
identifiers is not that contract.

### Exact Slice 1 relational schema

Add four tables through EF configuration and one forward migration:

| Table | Columns and constraints |
| --- | --- |
| `system_feedback_report` | Primary key `Id` with exact `feedback.` plus 32 lowercase hex shape; unique `RequestToken` with exact `feedback-request.` plus 32 lowercase hex shape; 64-character lowercase-hex `PayloadFingerprint` (not unique); enum text checks for category/impact/state; `Summary` length 1–200; `Observed` length 1–2,000; nullable `Expected` length 1–1,000 when present; required UTC `CreatedAt`; exact 32-character lowercase-hex `SubmissionOperationId`. Index `(State, CreatedAt, Id)`, `(Category, CreatedAt, Id)`, and `(Impact, CreatedAt, Id)`. |
| `system_feedback_step` | Integer primary key; required report foreign key with cascade delete; `Ordinal` 0–7; `Text` length 1–400; unique `(ReportId, Ordinal)`. |
| `system_feedback_operation` | Integer primary key; required report foreign key with cascade delete; required operation foreign key with restrict delete; `Ordinal` 0–7; unique `(ReportId, Ordinal)` and `(ReportId, OperationId)`; index `OperationId`. |
| `system_feedback_procedure` | Integer primary key; required report foreign key with cascade delete; `Ordinal` 0–7; `ProcedureId` length 1–200; positive `ProcedureVersion`; unique `(ReportId, Ordinal)` and `(ReportId, ProcedureId)`; index `(ProcedureId, ProcedureVersion)`. Application validation freezes an existing current version; do not add a cascading dependency that could make catalog maintenance delete feedback evidence. |

Map `SubmissionOperationId` as evidence but do not add an EF foreign key from the report to
`operation`: the coordinator writes the report and audit together, and avoiding that relationship
removes a circular insert dependency. The operation row still has the feedback id as `Subject`, so
`history(subject: reportId)` proves the link in both directions. There is no delete API in Slice 1;
the cascade rules only define database integrity for a future explicitly destructive operation.

SQLite `HasMaxLength` does not enforce length, so add database check constraints for the closed id,
fingerprint, enum, text-length, and ordinal invariants in addition to application validation. Add
`DbSet` properties and a dedicated `ConfigureSystemFeedback` method in
`DantesRoleplayDbContext.OnModelCreating`; do not mix these mappings into notifications or
operations.

### Exact validation and fingerprint rules

Validation does not silently normalize caller prose. Apply these rules before opening a write
transaction:

1. `RequestToken` must exactly match `feedback-request.` followed by 32 lowercase hexadecimal
   characters. Category and impact inputs must be one of the exact lowercase public values.
2. Every supplied string must already equal its own `Trim()` result. `Summary` and each reproduction
   step are single-line and contain no control characters. `Observed` and `Expected` may contain
   line-feed characters but no carriage return or other C0 control character; preserve their bytes
   after JSON decoding. Required strings cannot be empty.
3. Reproduction steps retain caller order. Operation and procedure id arrays must contain canonical,
   distinct values and are sorted with `StringComparer.Ordinal` before storage/output. Do not sort
   the reproduction steps.
4. Operation ids must be exactly 32 lowercase hex characters and exist in `operation`. Procedure
   ids must be exact stored ids and resolve to their current positive version. Failed operations
   are valid references. Empty optional arrays are equivalent to omission.
5. Construct one private canonical fingerprint document in this fixed property order:
   `category`, `impact`, `summary`, `observed`, `expected`, `reproductionSteps`,
   `relatedOperationIds`, `relatedProcedureIds`. Use validated string values unchanged, `null` for
   absent expected text, original step order, and ordinal-sorted reference ids. Serialize as compact
   UTF-8 JSON with `Utf8JsonWriter`, hash with SHA-256, and store lowercase hex. Exclude
   `requestToken`, report id/time/state, resolved procedure versions, intent, and procedures cited.
6. Never place caller report prose in exception messages, audit summaries, error `why`/`fix`, or
   application logs. Stable validation problems identify the field and violated bound.

These rules mean a different token with identical content is a different report; the same token and
fingerprint is an exact retry; and the same token with another fingerprint is
`FEEDBACK_REQUEST_CONFLICT`.

### Exact audit ownership and transaction algorithm

The successful submission path must **not** run inside `ToolRunner.RunAsync`. Follow the existing
transaction-owning campaign coordinator pattern:

1. `CommitTool` checks the exact closed JSON object and deserializes the domain request. Invalid
   JSON/shape uses the existing `InvalidPayloadEnvelope`, which writes the one failure audit.
2. A valid shape is passed through a thin `SystemFeedbackTools.SubmitAsync` adapter to
   `ISystemFeedbackService.SubmitAsync`. The service owns semantic validation, persistence, and its
   success/failure audit. The adapter translates its result directly to `ToolEnvelope.Success` or
   `ToolEnvelope.Failure` using the returned operation id. It never calls `ToolRunner`.
3. The service validates and fingerprints, then checks `RequestToken`. An existing equal
   fingerprint records one successful duplicate call with the existing report id as subject and
   returns it with `duplicate: true`. An existing unequal fingerprint records one failed call with
   `FEEDBACK_REQUEST_CONFLICT`; neither path changes the report.
4. For a new token, allocate `feedback.<Guid.NewGuid():N>` and `Operation.NewId()`, open one database
   transaction, re-check the token and every reference, stage the report/children, and call
   `IOperationLog.RecordAsync` with tool `commit`, the report id as subject, the supplied top-level
   intent/procedures, `consumesReadEvidence: true`, and the preallocated operation id. That
   `RecordAsync` save persists all tracked report rows and the operation inside the ambient
   transaction. Commit only after it succeeds.
5. The success audit summary is exactly bounded metadata such as
   `Recorded defect feedback '<id>' with blocked impact.` It contains no summary/observed/expected
   or reproduction text. Store that operation id in `SubmissionOperationId` before the save.
6. On validation/reference rejection, rollback if needed, clear the change tracker, and record one
   failed operation outside the aborted transaction. On cancellation, rollback with
   `CancellationToken.None`, clear, and rethrow. On an unexpected exception, the service rolls
   back, clears, records one sanitized `FEEDBACK_SUBMISSION_FAILED` operation outside the aborted
   transaction, and returns that failure result. The direct adapter never adds another audit. If
   writing the failure audit itself fails, let that exception escape; do not retry with another
   audit path.
7. Concurrent same-token inserts rely on the unique request-token constraint. If the loser receives
   that specific constraint violation, rollback/clear, reload the winner, and apply the same
   fingerprint duplicate/conflict rule. Do not catch unrelated `DbUpdateException` values as a
   duplicate.

Query uses the opposite pattern: `SystemFeedbackTools.FindAsync` is read-only and must run exactly
once through `ToolRunner.RunAsync`, because the service does not audit reads. An exact-id outcome
uses the report id as subject; a list leaves subject empty so protocol dispatch records
`query:feedback`.

### Stable Slice 1 outcomes

Use one typed problem record with `Code`, `Path`, `Reason`, and `Recovery`. Do not throw for expected
validation, reference, duplicate-conflict, or not-found outcomes.

| Outcome | Required behavior |
| --- | --- |
| New submission | Success data is `{ report, duplicate: false }`; next call is `query(kind: "feedback", id: "<report-id>")`. |
| Exact token retry | Success data is `{ report, duplicate: true }`; report bytes/children are unchanged; the retry has its own successful operation id. |
| Malformed/extra MCP payload | Existing `INVALID_PAYLOAD`; recovery starts with the literal advertised `commit(kind: "feedback", payload: ...)` call. |
| Semantically invalid field/bound | `INVALID_FEEDBACK`; `Path` names the field; recovery says to correct that field and resubmit with the same token only when the intended content is unchanged. |
| Missing operation/procedure reference | `FEEDBACK_REFERENCE_NOT_FOUND`; no report; recovery says to remove/correct the named reference or query it first. Do not disclose any referenced row's content. |
| Reused token with changed fingerprint | `FEEDBACK_REQUEST_CONFLICT`; no mutation; recovery says to use a new request token for a materially different report. Do not return the existing report prose. |
| Unexpected persistence failure | `FEEDBACK_SUBMISSION_FAILED`; no report or success audit; sanitized recovery tells the caller to inspect failure history and retry the same token after the system problem is corrected. |
| Invalid query shape/filter/time/limit | `INVALID_FEEDBACK_QUERY`; recovery begins with one valid literal feedback query. |
| Exact id absent | `FEEDBACK_NOT_FOUND`; safe recovery is `query(kind: "feedback", state: "open", limit: 50)`. |

The submission result's `OperationId` is the audit created by this call. The report's
`SubmissionOperationId` remains the first accepted submission audit and never changes on retry.

### Exact public dispatch changes

- Add `ISystemFeedbackService` as a resolved service parameter to both public dispatchers and
  register it scoped in `DataAccessServiceCollectionExtensions`.
- Add `feedback` to both `VerbSurface.QueryKinds` and `CommitKinds`. The commit kind does not support
  dry run: it is already append-only and retry-safe, and the only useful proof is the accepted
  stored report.
- Add nullable query parameter `impact`. Reuse existing `id`, `category`, `state`, `from`, `to`, and
  `limit`; update their capability descriptions where needed. Do not overload `query` text,
  `subject`, or history filters.
- Add one `IsFeedbackRequest` closed-query guard. For `feedback`, reject every parameter except
  `id`, `category`, `impact`, `state`, `from`, `to`, and `limit`. Parse exact lowercase enums and
  ISO-8601 UTC timestamps before calling the service. `from` is inclusive and `to` exclusive;
  reject `from >= to`. Default an omitted limit to 50 and reject—not silently clamp—values outside
  1–100.
- Exact-id mode accepts only `id`; list mode accepts the filters and optional limit but no id. Add
  the new `impact` argument to existing fixed-query guard signatures and require it to be null for
  every non-feedback kind, so adding the public parameter cannot leak into another query.
- Exact id returns `FEEDBACK_NOT_FOUND` when absent. Lists are ordered `CreatedAt` descending then
  `Id` using ordinal comparison. Slice 1 accepts only state `open`.
- Update the literal kind lists in both tool descriptions, the dispatch switches, capabilities
  examples, relevant guard-test expectations, and orient's one reporting next step in the same
  change. Do not add a fourth MCP tool or a feedback suggestion to every error.

### Expected Slice 1 file map

| File or area | Slice 1 change |
| --- | --- |
| `DantesRoleplay/SystemFeedback/SystemFeedback.cs` | New enums, entities, requests/views/results, and `ISystemFeedbackService`. Split only if the file becomes materially harder to read. |
| `DantesRoleplay.DataAccess/SystemFeedbackService.cs` | New validation, canonical fingerprint, read query, retry/concurrency, transaction, and audit owner. |
| `DantesRoleplay.DataAccess/DantesRoleplayDbContext.cs` | Four `DbSet`s plus isolated mappings/checks/indexes. |
| `DantesRoleplay.DataAccess/DataAccessServiceCollectionExtensions.cs` | Scoped service registration. |
| `DantesRoleplay.DataAccess/Migrations/<timestamp>_SystemFeedback.*` and model snapshot | One generated forward migration; review generated constraints/indexes rather than hand-authoring a second schema. |
| `DantesRoleplay.MCPServer/Tools/SystemFeedbackTools.cs` | Thin direct-envelope submit adapter and ToolRunner-wrapped read adapter. |
| `DantesRoleplay.MCPServer/Tools/CommitTool.cs` | Service parameter, exact payload parser, and `feedback` dispatch. |
| `DantesRoleplay.MCPServer/Tools/QueryTool.cs` | Service/impact parameters, closed request guard, parsing, and `feedback` dispatch. |
| `DantesRoleplay.MCPServer/Tools/VerbSurface.cs` | Exact query/commit specs and parameter descriptions. |
| `DantesRoleplay.MCPServer/Tools/OrientTool.cs` | One discoverability next step, only after the capability exists. |
| `catalog/procedures/system/procedure.system.feedback.md` and `catalog/manifest.json` | Canonical agent contract and manifest entry. No bootstrap duplicate. |
| `DantesRoleplay.Tests/SystemFeedbackTests.cs` | Store, validation, fingerprint, query, atomicity, retry/concurrency, and isolation tests. |
| `DantesRoleplay.Tests/VerbToolTests.cs` and `ProtocolWalkTests.cs` | Surface/dispatch expectations and cold orient → procedure → submit → query proof. |

Do not edit `KNOWN_ISSUES.md`, snapshot producers, notifications, events, effects, world models, or
catalog export/import in Slice 1 except for a test proving they remain unaffected.

### Terra execution and verification order

1. Re-read the approved boundary plus `procedure.system.modify` and `procedure.mcp.add-tool`; inspect
   the current dispatcher, operation-log, EF mapping, migration, guard-test, and protocol-walk
   patterns named above.
2. Add the domain types and focused failing tests for validation, fingerprinting, retry conflict,
   and deterministic reads. Implement the service and mappings until those tests pass.
3. Generate the `SystemFeedback` migration with the repository's EF tooling, inspect the generated
   SQL/model snapshot for all four tables, checks, indexes, and delete behaviors, then add migration
   and rollback/fault tests.
4. Add the catalog contract, exact public kinds/dispatch, DI, and orient discovery. Update all
   surface guards in the same patch; no advertised kind may exist without its dispatch case.
5. Run focused tests after each coherent change, then run these acceptance gates in order:

   - `dotnet test DantesRoleplay.slnx --no-restore --filter FullyQualifiedName~SystemFeedbackTests`
   - `dotnet test DantesRoleplay.slnx --no-restore --filter "FullyQualifiedName~VerbToolTests|FullyQualifiedName~ProtocolWalkTests"`
   - `roleplay.cmd validate catalog`
   - `dotnet test DantesRoleplay.slnx --no-restore`

6. Inspect the final diff for accidental caller-prose logging, duplicate audit paths, unrelated
   edits, generated migration drift, whitespace errors, and Slice 2/3 leakage. Write
   `platform/e10/E10-SLICE-1-RECEIPT.md` with commands and observed results, then stop for feature
   acceptance. Do not import the catalog into the persistent database unless separately requested.

If the focused test filter syntax is not accepted by the installed test runner, use two separate
filtered invocations and record the literal commands. Do not weaken or skip the tests to preserve a
single command line.

## Closed Slice 1 report shape

The proposed submission payload is:

~~~json
{
  "operation": "submit",
  "requestToken": "feedback-request.0123456789abcdef0123456789abcdef",
  "category": "defect",
  "impact": "blocked",
  "summary": "Campaign resume contradicts the active session header",
  "observed": "The resume result omitted the session that the preceding start operation returned.",
  "expected": "A fresh resume should include the one active session header.",
  "reproductionSteps": [
    "Start the reviewed campaign session.",
    "Query campaign-resume with includeSession true."
  ],
  "relatedOperationIds": ["0123456789abcdef0123456789abcdef"],
  "relatedProcedureIds": ["procedure.campaign.session"]
}
~~~

`requestToken`, `category`, `impact`, `summary`, and `observed` are required. The caller generates
one token for the intended report and reuses it only if that exact submission must be retried.
`summary` is 1–200 characters and `observed` is 1–2,000 characters. `expected` is optional and at
most 1,000 characters. There may be at most eight reproduction steps, each 1–400 characters, in
caller-observed order. Reference arrays are optional, distinct, canonically ordered on
storage/output, and bounded as above.

The payload is closed: reject nulls, unknown fields, unknown enum values, caller-supplied report
id/time/state/priority/reporter/build fields, raw operation objects, SQL, effects, event or
notification data, attachments, and values outside the limits. Text is stored as plain text and
returned as data, never rendered or executed. The procedure tells the agent to report observable
facts and a minimal reproduction, not private reasoning or a full conversation.

Exact retry uses the canonical fingerprint algorithm in the Terra implementation contract above.
Reusing the token with identical content returns the existing report; changed content conflicts;
the same content under a new token remains a distinct tester observation. The exact audit and
transaction algorithm above is authoritative for Slice 1.

## Dependency graph and slice order

~~~text
E10 durable system feedback                                      [planned parent]
├─ operation audit and SQLite transaction conventions            [implemented]
├─ three-verb query/commit capability catalog and guard tests     [implemented]
├─ procedure.system.feedback + report schema confirmation         [accepted and implemented]
├─ Slice 1: append-only submit, bounded read, and discovery        [implemented and accepted]
├─ Slice 2: local human triage history and export                  [specified; approval gate]
└─ Slice 3A: local reversible retention staging                    [implemented and accepted]
   └─ Slices 3B–3C: authorized remote access                       [blocked by E9 receipts]
~~~

| Slice | Starts only when | Objective exit |
| --- | --- | --- |
| 1. Submit and retrieve | The proposed semantic/public boundary is confirmed. | **Complete:** a fresh LLM can discover, submit, retry, and read one bounded immutable report, while every invalid/failure path leaves no partial report and no game change. |
| 2. Human triage and export | Slice 1 is accepted and the exact boundary below is approved. | A local developer can append an auditable disposition and export selected reports without editing/deleting original evidence or importing it as catalog content. |
| 3A. Local retention staging | Implemented from the approved Slice 3 decision proposal; see `E10-SLICE-3A-RECEIPT.md`. | Closed reports can be held, archived, and restored through immutable local actions; no data is deleted and MCP is unchanged. |
| 3B–3C. Remote access | Accepted E9 Slices 1–2 plus the preceding E10 receipt. | Verified principals use separately authorized submit/read then triage/export capabilities; anonymous and unauthorized calls fail closed. |

## Slice 1 — append-only submit, bounded read, and discovery

### Implementation boundary

1. Implement the exact domain, schema, validation, fingerprint, audit, dispatch, file, and test
   contracts above without adding Slice 2 lifecycle or export fields.
2. Add `procedure.system.feedback` to the authored catalog and manifest. It distinguishes routine
   validation/recovery from reportable contradictions: follow a precise `error.fix` once when safe;
   report when the documented recovery also fails, when advertised behavior and observation
   disagree, when a successful path is materially confusing, or when the agent has a concrete
   improvement observation. Reporting is never mandatory and never automatic.
3. Add a concise `orient` next step: when testing reveals a system defect, contradiction, or
   friction, read `procedure.system.feedback` and use `commit(kind: "feedback")`. Do not inject a
   feedback suggestion into every error envelope, which could turn ordinary validation failures
   into report loops.
4. Keep reports out of world description, graph/entity queries, mechanics/projections, event and
   notification routing, snapshot packages, catalog export/import, and normal `history` results.
   Their submission call remains discoverable in history by its feedback subject.

### Slice 1 acceptance matrix

| Area | Required proof |
| --- | --- |
| Happy path | A valid report persists once, returns its server id/time/open state, freezes requested operation/procedure references, and has one successful `commit` audit with redacted summary. |
| Discovery | `orient` and `query(kind: "capabilities")` describe only implemented reporting behavior. A fresh-context protocol walk can find the procedure, submit, query by id, and understand that it changed no game state. |
| Closed input | Reject missing/null/extra/wrong-type fields, bad enums, overlong text, too many/empty steps, duplicate/malformed/missing references, caller id/time/state/priority, raw logs/effects/events, and unsupported operations before insert. |
| Retry | The same token and exact payload return the original id and `duplicate: true`; the same token with changed content rejects; a new token with identical content creates a distinct report; concurrent same-token submits produce one report through a unique token constraint. |
| Ordering/query | Exact-id and bounded filter reads are deterministic, newest-first, inclusive `from`/exclusive `to`, reject limits outside 1–100, provide no free-text/SQL query, and do not expose operation details beyond supplied ids. |
| Atomicity | Inject reference, insert, audit, commit, exception, and cancellation failure; no partial report/reference or successful operation survives. A report after an unrelated failed call succeeds independently. |
| Isolation | Before/after comparisons prove no entity, component, relationship, mechanic, event, subscription, notification, snapshot, campaign/session/quest, or clock mutation; snapshot restore leaves reports untouched. |
| Privacy/safety | No report text enters operation summaries/errors/log output; text is never executed/rendered; documented size/count bounds hold; no transcript, chain-of-thought, credential, attachment, or hidden-state field exists. |
| Repository | Migration/model snapshot, DI, contract/manifest, surface/dispatch guards, focused store/MCP tests, catalog validation, full suite, protocol walk, whitespace search, and diff check pass. |

### Slice 1 stop gate

Stop when append-only submission/read and discovery are accepted. Do not add update/delete, a
developer CLI, assignment, comments, severity/priority, automatic deduplication beyond token-bound exact retry,
retention, remote exposure, issue-tracker delivery, or automatic remediation.

## Slice 2 — local human triage history and export

### Slice 2 decision record — implemented approval boundary

The following decisions resolved the blockers that previously made Slice 2 ambiguous. They formed
one approved semantic/schema boundary for the implementation recorded in the Slice 2 receipt.

| Decision | Exact Slice 2 meaning |
| --- | --- |
| Trust boundary | Triage and export are local developer commands in `DantesRoleplay.Tools`. They require filesystem/database access and are not MCP operations. E9 is not needed for this local-only boundary. |
| Lifecycle values | `open`, `acknowledged`, `resolved`, `dismissed`. These are developer dispositions, not severity, priority, truth, or remediation status. |
| Meanings | `open`: awaiting review or action. `acknowledged`: reviewed and intentionally left pending. `resolved`: reviewed and addressed or no longer reproducible. `dismissed`: reviewed and intentionally closed without a fix, including duplicate, invalid, or non-actionable reports. |
| Transitions | `open -> acknowledged|resolved|dismissed`; `acknowledged -> open|resolved|dismissed`; `resolved -> open`; `dismissed -> open`. Same-state transitions are rejected. Reopening is explicit and always targets `open`. |
| Concurrency | Every report has zero-based `TriageRevision`. A triage request supplies `expectedRevision`; one accepted disposition increments it by exactly one. Stale requests fail without appending a row or changing state. State alone is not a concurrency token because an open/close/reopen cycle can return to the same state. |
| History authority | Every accepted transition appends one immutable disposition row. `SystemFeedbackReport.State` and `TriageRevision` are indexed current projections; disposition history is the durable account of how they changed. Original report prose, evidence references, submission fingerprint/token, and first submission operation id remain immutable. |
| Reviewer identity | No reviewer/user field is stored in Slice 2. Local shell access is the temporary trust boundary; inventing a username or accepting one as an argument would create forgeable identity before E9. The required note records rationale, not identity. |
| Audit | The immutable disposition row is the Slice 2 audit record. Local CLI calls do not write MCP `Operation` rows: the existing developer tools are outside the MCP audit protocol, and without verified identity an extra operation row would add no authority. |
| Export | Export is read-only, bounded, deterministic in ordering/encoding, and never importable. It supports JSON and Markdown. It excludes request tokens and payload fingerprints, operation payloads, hidden world data, and database paths. |
| Redaction | Redaction is export-only. `--redact-ids` replaces all report prose, reproduction steps, and disposition notes for those selected ids with an explicit redacted marker while retaining ids, classification, lifecycle, timestamps, and reference ids. It never mutates the database. |
| Destruction | No edit, delete, purge, archive-by-retention, bulk transition, or automatic expiry exists in Slice 2. |
| MCP behavior | The existing `commit(kind: "feedback")` continues to accept only `operation: "submit"`. MCP may read the current state through the existing feedback query but cannot append dispositions, see developer notes, or export. |

There is no remaining design blocker inside Slice 2 if this table is accepted. The implementation
gate is satisfied by a request that explicitly says: **“Approve and implement the E10 Slice 2
boundary in `platform/e10/E10-DEPENDENCY-PLAN.md`.”** That approval covers the three new state ids,
disposition id/schema, report revision/schema change, forward migration, local `feedback` developer
command, and export format id. It does not authorize Slice 3, remote access, or deletion.

### Exact Slice 2 domain additions

Extend `DantesRoleplay.SystemFeedback` without creating a second report owner:

| Type/member | Required contract |
| --- | --- |
| `SystemFeedbackState` | Add `Acknowledged`, `Resolved`, and `Dismissed`; public names remain exact lowercase. |
| `SystemFeedbackReport.TriageRevision` | Required non-negative integer, default `0`; configure as an EF concurrency token. Existing rows migrate to `0`. |
| `SystemFeedbackReport.Dispositions` | Collection of immutable disposition rows. It is included only by administration reads, not returned as EF entities. |
| `SystemFeedbackDisposition` | `Id`, `ReportId`, `Revision`, `FromState`, `ToState`, `Note`, `CreatedAt`, and report navigation. |
| `SystemFeedbackDispositionRequest` | `ReportId`, lowercase `TargetState`, `ExpectedRevision`, and `Note`. No reviewer, priority, assignment, issue URL, or arbitrary metadata. |
| `SystemFeedbackDispositionView` | Lowercase states plus immutable id/revision/note/time. |
| `SystemFeedbackAdministrationView` | Existing report view, current `TriageRevision`, and dispositions ordered by revision. |
| `SystemFeedbackAdministrationQuery` | Optional distinct report ids plus typed category/impact/state, UTC `From`/`To`, and `Limit`; no free text, SQL, operation payload, or world filter. |
| `SystemFeedbackAdministrationFindResult` | Ordered administration views or one typed problem. |
| `SystemFeedbackTransitionResult` | Accepted administration view, or a typed problem plus current state/revision for conflict recovery. |
| `SystemFeedbackExportDocument` | Canonically ordered logical export data only. It does not know an output path and performs no file I/O. |
| `ISystemFeedbackAdministrationService` | Local-only methods with the exact signatures below. Do not inject this interface into `QueryTool`, `CommitTool`, or any MCP tool class. |

Use these service signatures so the implementation does not have to infer responsibility:

~~~csharp
Task<SystemFeedbackAdministrationFindResult> FindAsync(
    SystemFeedbackAdministrationQuery query,
    CancellationToken cancellationToken = default);

Task<SystemFeedbackTransitionResult> TransitionAsync(
    SystemFeedbackDispositionRequest request,
    CancellationToken cancellationToken = default);

Task<SystemFeedbackExportResult> BuildExportAsync(
    SystemFeedbackAdministrationQuery query,
    IReadOnlySet<string> redactedReportIds,
    CancellationToken cancellationToken = default);
~~~

`BuildExportAsync` returns `SystemFeedbackExportDocument` and performs database reads/redaction only.
`FeedbackTool` alone adds the database filename, renders JSON/Markdown, and performs atomic file I/O.

Keep `ISystemFeedbackService` as the reporting-agent boundary. It may continue returning the current
state but must not expose disposition notes or administration methods. Do not widen its submission
request.

### Exact Slice 2 relational schema

Add one forward migration that:

1. Adds `TriageRevision INTEGER NOT NULL DEFAULT 0` to `system_feedback_report`, with a check
   constraint `TriageRevision >= 0`, and expands the state check to the four persisted enum names.
   Configure `TriageRevision` as an EF concurrency token.
2. Adds `system_feedback_disposition`:
   - `Id` primary key with exact `feedback-disposition.` plus 32 lowercase hexadecimal shape;
   - required `ReportId` foreign key to `system_feedback_report` with cascade delete, defining
     integrity only because Slice 2 has no delete operation;
   - positive `Revision`, required `FromState` and `ToState` with the four-value enum check;
   - required `Note`, 1–500 characters, already trimmed, single-line, and without C0 controls;
   - required UTC `CreatedAt`;
   - unique `(ReportId, Revision)` and index `(ToState, CreatedAt, Id)`.
3. Adds database checks that `FromState <> ToState` and that ids, states, note length, and revision
   satisfy the same invariants enforced by the application.

The disposition id is server-generated as `feedback-disposition.<Guid.NewGuid():N>`. Do not add a
reviewer column, operation foreign key, soft-delete flag, external issue id, or export marker.

### Exact transition algorithm

`TransitionAsync` owns one database transaction:

1. Validate the canonical report id, exact lowercase target state, non-negative expected revision,
   and note bounds before the transaction. Preserve the accepted note exactly after JSON/shell
   decoding; do not silently trim or normalize it.
2. Begin a transaction and load the report with its current revision. Unknown id returns
   `FEEDBACK_NOT_FOUND`. A revision mismatch returns `FEEDBACK_TRIAGE_CONFLICT` with current state
   and revision but no report prose.
3. Validate the transition table above. Same-state and forbidden transitions return
   `INVALID_FEEDBACK_TRANSITION` without a disposition row.
4. Allocate the disposition id, append revision `expectedRevision + 1`, update the report state and
   revision, and save once inside the transaction. Commit only when both changes persist.
5. Treat EF concurrency failure or the unique `(ReportId, Revision)` violation as
   `FEEDBACK_TRIAGE_CONFLICT`; rollback, clear tracking, and return the winner's current state and
   revision. Do not reinterpret unrelated database failures as conflicts.
6. Cancellation rolls back with `CancellationToken.None`, clears tracking, and rethrows. Unexpected
   failures roll back and return sanitized `FEEDBACK_TRIAGE_FAILED`; no partial row/state change is
   permitted.

### Exact local developer command surface

Add one `FeedbackTool` to the explicit `ITool[]` list. Its grammar is:

~~~text
roleplay feedback list [--state <state>] [--category <category>] [--impact <impact>]
                       [--from <UTC>] [--to <UTC>] [--limit <1..1000>]
                       [--database <path>]

roleplay feedback show <feedback-id> [--database <path>]

roleplay feedback triage <feedback-id> --to <state> --expected-revision <n>
                         --note <text> [--database <path>]

roleplay feedback export <file> --format <json|markdown>
                         [--ids <comma-separated-feedback-ids>]
                         [--state <state>] [--category <category>] [--impact <impact>]
                         [--from <UTC>] [--to <UTC>] [--limit <1..1000>]
                         [--redact-ids <comma-separated-feedback-ids>]
                         [--database <path>]
~~~

Command rules:

- `list` is newest-first and prints bounded metadata only: id, created time, category, impact,
  state, triage revision, and summary. `show` prints one report and its complete disposition history.
- `triage` accepts only `acknowledged`, `resolved`, `dismissed`, or `open` as allowed by the
  transition table. It writes no output file and returns the new state/revision on success.
- `export` requires a file path and explicit format. It refuses to overwrite an existing file
  unless `--overwrite` is supplied. The implementation must resolve and display the exact target
  before writing; a failed write leaves no partially replaced destination. Write to a sibling
  temporary file. For a new destination, atomically move the temporary file into place; for an
  existing destination with `--overwrite`, use same-volume atomic replacement so a failure leaves
  the old file intact. The temporary path is server-generated in that exact destination directory,
  validated as a regular file rather than a link/reparse point, and removed after a failed write.
  `--overwrite` replaces only that exact validated file and is the sole destructive behavior in
  Slice 2.
- `--ids` and filters are intersected. Redaction ids must be a subset of selected report ids;
  unknown or unselected redaction ids fail rather than being silently ignored.
- `from` is inclusive and `to` exclusive, exact UTC ISO-8601; reject `from >= to`. Default limit is
  `100`; maximum is `1000`. Sorting is total: exports use report `(CreatedAt, Id)` ascending and
  dispositions by revision ascending. Lists use `(CreatedAt, Id)` descending.
- Exit `0` on success, `2` for invalid command/filter/id/transition input or unknown report,
  `3` for `FEEDBACK_TRIAGE_CONFLICT`, and `1` for database or output failure. Errors go to stderr
  and never echo report prose or disposition notes.

### Exact export contract

Both formats carry the same logical fields and no others:

- `schemaVersion: "dantes-system-feedback-export-v1"`;
- `sourceDatabase`: database filename only, never an absolute path;
- `sourceAsOfUtc`: maximum report/disposition timestamp in the selection, or `null` when empty;
- normalized selection filters and selected count;
- each report's id, created time, category, impact, state, triage revision, summary, observed,
  expected, ordered reproduction steps, ordered related operation ids, frozen procedure id/version
  references, first submission operation id, and ordered dispositions;
- no `RequestToken`, `PayloadFingerprint`, operation body/evidence, world/entity/component/event data,
  environment variables, credentials, stack traces, or hidden transcript/reasoning.

JSON uses UTF-8 without BOM, LF line endings, fixed property order, invariant UTC `O` timestamps,
and one trailing LF. Markdown uses a fixed heading/field order, escapes untrusted text so it cannot
create raw HTML, and uses fenced blocks for multiline report prose. Export ordering and content are
stable for an unchanged selection; no wall-clock generation timestamp is included. For ids named by
`--redact-ids`, prose/steps/notes become `[redacted from export]` and the report carries
`redacted: true`; other metadata and references remain.

### Stable Slice 2 outcomes

| Code | Meaning and recovery |
| --- | --- |
| `INVALID_FEEDBACK_ADMIN_QUERY` | Invalid filter, UTC window, limit, ids, format, or redaction selection. Correct the named CLI option. |
| `FEEDBACK_NOT_FOUND` | Exact report id does not exist. Run `roleplay feedback list`. |
| `INVALID_FEEDBACK_TRANSITION` | Transition is same-state or absent from the closed table. Re-read `show` and choose an allowed target. |
| `FEEDBACK_TRIAGE_CONFLICT` | `expectedRevision` is stale. Re-run `show`, review intervening dispositions, and retry with the current revision only if still appropriate. |
| `FEEDBACK_TRIAGE_FAILED` | Sanitized persistence failure; no report state or history row changed. Inspect the database/tool failure before retrying. |
| `FEEDBACK_EXPORT_EXISTS` | Exact output file exists without `--overwrite`. Choose another file or explicitly allow replacement. |
| `FEEDBACK_EXPORT_FAILED` | Output could not be completed atomically. Existing destination remains intact; retry after fixing the path/permission problem. |

### Slice 2 file map

| File | Change |
| --- | --- |
| `DantesRoleplay/SystemFeedback/SystemFeedback.cs` | Add states, revision, disposition entity, administration requests/views/results/interface. |
| `DantesRoleplay.DataAccess/DantesRoleplayDbContext.cs` | Configure the revision concurrency token and disposition table/checks/indexes. |
| `DantesRoleplay.DataAccess/SystemFeedbackAdministrationService.cs` | Own administration reads, transitions, selection, redaction projection, and canonical export data. |
| `DantesRoleplay.DataAccess/Migrations/<timestamp>_SystemFeedbackTriage.cs` plus designer/snapshot | Forward-only schema change. |
| `DantesRoleplay.Tools/Commands/FeedbackTool.cs` and `Program.cs` | Local-only list/show/triage/export command and explicit registration. |
| `catalog/procedures/system/procedure.system.feedback.md` and `catalog/manifest.json` | Clarify that current state is readable but triage notes/actions are local-developer-only. |
| `DantesRoleplay.Tests/SystemFeedbackAdministrationTests.cs` | Transition semantics, history, concurrency, rollback, query boundaries, deterministic export, and redaction. |
| `DantesRoleplay.Tests/FeedbackToolTests.cs`, `CatalogCoverageTests.cs`, `ProtocolWalkTests.cs` | CLI behavior, runtime-evidence classification, and proof no MCP triage surface exists. |

Do not change `QueryTool` or `CommitTool` dispatch kinds, add a fourth MCP verb, add catalog
import support, edit `KNOWN_ISSUES.md`, or touch world/events/notifications/snapshots.

### Slice 2 verification and stop gate

Run, in order:

1. `dotnet test DantesRoleplay.slnx --no-restore --filter FullyQualifiedName~SystemFeedbackAdministrationTests`
2. `dotnet test DantesRoleplay.slnx --no-restore --filter "FullyQualifiedName~FeedbackToolTests|FullyQualifiedName~SystemFeedbackTests|FullyQualifiedName~ProtocolWalkTests|FullyQualifiedName~MigrationDriftTests|FullyQualifiedName~CatalogCoverageTests"`
3. `roleplay.cmd validate catalog`
4. `dotnet test DantesRoleplay.slnx --no-restore`
5. `git diff --check` over the touched files and a targeted review proving request tokens,
   fingerprints, report prose, and notes do not enter errors/logs or unintended exports.

Write `platform/e10/E10-SLICE-2-RECEIPT.md`, then stop. Do not implement retention, deletion,
authentication, authorization, remote endpoints, issue-tracker delivery, or automated remediation.

## Slice 3 — retention and remote access

The implementation-ready decision proposal is
[`E10-SLICE-3-DECISION-PROPOSAL.md`](E10-SLICE-3-DECISION-PROPOSAL.md). It resolves the E10-specific
choices, splits local reversible retention from remote authorization, deliberately selects no hard
deletion or external delivery, and gives Terra an exact Slice 3A contract. The record's permanent
ids and schema meaning still require explicit approval before implementation.

The historical blocker analysis below remains useful evidence for why remote access cannot bypass
E9 and why deletion cannot be inferred.

Do not infer retention from local testing convenience. Once real volume is known, define backup,
export, archive/deletion, recovery, and evidence-retention needs. Any destructive removal must be
explicit, narrowly targeted, confirmed, and covered by a prior export/backup policy.

Do not expose report submit/read/triage remotely until E9 and deployment policy identify the trusted
principal, allowed scopes, rate/abuse limits, privacy classification, reviewer authority, and safe
responses. Remote access may require a separate feature if it introduces external issue tracking,
attachments, personal data, or cross-tenant routing.

### Historical blocking record for the formerly combined Slice 3

The original combined Slice 3 was not implementation-ready for the reasons below. The linked
decision proposal resolves these rows for local Slice 3A by choosing reversible archive-only
retention and no remote surface. The E9-related rows remain hard start gates for Slices 3B–3C.

| Former blocker | Resolution or remaining gate |
| --- | --- |
| E9 readiness | Not needed for local 3A. Accepted E9 Slices 1–2 remain a hard start gate for remote 3B–3C. |
| Capabilities | Five permanent feedback capability ids and one host-supplied feedback-store scope are proposed; there is deliberately no purge/policy-write id. |
| Deployment | One tenant/environment/database; future remote traffic uses the configured authenticated HTTP MCP deployment behind trusted TLS termination. |
| Reviewer authority | Remote read, triage, and export grants are distinct; triage/export require feedback-store administrator authority. Local CLI remains a separate filesystem trust boundary. |
| Privacy classification | Internal-confidential; encrypted operator volume, bounded content, no report/principal content in ordinary logs/metrics/errors. |
| Abuse/rate limits | Exact principal/store windows, export concurrency, safe retry response, and single-instance constraint are specified in the proposal. |
| Retention numbers | Closed non-positive reports: 180 days; positive: 90 days; latest close starts the clock; reopen cancels it; open/acknowledged never qualify. |
| Backup/export prerequisite | Not applicable because the selected feature has no destructive removal. A future purge must define and verify backup/restore first. |
| Deletion semantics | Reversible archive only. Hard purge, row deletion, tombstones, and automatic expiry are excluded. |
| External delivery | None. The database remains authoritative; connectors require a separate feature. |
| Monitoring | Safe numeric counters only; monitoring failure never weakens authorization or persistence safety. |
| Test evidence | Exact 3A boundary/concurrency/privacy tests and later 3B–3C authorization/rate/transport gates are recorded in the proposal. |

The approved split is Slice 3A (local reversible retention), Slice 3B (authorized remote
submit/read), and Slice 3C (authorized remote triage/export). Hard purge and external issue delivery
are excluded and require new decision records.

## Plan-quality audit

- Feedback, ordinary operation audit, game state, notifications, and repository issues have
  distinct owners: yes.
- The first slice is useful to a cold LLM and to a later developer without triage or external
  services: yes.
- Slice 1's permanent ids, schema, migration, and public surface are recorded as accepted; Slice 2
  separately names its schema and local-tool approval gate: yes.
- Invalid, duplicate, concurrent, cancelled, privacy-sensitive, and post-failure submissions have
  explicit outcomes: yes.
- The model cannot edit/delete/resolve its own report or use feedback to change game behavior: yes.

## Plan-change rule

Re-plan before adding arbitrary attachments or logs, automatic report generation, full-text search,
semantic clustering, priority scoring, remediation, external delivery, remote identity, destructive
retention, or feedback fields inside operations/world/events/notifications. Those materially change
privacy, authority, storage, or causal semantics and are not routine extensions of E10.

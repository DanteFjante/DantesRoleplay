# E10 Slice 3 decision proposal — retention staging and authorized remote access

Status: **Slice 3A implemented and accepted. This record remains the authority for its permanent
ids and schema meaning. Slices 3B–3C remain gated by the stated E9 evidence.**
Last updated: 2026-08-21

## Approval boundary

This document was approved as one planning boundary and Slice 3A was implemented. Slice 3B and 3C
have separate start gates. The approval phrase that authorized Slice 3A was:

> Approve the E10 Slice 3 decision record and implement Slice 3A only.

That approval confirms the permanent ids, retention semantics, schema additions, command surface,
and explicit exclusions below. It does not approve remote exposure, E9 implementation, hard
deletion, or an external connector.

## Decisions that close the former blockers

| Former blocker | Decided behavior |
| --- | --- |
| E9 readiness | E10 does not create identity or authorization. Remote feedback starts only after E9 Slices 1–2 have accepted receipts proving verified OIDC context, deny-default authorization, audit privacy, and transport parity. Slice 3A has no E9 dependency because it remains local CLI administration. |
| Deployment | One configured tenant and one configured environment own one database. Production remote access, when enabled later, uses only the existing HTTP MCP endpoint behind a trusted TLS-terminating reverse proxy. Direct public Kestrel exposure is unsupported. CLI remains local and is never a remote transport. |
| Capabilities | Proposed permanent E9 capability ids are `capability.system-feedback.submit`, `.read`, `.triage`, `.export`, and `.retention.read`. There is no purge or retention-policy-write capability in this feature version. Scope is one exact `feedback-store:<environment-id>` value supplied by trusted host configuration, never by tool payload. |
| Reviewer authority | Submit is distinct from read. Read does not imply triage. Triage/export/retention-read require an administrator grant for the configured feedback store. A submitter cannot review their own report unless they independently hold that administrator grant. Revoked grants deny the next request; no cached allow survives revocation. Local CLI access remains a separate trusted-filesystem boundary and does not manufacture a remote principal. |
| Principal evidence | E9 supplies `principal.<64 lowercase hex>` derived from SHA-256 over canonical issuer, tenant, and provider subject with length-delimited UTF-8 fields. Raw token, subject, email, display name, claims, roles, and credentials are never persisted in feedback data. Remote submissions and dispositions store the opaque principal id; pre-E9/local records store no invented identity. |
| Anonymous behavior | No remote feedback operation permits anonymous access. Missing, invalid, expired, wrong-issuer, wrong-audience, wrong-tenant, inactive, revoked, ambiguous, or provider-unavailable context fails closed with `AUTHORIZATION_DENIED`. The response does not reveal whether a report id exists. |
| Privacy | Feedback is classified internal-confidential operational data. Existing field bounds and the instruction not to include secrets remain mandatory. Report prose, steps, notes, and principal ids are forbidden in ordinary logs, metrics, denial messages, and operation summaries. Database and archive files must reside on an operator-managed encrypted volume; Slice 3 adds no field-level cryptography or key management. |
| Rate/abuse limits | When Slice 3B is enabled, per principal: 20 submissions per rolling 10 minutes, 120 report reads per minute, 30 triage writes per 10 minutes, and 5 exports per 10 minutes. Per configured store: 200 submissions per 10 minutes and at most 4 concurrent exports. Limits are enforced before application work, return `FEEDBACK_RATE_LIMITED`, include an integer retry-after seconds value, and never echo report content. The single-instance deployment uses bounded in-memory counters; multi-instance deployment remains unsupported until a shared limiter is selected. |
| Retention numbers | `open` and `acknowledged` reports never become archive-eligible. `resolved` and `dismissed` reports become eligible 180 days after the disposition that most recently entered that closed state. A later reopen cancels eligibility; a later close starts a new clock. `positive` reports use 90 days instead of 180. All comparisons use UTC and an explicit caller-supplied `asOfUtc` for deterministic previews/tests. |
| Holds | One report may be held or unheld through immutable retention actions. A held report is never archive-eligible. Hold and release require a 1–100 character external reference and a 1–500 character rationale; neither field may contain C0 controls or surrounding whitespace. Holds have no automatic expiry in Slice 3A. |
| Archive semantics | Archive is reversible metadata, not deletion and not a feedback lifecycle state. Archiving does not alter `State`, `TriageRevision`, report prose, references, submission audit, or disposition history. Existing MCP feedback reads remain unchanged in Slice 3A. Local list/retention commands hide archived reports by default and accept explicit `--include-archived`. |
| Backup/export prerequisite | No backup prerequisite exists because Slice 3A performs no destructive deletion. Existing deterministic export remains available. If hard purge is proposed later, it requires a new decision record defining recoverable backup, restore verification, custody, and confirmation before any purge code or capability id is created. |
| Deletion | No hard purge, row deletion, cascade invocation, tombstone, automatic expiry, or delete capability is implemented. This is a deliberate product decision, not an unresolved item. Retention means reversible archive staging only. |
| External delivery | No issue tracker, email, chat, webhook, or connector exists. The database remains authoritative. External delivery must be planned as a separate feature with a named destination and source-of-truth rule. |
| Monitoring | Emit numeric counters only: authorized/denied/rate-limited calls by capability and safe reason code, submission/triage/export failures, archive candidates, archive/restore/hold actions, and duration buckets. Never emit report ids, principal ids, prose, notes, reference ids, database paths, or tokens. Monitoring failure cannot block persistence, authorization denial, or archive safety. |

## Slice order

~~~text
Slice 3A — local reversible retention staging                 [implemented and accepted]
└─ Slice 3B — E9-authorized remote submit/read                [blocked on accepted E9 Slices 1–2]
   └─ Slice 3C — E9-authorized remote triage/export           [blocked on 3B evidence]

Hard purge and external delivery                              [not part of Slice 3]
~~~

## Slice 3A — local reversible retention staging

### Domain and persistence contract

Extend `SystemFeedbackReport` with these current projections:

- `RetentionRevision: int`, required, zero-based, default `0`, nonnegative EF concurrency token;
- `ArchivedAt: DateTime?`, UTC when present;
- `HoldState: SystemFeedbackHoldState`, exactly `None` or `Held`, stored as Pascal-case text.

Add the append-only `SystemFeedbackRetentionAction` entity:

- `Id`: `feedback-retention.` plus 32 lowercase hexadecimal characters;
- `ReportId`: required canonical feedback id and cascading foreign key definition; no delete route;
- `Revision`: positive integer, unique with `ReportId`;
- `Action`: exactly `Archive`, `Restore`, `PlaceHold`, or `ReleaseHold`;
- `FromArchived` and `ToArchived`: required booleans;
- `FromHoldState` and `ToHoldState`: required closed enum values;
- `Reference`: nullable for archive/restore; required 1–100 characters for hold/release;
- `Note`: required 1–500 characters for every action;
- `EffectiveAsOf`: UTC eligibility time for archive; null for other actions;
- `CreatedAt`: server UTC acceptance time.

Strings must already equal `Trim()`, be single-line, and contain no C0 or DEL controls. An action
must change exactly one projection: archive/restore changes archive state without changing hold;
place/release hold changes hold state without changing archive state. Each accepted action appends
one row, increments `RetentionRevision` by exactly one, and updates the matching report projection
in the same transaction. Original reports, dispositions, and retention actions are immutable.

Database checks and indexes:

- checks for canonical action id, positive revision, closed action/state values, changed projection,
  UTC-managed non-null timestamps, and `RetentionRevision >= 0`;
- unique `(ReportId, Revision)`;
- index `(Action, CreatedAt, Id)`;
- index report `(ArchivedAt, State, Category, CreatedAt, Id)` and `(HoldState, State, CreatedAt, Id)`.

### Closed transition table

| Action | Required current projection | Result |
| --- | --- | --- |
| `archive` | not archived, not held, current state `resolved` or `dismissed`, and eligible at supplied `asOfUtc` | set `ArchivedAt` to server acceptance time |
| `restore` | archived | set `ArchivedAt` to null |
| `place-hold` | hold state `none` | set hold state `held`; allowed whether archived or not |
| `release-hold` | hold state `held` | set hold state `none`; does not automatically archive |

Same-projection and absent-table transitions fail. Archive eligibility is recalculated inside the
write transaction; a prior preview is informative and never authorizes a later write.

### Administration service

Add provider-neutral local administration types in `DantesRoleplay/SystemFeedback/` and implement
them in `DantesRoleplay.DataAccess/SystemFeedbackRetentionService.cs`:

~~~csharp
Task<SystemFeedbackRetentionFindResult> FindEligibleAsync(
    SystemFeedbackRetentionQuery query,
    CancellationToken cancellationToken = default);

Task<SystemFeedbackRetentionTransitionResult> TransitionAsync(
    SystemFeedbackRetentionRequest request,
    CancellationToken cancellationToken = default);
~~~

`SystemFeedbackRetentionQuery` contains typed optional category/state filters, `asOfUtc`,
`includeArchived`, and limit. `asOfUtc` is required, UTC, and no more than five minutes in the
future relative to the service clock. Default limit is 100; maximum is 1000. Eligibility results
sort by `(EligibleAt, ReportId)` ascending and contain metadata only: report id/category/impact/state,
closing time, eligible time, archived time, hold state, retention revision, and summary. They do not
contain observed/expected prose, steps, disposition notes, request tokens, or fingerprints.

Every transition requires canonical report id, exact lowercase action, nonnegative
`expectedRetentionRevision`, and note. Archive additionally requires `asOfUtc`; hold/release require
reference. Load, re-check, append, projection update, save once, and commit in one transaction.
Map only EF concurrency and `(ReportId, Revision)` uniqueness races to a safe conflict containing
current retention revision/projection but no report prose.

### Exact local command surface

Extend the existing `roleplay feedback` tool:

~~~text
roleplay feedback retention eligible --as-of <UTC>
    [--category <category>] [--state <resolved|dismissed>]
    [--include-archived] [--limit <1..1000>] [--database <path>]

roleplay feedback retention archive <feedback-id> --as-of <UTC>
    --expected-retention-revision <n> --note <text> [--database <path>]

roleplay feedback retention restore <feedback-id>
    --expected-retention-revision <n> --note <text> [--database <path>]

roleplay feedback retention place-hold <feedback-id> --reference <text>
    --expected-retention-revision <n> --note <text> [--database <path>]

roleplay feedback retention release-hold <feedback-id> --reference <text>
    --expected-retention-revision <n> --note <text> [--database <path>]
~~~

There is no bulk mutation, automatic scheduler, policy editor, purge, or delete command. Each write
names exactly one report and returns its new archive/hold projection and retention revision. Local
`feedback list` gains `--include-archived`; `show` includes retention projection and immutable
retention actions. Existing MCP query/commit shapes do not change.

Exit codes remain `0` success, `2` invalid/not found/ineligible, `3` stale revision, and `1`
database failure. Errors never include report summary/prose, disposition notes, or hold rationale.

### Stable Slice 3A outcomes

| Code | Meaning |
| --- | --- |
| `INVALID_FEEDBACK_RETENTION_QUERY` | Invalid UTC as-of, filter, limit, id, reference, note, or action. |
| `FEEDBACK_NOT_FOUND` | Exact report id does not exist. |
| `FEEDBACK_RETENTION_INELIGIBLE` | Report is open/acknowledged, held, already archived, or has not reached its exact eligibility time. |
| `INVALID_FEEDBACK_RETENTION_TRANSITION` | Restore/hold transition is absent from the closed table. |
| `FEEDBACK_RETENTION_CONFLICT` | Expected retention revision is stale; refresh before retrying. |
| `FEEDBACK_RETENTION_FAILED` | Sanitized persistence failure; no action or projection changed. |

### Slice 3A file map

| File | Change |
| --- | --- |
| `DantesRoleplay/SystemFeedback/SystemFeedback.cs` | Retention projection, action entity, requests/views/results/interface. |
| `DantesRoleplay.DataAccess/DantesRoleplayDbContext.cs` | Action table, checks, indexes, concurrency token. |
| `DantesRoleplay.DataAccess/SystemFeedbackRetentionService.cs` | Eligibility and single-report transitions. |
| `DantesRoleplay.DataAccess/Migrations/<timestamp>_SystemFeedbackRetention.cs` | Atomic forward migration and snapshot. |
| `DantesRoleplay.Tools/Commands/FeedbackTool.cs` | Local retention commands and archived visibility option. |
| `catalog/procedures/system/procedure.system.feedback.md`, manifest | Explain local archive/hold semantics and unchanged MCP behavior. |
| `DantesRoleplay.Tests/SystemFeedbackRetentionTests.cs`, `FeedbackToolTests.cs` | Policy clocks, transitions, conflicts, privacy, CLI behavior. |
| `DantesRoleplay.Tests/CatalogCoverageTests.cs`, `ProtocolWalkTests.cs`, `MigrationDriftTests.cs` | Runtime-evidence classification, no MCP mutation, atomic migration. |

### Slice 3A acceptance

Tests must prove both 90/180-day boundaries immediately before/at/after eligibility, re-open and
re-close clock reset, held exclusion, release without implicit archive, archive/restore, stale and
ABA revision conflicts, same-state rejection, transaction rollback, cancellation, deterministic
ordering, limit/filter validation, no content in errors/logs, no operation-log writes, and no MCP
retention fields/actions. Run focused tests, catalog validation, migration application/drift, the
protocol walk, full suite, and `git diff --check`. Write `E10-SLICE-3A-RECEIPT.md` and stop.

## Slice 3B — E9-authorized remote submit/read

Start only when accepted E9 receipts prove the security profile above. Route the existing feedback
submit/read operations through the shared E9 interceptor using the permanent capability ids and
configured feedback-store scope. Add nullable opaque `SubmittedByPrincipalId` to reports for new
remote submissions; existing/local rows remain null. No payload accepts a principal or scope.

Authorization and rate limits run before report lookup, validation that could reveal data,
idempotency lookup, operation success audit, or persistence. Equivalent unauthorized exact-id and
list reads return the same safe denial. E9 owns authorization-decision audit; feedback owns only the
accepted report. Do not add remote triage/export or expose retention metadata. Write a 3B receipt
and stop.

## Slice 3C — E9-authorized remote triage/export

Start only after 3B transport/privacy evidence is accepted. Use `.triage` and `.export` capabilities;
administrator grant is required. Remote dispositions store nullable opaque `ActorPrincipalId` and
`ActorKind = RemotePrincipal`; local dispositions retain null with `ActorKind = LocalDeveloper`.
Remote export always uses the Slice 2 redaction contract, is limited to 100 reports, is streamed as
the selected format, and never accepts a server filesystem path or overwrite flag. Retention
metadata/actions stay administrator-only under `.retention.read`. Write a 3C receipt and stop.

## Explicitly outside this proposal

- hard purge, database row deletion, tombstones, backup/restore packages, automatic expiry;
- policy editing, arbitrary retention durations, hold expiry, bulk mutations, scheduled jobs;
- multi-instance or multi-tenant databases, shared distributed rate limiting;
- attachments, screenshots, transcripts, semantic clustering, scoring, automatic remediation;
- GitHub/issue tracker, email, chat, webhook, or any external delivery.

Any one of these requires a new decision record and approval.

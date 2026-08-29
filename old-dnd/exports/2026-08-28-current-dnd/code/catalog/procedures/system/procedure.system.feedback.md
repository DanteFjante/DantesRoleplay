---
id: procedure.system.feedback
category: system
name: Report feedback about the current system
governs: commit(kind: "feedback"), query(kind: "feedback")
status: active
---

## Description

Use this only to record an observed problem, confusion, friction, improvement idea, or positive
result in the current system while testing it. A report is durable and its original submission is
append-only. It does not change the game world, create an event, notify anyone, or cause work to
happen automatically.

## Instructions

1. When a call fails, first follow the concrete `error.fix` once if it is safe and applicable.
2. Submit feedback when that recovery also fails, when the advertised behavior differs from what
   happened, or when a successful outcome still produced material friction or a concrete
   improvement idea. Reporting is optional; never invent a complaint merely to fill a report.
3. Call `commit(kind: "feedback")` with `operation: "submit"`, a new request token, the category
   and impact, a short factual summary, and what was observed. Add the expected result and the
   smallest observable reproduction when they help someone verify it later.
4. Cite only operation ids and procedure ids already returned by the system. Do not include hidden
   reasoning, full transcripts, credentials, tokens, private data, or copied tool internals.
5. Read reports with `query(kind: "feedback")`. Use the returned id for an exact report; list
   filters are category, impact, state, and an ISO-8601 UTC `[from, to)` time window. This read
   surface can show the current state, but it cannot triage a report or reveal triage notes.

## Input

`requestToken` is exactly `feedback-request.` plus 32 lowercase hexadecimal characters. Reuse the
same token only to retry the exact same report. `category` is one of `defect`, `friction`,
`documentation`, `suggestion`, or `positive`; `impact` is `blocked`, `degraded`, `minor`, or
`none`. `summary` and `observed` are required. `expected`, up to eight reproduction steps, up to
eight operation ids, and up to eight procedure ids are optional.

## Recovery

- `INVALID_FEEDBACK`: correct the named field and retry with the same request token.
- `FEEDBACK_REFERENCE_NOT_FOUND`: query the cited record and use an id that exists.
- `FEEDBACK_REQUEST_CONFLICT`: keep the existing report and use a new token for a genuinely new
  report.
- `FEEDBACK_SUBMISSION_FAILED`: retry the exact request token once the system is available.

## Local developer review

Local developers may use `roleplay feedback list`, `show`, `triage`, `retention`, and `export` against a
database file. This is deliberately outside MCP: the local shell is the review boundary until
reviewer identity and remote authorization exist. Triage changes only the current state and adds
an immutable rationale note; it requires the report's displayed `triageRevision` to avoid stale
updates. The states are `open`, `acknowledged`, `resolved`, and `dismissed`.

Exports are local, read-only JSON or Markdown artifacts. They are not catalog input and never
include request tokens, payload fingerprints, operation payloads, database paths, or hidden world
data. `--redact-ids` replaces report prose, reproduction steps, and triage notes in the export
only; it does not modify the report.

`roleplay feedback retention` is also local-only. Closed `resolved` and `dismissed` reports become
eligible for reversible archival 180 days after their most recent closing disposition; `positive`
reports use 90 days. Open or acknowledged reports never become eligible. A local hold prevents
archival until explicitly released. Archive/restore and hold/release append immutable retention
actions and require the displayed retention revision. Archive does not change triage state, delete
data, alter MCP reads, or cause a remote action. There is no purge, bulk retention action, policy
editor, scheduler, or automatic expiry.

## Constraints

- Be factual, specific, and minimally reproducible; a report is evidence for later testing, not
  a conversation.
- No MCP caller can triage, see triage notes, export, archive, hold, delete, purge, or deliver a
  report remotely. There is no automatic retention workflow or automatic response.

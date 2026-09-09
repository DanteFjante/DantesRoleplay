---
id: procedure.system.feedback
category: system
name: Report feedback about the current system
governs: commit(kind: "feedback"), query(kind: "feedback")
status: active
createdBy: "seed"
changeNote: "Seeded from bootstrap file."
---

## Description
Use this only to record an observed problem, confusion, friction, improvement idea, or positive
result in the current system while testing it. A report is durable and its original submission is
append-only. It does not change the game world, create an event, notify anyone, or cause work to
happen automatically.

## Matches

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

## Constraints
- Be factual, specific, and minimally reproducible; a report is evidence for later testing, not
  a conversation.
- No MCP caller can triage, see triage notes, export, archive, hold, delete, purge, or deliver a
  report remotely. There is no automatic retention workflow or automatic response.

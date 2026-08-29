# Session Feature S3 Slice 1 validation receipt

Status: **Accepted**
Recorded: 2026-08-21

## Implemented boundary

- Added the permanent append-only vocabulary `game.core.campaign.session-recap`. Its first-fixture
  schema is exactly S0's `session.s0.c3-only.v1` chapter, arc, and up-to-five milestone fields.
  C3 event ids are deliberately excluded.
- Added the closed `commit(kind: "campaign")` operation `validate-session-end`, with exactly
  `operation`, `sessionId`, and `expectedStatus`. Campaign scope and every recap fact are derived;
  the caller cannot submit campaign identity, recap text, source data, or extra fields.
- `CampaignSessionEndValidator` resolves the full active session graph through S2 and composes a
  fresh C3-only deterministic recap. It rejects malformed session scope/lifecycle, an existing
  recap, a stale non-active session, or incomplete/malformed C3 context.
- The public validation result exposes only derived session/campaign identity, preview presence,
  sorted section keys, and next action. It never returns the recap data itself. No recap component,
  lifecycle replacement, structural event, notification, or external owner mutation is performed.

## Evidence

- `SessionFeature1Tests`: 4/4 passed. The new fixture starts an S1 session, commits an independent
  C3 chapter advance, derives the chapter/arc/milestone recap, proves the event id is absent, proves
  no session recap or lifecycle mutation, and confirms a fresh resolver derives the same preview.
- Session/C3/verb-surface/protocol-walk regression selection: 14/14 passed.
- `roleplay validate catalog`: passed with 257 valid records. It emitted two existing-content
  near-duplicate advisories for concurrently added character-profile contracts; no live data was
  touched.

## Deferred to Slice 2

`end-session`, lifecycle replacement, recap persistence, historical recap readback, and atomic
event/audit rollback remain unavailable. Slice 2 must re-run this resolver within its root
transaction; a Slice 1 preview is never reusable authorization or cached close input.

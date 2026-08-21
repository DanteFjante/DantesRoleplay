# Session Feature S3 Slice 2 validation receipt

Status: **Accepted**
Recorded: 2026-08-21

## Implemented boundary

- Added `end-session` under the existing `commit(kind: "campaign")` surface. Its only accepted
  caller data is `operation`, `sessionId`, and `expectedStatus: "active"`; it repeats the Slice 1
  C3 resolver after beginning the shared database transaction.
- A successful end derives exactly two effects in canonical order: add the immutable
  `game.core.campaign.session-recap`, then replace the complete session lifecycle with retained
  `ended` status and the original ordinal. The campaign scope relationship remains untouched.
- Added trusted-host-only `query(kind: "session-recap", id: "session.*")`. It accepts no filters,
  reads only a validated ended session graph, and returns the bounded immutable factual recap with
  derived session/campaign identity. It is distinct from active-session resume and generic history.
- Failure, cancellation, stale/replayed end, malformed graph, and invalid recap paths produce no
  lifecycle/recap partial write. S2's active resume returns `NO_ACTIVE_SESSION` once close commits.

## Evidence

- `SessionFeature1Tests`: 5/5 passed. It verifies cancellation leaves no recap, a C3 continuity
  change is captured without event id, end produces exactly two structural events, the lifecycle is
  retained as ended, a fresh recap reader succeeds, active resume refuses the closed session, replay
  is unchanged, and `session-recap` rejects an extra filter.
- Affected campaign/session/verb/protocol selection: 15/15 passed.
- `roleplay validate catalog`: passed with 266 valid records. It emitted two near-duplicate
  advisories for procedure contracts; no live data was touched.
- Full suite: 548/548 passed.

## Deferred boundary

No checkpoint/restore, reopen/archive/purge, participant control, gameplay action wrapping,
narrative artifact, player-facing audience, generic recap search, or external owner mutation was
added. C5/CH14 must explicitly replace the trusted-host recap audience before exposure to players.

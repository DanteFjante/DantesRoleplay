# Session Feature S0 ratification — first trusted-host continuity fixture

Status: **Ratified planning boundary; no runtime artifact is created**  
Ratified: 2026-08-21

## Decision

The first session capability is one trusted-host session for the disposable C3 fixture campaign
`campaign.test.sealed-observatory`, constructed by `CampaignFeature3Tests` from
`world.feature-01.fixture`. It is a reusable contract fixture, not catalog-authored campaign state
and not a promise of a player-facing session.

The only required current context source is C3's fixed `query(kind: "campaign-resume")` result for
that active campaign. C4/Q3 quest context, C5 audience filtering, separate world/location/clock
projections, characters, items, participants, gameplay, and all raw entity/event/history reads are
deliberately absent from S1–S3's first fixture.

## Owner map and bounded context

| Section | Source/owner | First-fixture rule |
| --- | --- | --- |
| Session header | C8 session record and its one campaign scope link | Future S1/S2 exposes only `sessionId`, derived `campaignId`, `status`, and append-only `ordinal`. It stores no copied campaign/world/context field. |
| Current context | C3 `CampaignResume` / `procedure.campaign.chapter` | Consume the existing bounded campaign result as-is: campaign identity, title, premise, goals, tone/boundaries, world ID, current chapter, current arc, C3 references, and recent milestones. No raw components or alternate session copy. |
| Quest/objective | C4/Q3 | Omitted until both owners are globally accepted and C4 approves a bounded composition. |
| World beyond C3 | World owner | Omitted. The C3 world ID and its already-bounded references are sufficient for this fixture. |
| Character/item/participant | Their owners and S5 | Omitted. No roster, identity, or control claim. |
| Audience | C5/identity | Trusted host only. Descriptive visibility is not authorization, and no player/human-safe view exists. |

The current-context result is unavailable, not substituted from chat/cache, when the campaign is
inactive, C3 cannot produce its fixed resume, or the session's sole campaign scope is malformed.
Repeated records retain their C3 canonical order. There are no session-local omission messages,
redaction rules, or source overrides in this first fixture.

## Future C8 lifecycle policy

These semantics authorize C8/S1 planning only; they do not create an ID, component, procedure,
mechanic, or public operation today.

- A session is scoped to exactly one active campaign. At most one `active` session may exist per
  campaign; unrelated active campaigns are independent.
- The first identity policy is host-proposed canonical `session.*` IDs. The future start request is
  exactly `{ operation: "validate-session" | "start-session", campaignId: "campaign.*", sessionId: "session.*" }`.
  The host never supplies status, ordinal, context, time, audience, checkpoint, summary, effects,
  links, audit/event data, or retry behavior.
- `validate-session` is zero-effect. `start-session` creates only the C8-owned session entity, complete active
  lifecycle state, and its one campaign-scope link in one transaction. A fresh host derives it
  from those records; no campaign-side current-session field is allowed.
- A second start while an active session exists returns `ACTIVE_SESSION_EXISTS` unchanged. An ID
  collision returns `SESSION_ID_TAKEN` unchanged. Cancellation, timeout, guard/reaction/event, or
  audit failure rolls back every proposed session artifact. Recovery is an ordinary scoped-entity
  inspection after C8 publishes the session component; no retry may reopen or overwrite state.
- The normal lifecycle is `active` to immutable retained `ended`. S1 starts only; S2 reads only;
  S3 alone plans normal ending and its factual record. Archive, deletion, correction, reopen,
  parallel active sessions, and retention/purge are out of scope.

## Factual closure policy

S1 stores no summary. S2 composes only current C3 context. At S3 close, the immutable factual
record uses `protocolVersion: "session.s0.c3-only.v1"` and the C3-bounded snapshot of: current
chapter (`id`, `status`, `title`, `partyQuestion`), current arc (`id`, `status`, `title`,
`partyStake`), and at most five C3 milestones in C3 order. Campaign/world scope is derived from
the durable session link and is not duplicated in recap data. Missing required C3 chapter/arc
blocks close; an empty milestone collection is `[]`. Generated prose, chat, events/audit IDs,
raw components, quest state, and hidden data are forbidden.

## Checkpoint and interruption policy

S0 selects **evidence-only checkpoint policy**. S1–S3 create no checkpoint, snapshot bytes,
restore operation, automatic repair, database/file backup, or rollback. S4 owns the first named
checkpoint reference after a generic snapshot/provenance owner and declared scope exist. A host
interruption leaves an already committed active session available to S2, a committed ended session
available to S3 historical read, and an interrupted root wholly committed or wholly rolled back.

## Transaction and fresh-host evidence

C8 is the future session transaction owner and must use the established effect/event/audit path.
Its start/end roots write only their session-owned records plus ordinary structural evidence; no
special session event or notification is authorized by S0. Failure audit follows the established
root policy and is never copied into session state.

The acceptance fixture is: create the disposable C3 campaign; validate/start one session; make one
separately governed C3 continuity change; end through S3; open a fresh host; and read the retained
session plus C3-derived context. It must prove no transcript/cache dependency and no world, quest,
character, item, clock, or rules mutation caused by the session lifecycle.

## Next leaf

Amend C8/S1 from this ratification, then implement only the session shape and zero-effect
validation slice. S2 remains blocked until that S1 slice is globally accepted. Any added owner
projection, player-safe output, checkpoint/restore behavior, or second active session requires an
S0 amendment first.

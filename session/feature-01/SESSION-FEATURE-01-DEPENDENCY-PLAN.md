# Session Feature S1 dependency plan — start one active campaign session

Status: **Slices 1–2 accepted: focused, catalog, and full-suite validation pass.**  
Last updated: 2026-08-21

## Execution rule

This plan follows AGENTS.md, `procedure.system.create-feature`, `procedure.system.modify`, `procedure.mcp.add-tool`, the [Session Operations Plan](../../SESSION_OPERATIONS_PLAN.md), [S0](../feature-00/SESSION-FEATURE-00-DEPENDENCY-PLAN.md), [Campaign Feature C8](../../campaign/feature-08/CAMPAIGN-FEATURE-08-DEPENDENCY-PLAN.md), and [Campaign Creation Plan](../../CAMPAIGN_CREATION_PLAN.md). C8 remains the runtime owner. Slice 1 implements only the confirmed vocabulary and zero-effect readiness validation; Slice 2 remains prospective.

## Target capability

A trusted host can validate and start exactly one new active game session for one eligible campaign. Starting creates one session entity/record and its campaign scope link in one root transaction, establishes no gameplay state, and returns a bounded session identity ready for S2 resume. A campaign cannot acquire a second active session; a failed/replayed/stale start leaves no session, link, event, notification, or success audit.

The first fixture is one S0-ratified campaign under the chosen one-active-session policy. It is a reusable campaign-session lifecycle contract, not a global singleton, player login, party roster, encounter, browser connection, checkpoint, world-clock advance, or automatic recap.

### Included

- One campaign-scoped session identity and minimal lifecycle state sufficient to represent `active` now and `ended` later in S3.
- One governed `validate`/`start` path through the confirmed C8/session operation surface, one-active-session guard, campaign scope/lifecycle checks, and transaction/audit/event behavior.
- Readback of the new session identity/status and bounded handoff to S2; no inferred recap/context beyond the approved C3 projection availability check.
- Canonical ordering, collision/stale/replay/corrupt-state/cancellation/timeout/rollback/fresh-host evidence.

### Excluded

- Resume, end, recap, summary field writes, checkpoint/snapshot/restore, interruption repair, archival/retention, fork, or any closed-session behavior (S2–S4).
- Party or character roster, player identity/control, participant readiness, campaign attendance, browser session/cookie, chat transcript, voice/video, collaboration, or authorization implementation (S5/S8/S9 and CH14).
- Changing campaign chapter/arc, quest/objective, world/faction/clue/location/clock, character/item state, encounter, rules action, event reaction, or generic activity history.
- A second active session for a campaign, raw caller effects/components/links/audit fields, direct database writes, arbitrary session IDs after the S0 identity policy is confirmed, or a new MCP tool/kind without separate confirmation.

## Ownership and state boundary

| Concern | Authoritative owner and S1 rule |
| --- | --- |
| Campaign eligibility/scope | Campaign root/attachment owner. S1 requires exactly one eligible campaign resolved through its existing identity; it stores no copied campaign ID in session data. |
| Session identity/lifecycle record | C8/S1. A session is a distinct campaign-scoped entity with its minimal session component; it is not a field added to the campaign root or an MCP/HTTP connection. |
| Session-to-campaign scope | A directed Campaign-owned relationship. It is the sole durable scope proof; session component data carries no campaign/world/character ID. |
| One-active-session invariant | C8 session resolver/guard. It inspects the full campaign-scoped session set and rejects zero/multiple/malformed/cross-scope ambiguity rather than choosing one silently. |
| Chapters/arcs, quest context, audience context | C3 is the only required first-fixture projection. C4/Q3 quest context and C5 audience context are omitted by S0; S1 makes no context copy or lifecycle change. |
| Start/resume/end transport | Existing `commit`/`query` surface and the future `procedure.campaign.session`; no new kind/tool is presumed. |
| Root effects/events/audit | Existing ActionRunner/Campaign transaction owner. The root operation/audit correlation remains history, not a session component field. |

S1 owns neither a “currentSessionId” campaign field nor a campaign-side session array. Those would duplicate the scoped relationship and create two active-session truths. The active session is derived from the campaign's linked session records whose session lifecycle is `active`.

## Proposed permanent vocabulary — confirmation required

| Role | Proposed ID and boundary |
| --- | --- |
| Session lifecycle component | `game.core.campaign.session`, attached only to a session entity. Complete data is proposed as `{ status: "active" | "ended", ordinal: positive integer }`; `ended` is reserved for S3. It contains no campaign/world/character IDs, timestamps, recap, checkpoint, transcript, audience, source, operation, or gameplay data. |
| Campaign scope relationship | `game.core.campaign.has-session`, directed from campaign to session with empty data. A session has exactly one such incoming campaign link. |
| Governing procedure | `procedure.campaign.session`, extending C8's required contract with closed start/resume/end behavior. |
| Start transaction owner | `CampaignSessionStarter`, the C8 compiled campaign workflow that returns only the new entity/component/link effects after all guards validate. It reuses the existing C2/C3 transaction pattern; a sandbox mechanic would require an action-selection surface that S1 intentionally does not expose. |

Confirm the IDs, direction, component schema, session-ID policy, ordinal allocation, campaign eligibility state, event/audit names, and C8 parent scope before authoring. If Campaign already has a generic lifecycle-record or scoped-child primitive that satisfies these invariants, C8 must reuse it instead of creating duplicate vocabulary.

### Identity and ordering decision

S0 must choose one permanent identity policy before S1 implementation:

1. **Host-proposed canonical session ID** (recommended for deterministic fixture/replay): input validates a new permanent ID and collision; a repeated start is rejected unchanged and the host reads the existing session.
2. **C8-derived session ID:** the root derives a collision-safe canonical ID from a confirmed allocator. The allocator's replay/collision and audit behavior must be specified before writing.

`ordinal` is a campaign-local, append-only display/order value, allocated from the current complete scoped session set inside the root transaction. It is not a timestamp, a total play-time counter, or a concurrency substitute. Sessions are ordered by ordinal ascending, then canonical session ID; an ordinal collision or gap that violates the confirmed history rule is corrupt state and blocks. If S0 selects a different stable ordering source, revise this proposed schema before implementation.

## Closed request/result boundary

The final request uses the S0-confirmed session identity policy. Under the recommended host-proposed policy it is exactly:

~~~text
{
  operation: "validate-session" | "start-session",
  campaignId: canonical existing campaign entity ID,
  sessionId: canonical proposed new session entity ID
}
~~~

No request accepts a world ID, chapter/arc/quest/character/item ID, active-session flag, ordinal, time, checkpoint, summary, audience, player identity, raw component/link/effect, audit/event, retry, or transaction field. Missing/null/empty/non-object/unknown/duplicate fields, malformed/colliding ID, unsupported operation, wrong campaign, inactive/archived campaign, unavailable S0-required context, existing active session, or corrupt linked-session state fails before effects.

`validate-session` performs every campaign/session/context/readiness check and returns zero structural effects. `start-session` repeats every check in the root transaction and cannot consume a cached validation result. A successfully repeated request returns a stable `session-already-exists`/`active-session-exists` correction according to S0; it never creates another session or treats a browser retry as permission to reopen one.

Canonical success output contains only `campaignId`, `sessionId`, `status: "active"`, `ordinal`, `resumeAvailable: true`, and literal `nextAction`. It contains no factual recap, raw projection, source/history/audit ID, event ID, ownership claim, player data, or assertion that gameplay has started. The subsequent S2 resume call obtains context through owner projections.

## Resolution and transaction rules

1. Resolve exactly one campaign and verify its lifecycle/scope through the Campaign owner. Read the S0-ratified C3/C4/C5 readiness sources only as required; failure never causes S1 to create or mutate them.
2. Validate the proposed/derived session identity and inspect all linked session records. Reject two active records, malformed component/link, a session linked to multiple campaigns, a session with no campaign, noncanonical/duplicate ordinal, or a conflicting pre-existing ID before proposing effects.
3. Require no current active session for the campaign. An ended session is historical and does not block a new start; an unclosed/interrupted active session invokes its S0-confirmed recovery path rather than being overwritten.
4. For `validate`, return the canonical zero-effect result. For `start`, create the session entity, add its complete active lifecycle component, and create its one campaign scope relationship in confirmed canonical order. No world/quest/character/item/clock/recap/checkpoint effect appears.
5. The C8 root commits the exact bundle and ordinary structural event/audit as one transaction. Guard/reaction/event/notification/audit failure, cancellation, or timeout rolls back the entity, component, and relationship together. Failure audit follows existing policy only.
6. Query fresh session and campaign projections after commit. A fresh host derives the sole active session from the scope link/component, not a cache or prior `start` result.

If the platform cannot enforce one active child session under concurrent starts inside the shared root transaction, S1 is blocked on the generic campaign-scoped uniqueness/locking guard. It may not implement a pre-query-then-write race or a campaign-side cached flag.

## Dependency graph and slices

~~~text
S0 ratified continuity fixture
├─ verified C3 campaign continuity and C4/C5 readiness requirements       [campaign/context gates]
├─ confirmed C8 component/link/identity/ordinal vocabulary                 [semantic gate]
├─ campaign-scoped active-session uniqueness under root transaction        [concurrency gate]
└─ existing action/event/audit composition                                 [shared root gate]
   ├─ Slice 1: session vocabulary, scoped reader, and zero-effect validate
   └─ Slice 2: atomic start and fresh-host readback
      └─ S2 resume context and S3 end/recap
~~~

### Slice 1 — session shape and validation

**Prerequisites:** S0 receipt accepted; C3/C4/C5 prerequisite status meets the ratified fixture; permanent IDs/identity/ordering/one-active policy are confirmed.

1. Add the confirmed component, scope relationship, procedure, read projection, and zero-effect validate resolver.
2. Validate eligible/ineligible campaign, identity collision, no-active versus one/multiple active, malformed/cross-scope/dangling session state, ordinal policy, required-context unavailability, and no-write behavior.
3. Test that session reads return only session-owned fields and cannot mutate/expose raw campaign/world/quest/character/item state.
4. Run focused tests and `roleplay validate catalog` after catalog work.

**Exit:** implemented. An eligible C3 campaign yields a deterministic zero-structural-effect start preview; an existing active session is rejected, and neither path creates a session artifact. The generic commit operation record is protocol history only. Broader corrupt/cross-scope coverage and Slice 2 remain pending global acceptance.

### Slice 2 — atomic start fixture

**Prerequisites:** Slice 1 accepted; root transaction and campaign-scoped concurrency guard are proven; event/audit contract is confirmed.

1. Add the C8 start transaction owner and apply exactly entity creation, lifecycle component add, and scope relationship create in the confirmed order.
2. Start one S0-ratified campaign session, query it and the campaign back, then repeat from a fresh host to prove derivation of the sole active record.
3. Inject failures at entity/component/link, guard/reaction/event/notification/audit, cancellation, and timeout boundaries. Test duplicate/replayed/concurrent start, ID collision, campaign lifecycle/scope change, corrupt prior session, and rollback/no external owner mutation.
4. Run focused tests, `roleplay validate catalog`, full suite at acceptance, and a protocol walk only if the C8 action/query registration changes.

**Exit:** implemented locally. One eligible campaign has exactly one new active session with durable scoped identity; cancellation, replay, and an already-active campaign leave no partial session or unrelated state. Broader injected guard/reaction/timeout and concurrent-start tests remain acceptance work.

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| One-active invariant | A campaign with no active session can start one. A campaign with one/multiple/malformed active records cannot start another; no record is silently selected, repaired, or overwritten. |
| Scope integrity | Each session has exactly one campaign scope link; cross-campaign, dangling, reverse, duplicate, or copied campaign scope fails unchanged. |
| Lifecycle isolation | Start creates only session entity/component/link and its own event/audit. It changes no chapter, arc, quest, world, clock, location, character, item, rule, recap, or checkpoint. |
| Identity/order | Identity collision/replay is deterministic; ordinal is canonical and append-only under the confirmed policy. Caller cannot supply status/ordinal or force a retry to reopen state. |
| Readiness | S0-required C3/C4/C5 context is checked through its owners. Missing, stale, hidden, unavailable, or invalid context blocks with a recovery call and is never copied or repaired. |
| Atomic failure | Failure/cancellation/timeout at every transaction boundary leaves no session entity/link/component/event/notification/success audit and no external-owner mutation. |
| Fresh continuity | A fresh host finds the active session solely from the campaign scope relationship and session lifecycle state; no chat/cache/browser state is required. |
| Boundary | S1 grants no resume content, end/recap, checkpoint, restore, player control, gameplay action, or web write capability. |

## Evidence and change control

The implementation receipt records the S0 ratification, confirmed vocabulary/identity policy, C3/C4/C5 readiness sources, canonical request/result fixtures, component/link readback, fresh-host proof, concurrency/replay/rollback/cancellation/timeout cases, catalog validation, full-suite result, and protocol walk where applicable. It does not contain a transcript, recap prose, player/account data, external-state copies, raw effects, or root operation IDs in session state.

Amend S1 before adding a second active session, resume/end behavior, recap/checkpoint/restore fields, time/duration, participant/player data, session-owned activity, archival/retention, a new audience/public surface, a browser write, or multi-host coordination. Those belong to S2–S9, C8/S4, Campaign C5, CH14, Website/API, or dedicated concurrency/deployment work.

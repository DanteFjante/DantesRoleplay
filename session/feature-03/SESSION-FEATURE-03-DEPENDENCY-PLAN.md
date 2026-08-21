# Session Feature S3 dependency plan — end with a factual continuity recap

Status: **Planned; implementation awaits accepted S0–S2, confirmed C8 recap schema, and every selected owner projection/readiness contract.**  
Last updated: 2026-08-20

## Execution rule

This is a planning-only repository artifact. It follows AGENTS.md, `procedure.system.modify`, `procedure.mcp.add-tool`, the [Session Operations Plan](../../SESSION_OPERATIONS_PLAN.md), [S0](../feature-00/SESSION-FEATURE-00-DEPENDENCY-PLAN.md), [S1](../feature-01/SESSION-FEATURE-01-DEPENDENCY-PLAN.md), [S2](../feature-02/SESSION-FEATURE-02-DEPENDENCY-PLAN.md), and [Campaign Feature C8](../../campaign/feature-08/CAMPAIGN-FEATURE-08-DEPENDENCY-PLAN.md). C8 owns the concrete lifecycle/summary implementation; S3 writes no runtime artifact.

## Target capability

A trusted host can validate and end exactly one active campaign session. Ending reads the S0-ratified owner projections at the transaction boundary, creates one immutable bounded factual recap on that session, changes only the session lifecycle from `active` to `ended`, and records ordinary root evidence. A later fresh host can inspect the closed session’s factual continuity record while S2 continues to compose current context only for an active session.

The first fixture is one S1 active session with at least one separately governed committed play change. It proves a reusable factual closure contract; it is not an AI-written story, chat transcript, event/history mirror, campaign snapshot, restore point, chapter/quest transition, or historical truth replacement.

### Included

- One guarded `active→ended` session transition through the confirmed C8 action surface.
- One append-only session recap component/record containing only the S0-approved bounded factual closure fields and source/projection references or versions needed to interpret them.
- Owner-projection validation at close, canonical field/record order, closed missing/omitted/denied behavior, and historical readback of the recap.
- Atomic lifecycle/recap/event/audit handling, stale/replay/corrupt-state/cancellation/timeout/rollback/fresh-host evidence.

### Excluded

- Start/resume current-context behavior, checkpoint/snapshot/restore/fork, interrupted-session repair, archive/purge/reopen, participant roster/control, gameplay orchestration, or player-facing table controls (S1/S2/S4–S9).
- Mutating any chapter/arc, quest/objective/evidence, world/faction/clue/location/clock, character/item/resource, combat/encounter, or ruleset action merely because the session ends.
- Free-form host/AI recap input, generated in-world prose, transcript, raw event/audit list, arbitrary history query, copied component data, hidden fact, secret, player identity, or browser write.

## Ownership and recap boundary

| Concern | Authoritative owner and S3 rule |
| --- | --- |
| Active/ended session lifecycle | S1/C8 `game.core.campaign.session` state. S3 is the only planned normal `active→ended` writer; it does not reopen or archive. |
| Factual recap record | C8/S3. It is immutable session-owned closure metadata, not an owner of world/campaign/quest/character/item/rules truth. |
| Campaign/chapter/arc source | C3 bounded resume projection. S3 derives approved closure fields; it cannot transition or copy its raw state. |
| Quest/objective source | C4/Q3 bounded projection. S3 records only S0-approved facts/references and never raw objective/evidence state. |
| World/knowledge/character/item source | Approved World/C5/Character/Items projections. Unavailable/denied values follow S0 safe fail/omit rules; S3 never uses cached/chat substitutes. |
| Factual versus narrative recap | S3 stores deterministic, source-bound factual closure only. S7 alone may later attach attributed narrative artifacts; narrative never changes S3 data. |
| Checkpoint/restore | S4/C8 snapshot owner. S3 may reference a separately confirmed checkpoint only if S0 selected it; it neither creates nor restores one. |
| Event/audit/history | Root ActionRunner/Campaign owner. Audit is proof of closing; operation ID/event list is not copied into recap data. |

The recap is a bounded historical statement about the close boundary. It may preserve approved concise factual values or references from owner projections, but those values never become editable authority. If later owner state differs, its current projection remains authoritative; S3’s recap remains an immutable record of what the confirmed closure composer observed. It must never pretend to explain every action or event during the session.

## Proposed permanent vocabulary — confirmation required

| Role | Proposed ID and boundary |
| --- | --- |
| Recap component | `game.core.campaign.session-recap`, attached once to an ended session entity. It is append-only and absent while active. |
| Recap data | Exact schema is S0-confirmed. It must contain `protocolVersion`, canonically ordered bounded factual sections, and source/projection reference/version evidence; it may not contain a campaign/world/character ID field, raw component/relationship, transcript, prose, audit/event ID, checkpoint bytes, player identity, or raw effect. |
| Governing procedure | `procedure.campaign.session`, extended with closed `end`/historical-recap behavior. |
| End mechanic | `mechanic.game.core.campaign.session.end`, a root C8 planner that creates the recap and replaces the complete lifecycle state in one transaction. |

Confirm IDs, complete recap fields/limits, source-reference format, source-read ordering, safe omission versus blocking policy, historical read route, lifecycle replacement details, and C8 audit/event behavior before writing. If an existing generic immutable receipt component meets the exact boundary, C8 may reuse it only after proving it preserves session-specific ownership and no duplicated data.

## Factual recap input/result boundary

The end request is intentionally closed and carries no recap content. Proposed shape:

~~~text
{
  operation: "validate" | "end",
  sessionId: canonical existing active session entity ID,
  expectedStatus: "active"
}
~~~

The campaign scope is derived from the session’s one scope relationship; callers cannot choose a campaign, source, summary fields, audience, checkpoint, participant, final state, or raw data. Missing/null/extra/non-object/unknown operation, malformed ID, non-active/ended/replayed session, absent/multiple/corrupt campaign scope, stale expected status, invalid C8 lifecycle, unapproved/malformed owner projection, or disallowed unavailable/denied field fails before effects.

`validate` resolves the exact closing recap and returns zero effects. `end` repeats all resolution in the root transaction and cannot reuse a cached preview. A valid success returns only `sessionId`, `campaignId`, `previousStatus: "active"`, `currentStatus: "ended"`, `recapPresent: true`, sorted recap section keys, and literal `nextAction`. It returns no recap source text, raw field payloads, chat, event/audit IDs, changed-owner claim, or permission assertion.

The historical recap read is separate from S2 current active-session resume. It accepts only a confirmed session identity/history route, applies C5/CH14 audience rules when they exist, and returns the immutable bounded recap projection. It does not transform an ended session into a new active resume context.

## Resolution and transaction rules

1. Resolve exactly one session entity, its complete `active` lifecycle component, one valid campaign scope link, and its S0-approved source inventory. Reject ended, missing, duplicate, dangling, cross-campaign, malformed, or ambiguous session state.
2. Resolve each approved owner projection at the close boundary in the confirmed canonical order. Validate projection versions, scopes, bounds, and audience. Apply S0’s explicit rule for unavailable values: block, or emit the one safe omission/reason form. Do not infer facts from operation history or an earlier S2 response.
3. Build the complete canonical recap object deterministically from these bounded projections/references. A host/AI supplies no prose, fact, delta, entity list, event, or post-processing instruction. If a required fact cannot be represented through the confirmed schema, it is a plan amendment, not an extra JSON key.
4. For `validate`, return the normalized recap preview with zero effects. For `end`, repeat source resolution inside one C8 root transaction.
5. Apply exactly the immutable recap component add and complete session lifecycle replacement `active→ended` in the confirmed order. The campaign scope link stays intact. No external owner effects are present; a referenced checkpoint is not created or restored.
6. Emit/record only the confirmed root structural event/audit after commit. Failure at source read, recap validation, component/lifecycle effect, guard/reaction/event/notification/audit, cancellation, or timeout rolls back both recap and end state. A later end/replay cannot create a second recap.

If owner projections may change concurrently while close resolves, the root must either read a version/fingerprint guard approved by those owners or fail stale and require a fresh validation. It may not write a recap assembled from incompatible point-in-time projections.

## Dependency graph and slices

~~~text
S0 factual field/omission/audience policy + S1 active session + S2 source composition
├─ C3/C4/C5/World/Character/Items approved close-boundary projections       [owner gates]
├─ confirmed immutable recap data and historical read vocabulary             [semantic gate]
├─ compatible source version/fingerprint or stale-close guard                [consistency gate]
└─ C8 root event/audit transaction                                            [shared gate]
   ├─ Slice 1: deterministic recap resolver and zero-effect end validation
   └─ Slice 2: atomic recap add + active→ended transition + historical readback
      └─ S4 checkpoint/restore and S7 narrative artifacts
~~~

### Slice 1 — factual recap validation

**Prerequisites:** S0–S2 accepted; all ratified source projections/versions and fail/omit behavior are verified; recap schema and close consistency strategy are confirmed.

1. Add the confirmed recap vocabulary and zero-effect closing resolver; no active session becomes ended yet.
2. Compose canonical preview from exact owner projections and reject noncanonical/missing/unauthorized/stale/corrupt source inputs before effects.
3. Test a valid fixture with one independently committed play change, no-change session, required/optional unavailable source, denial/redaction, wrong/ended/corrupt session, source version race, output bounds/order, and zero effects/events/audits.
4. Run focused tests and `roleplay validate catalog` after catalog work.

**Exit:** one active session has a deterministic, source-bound factual recap preview with no session or external state mutation.

### Slice 2 — atomic end and historical recap

**Prerequisites:** Slice 1 accepted; lifecycle/recap effects, event/audit, and concurrency guards are confirmed under the C8 root.

1. Add the end mechanism with exactly recap add and complete lifecycle replacement in the agreed order.
2. End the fixture, query session/campaign/recap historical projection back, and prove a fresh host sees the immutable recap while S2 refuses to treat it as active context.
3. Inject failure at source/version check, recap add, lifecycle set, guard/reaction/event/notification/audit, cancellation, and timeout boundaries. Test stale/duplicate/replayed end, corrupt recap/lifecycle/scope, changed source, rollback, restore/readback, and no external owner mutation.
4. Run focused tests, `roleplay validate catalog`, full suite at acceptance, and protocol walk only if session read/action registration changes.

**Exit:** one active session closes exactly once with one immutable bounded factual recap; every failed/replayed/concurrent attempt leaves session and external owner state unchanged.

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Close transition | Exactly one valid active session becomes ended and gains exactly one recap. Ended/replayed/stale/ambiguous sessions fail unchanged. |
| Recap truth | Every recap section is deterministically derived from a confirmed owner projection/reference at close. It is a historical closure record, never an editable copy or replacement authority. |
| No free text | Caller/AI cannot submit a summary, transcript, event list, delta, source, or extra field. Narrative prose is a later S7 artifact with separate attribution. |
| Source consistency | Missing/stale/corrupt/cross-scope/denied projections follow the confirmed block/omit rule. Concurrent incompatible source change fails stale rather than producing a mixed recap. |
| Isolation | Ending writes only C8 session recap/lifecycle state and its root evidence; it changes no campaign/world/quest/character/item/rules/action state or checkpoint. |
| Atomicity | Recap, `active→ended`, events/notifications, and success audit commit together or roll back together on every failure/cancellation/timeout path. |
| Fresh history | A fresh host can inspect the immutable historical recap through the confirmed bounded route. S2 resumes only active sessions from current owner state. |
| Boundary | S3 does not restore/fork, create a player view/roster, run gameplay, archive/purge/reopen, or expose generic history/search. |

## Evidence and change control

The implementation receipt records the S0 recap schema/owner map, source version/fingerprint strategy, canonical validate/end fixtures, independent committed-play example, historical/fresh-host readback, stale/replay/corrupt/denied/rollback/cancellation/timeout cases, external-state preservation comparison, catalog validation, full-suite result, and protocol evidence. It contains no transcript, free-form narrative, secrets, raw source projections, player data, raw effects, or operation IDs in recap state.

Amend S3 before adding recap prose, caller/AI text, delta/activity/event history, checkpoint creation/restore, session reopening/archive/purge, participant data, player views/controls, search/filtering, external state mutation, or a new read/action kind. Those belong to S4–S9, C5/CH14, Website/API, snapshot owner, or `procedure.mcp.add-tool` confirmation.

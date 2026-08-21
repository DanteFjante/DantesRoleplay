# Session Feature S8 dependency plan — player-safe session view and bounded table controls

Status: **Planned; blocked by a real authenticated audience-policy capability, accepted CH14 player control, C5/C8-approved session projection, and the Website/API semantic-write decision.**  
Last updated: 2026-08-20

## Execution rule

This is a planning-only repository artifact. It follows AGENTS.md, `procedure.system.modify`, `procedure.mcp.add-tool`, the [Session Operations Plan](../../SESSION_OPERATIONS_PLAN.md), Campaign C5/C8, S5/S6, CH6/CH14, the [Website/API plan](../../WEBSITE_AND_API_PLAN.md), and the authorization/audit/action-owner contracts. It writes no runtime artifact.

S8 is a policy-gated consumer composition, not a new session authority. It first gives an authenticated participant one bounded, read-only view of the active session that contains only records each existing owner approves. Only after the read boundary is proven can its interface invoke one already player-authorized, session-compatible action through the existing CH14/S6/action path. A table control is never a direct browser/database/MCP write or a generic session command.

## Target capability

An authenticated player who currently controls an active character enrolled in one active session can open one fixed session-table view for that campaign. The server authenticates transport context; C5 and CH14 authorize campaign/character scope before any projection; C8 supplies the session lifecycle summary; the character owner supplies only that player’s safe character reading; and the UI renders a stable, bounded result.

After the read-only fixture is accepted and Website/API permits semantic writes, the same participant can use one visible control to invoke one separately classified player-safe action already admitted by CH14 and S6. The control submits only the action owner’s closed request; it does not edit session, roster, narrative, campaign, or game data. The normal action root handles mechanics, audit, failure, and refresh.

The first fixture is one authenticated principal, one active campaign, one active session, one S5-enrolled active character with an active CH14 control grant, one fixed player-safe view, and—later—one existing player-safe session action. It proves enforcement and transport parity, not player administration, a party roster, live chat, a tactical UI, or general browser gameplay.

### Included

- One fixed, policy-gated participant projection with an active-session identity/state, owner-approved player-safe campaign context, and only the caller’s allowed character/session participation status.
- Deny-before-projection rules, server-rendered/read API consumer parity where accepted, XSS-safe rendering, post-commit invalidation/refresh only after existing successful operations, and fresh-host/stale-page proof.
- One later UI/MCP consumer control that delegates a pre-classified CH14/S6 action unchanged, returns its ordinary safe result, and refreshes from authoritative reads.
- Explicit error/empty/ended/revoked/retired/no-session states; no client-created persistent state.

### Excluded

- Authentication, account/session/token handling, administrator roles, campaign audience policy, character control grants, roster mutation, attendance/presence/ready state, session start/end/checkpoint, narrative publication, or notification semantics.
- Direct browser writes, arbitrary commands/actions/mechanic IDs/effects, game-rule execution in UI, local optimistic game state, an action queue/retry/replay, or a generic “session control” endpoint/tool.
- Party-member discovery, other-character sheets, raw world/quest/hidden source data, raw history/audits, transcripts, activity feed, chat, maps, combat/turn controls, remote exposure, or live multi-user synchronization.

## Ownership and authorization boundary

| Concern | Owner and S8 rule |
| --- | --- |
| Principal authentication and campaign audience | Identity/authorization capability and C5. S8 receives a trusted context and a policy result; audience/visibility is never a request field or client-side filter. |
| Player-to-character control | CH14. S8 requires the active control grant before character-specific data or a control is resolved; it never stores principal/character mappings. |
| Session lifecycle, factual recap, roster | C8/S1–S5. S8 reads only an approved active-session projection and cannot mutate lifecycle, summary, participant links, or checkpoint evidence. |
| Campaign/world/quest and narrative content | C5/C8/S7 plus each source owner. S8 receives a pre-redacted bounded projection only; it does not compose raw data or decide what is safe. |
| Character projection | CH6/CH14. The view contains only the caller’s owner-approved safe character summary, never another participant’s sheet. |
| Player action and session correlation | CH14, S6, and the action owner. S8 delegates one closed semantic action only after all owners opt in; it does not select a mechanic or root transaction. |
| HTTP/MCP/UI delivery | Website/API and adapters. They call one internal policy/query/action service; no browser-to-database/MCP path exists. |
| Audit/events/SSE | Existing action/audit/notification owners. S8 renders fresh data after committed invalidation and creates no parallel UI history. |

S8 denies before session/campaign/character data is projected. It must not rely on obscured IDs, route guards, a hidden button, cached page data, caller-supplied `playerId`/`characterId`/`audience`, a roster link, or descriptive GM/party labels. Revocation, retirement, session end, roster change, or policy revision requires a new authorization decision on every view/control request.

## Proposed public boundary — confirmation required

S8 deliberately proposes no durable session component, relationship, procedure, mechanic, or state transition. It should first consume an owner-approved fixed read model and existing action contract.

| Need | Confirmation boundary |
| --- | --- |
| Participant view | C5/C8 must either extend an existing bounded campaign/session projection or approve one named fixed participant-view query. The exact kind/route/DTO is intentionally undecided; it must take no caller-selected audience, component list, entity list, history range, or arbitrary session ID. |
| Character selection | CH14 resolves the caller’s controlled active character from trusted principal and campaign scope. The view request never accepts a character/principal/control-grant ID. |
| Table control | The exact action is owned by CH14/S6/the selected action owner. It retains its existing closed input and cannot gain a generic S8 wrapper or `session-control` kind. |
| HTTP page/API | Website/API decides server-rendered page, fixed read endpoint, semantic action endpoint, error envelope, loopback/authenticated exposure, and SSE resource vocabulary. S8 must not choose routes unilaterally. |

Confirm audience roles and revocation point; active-session and S5 participation semantics; the fixed field allowlist and omission/error behavior; player-safe character field policy; policy-revision/cache rule; page/API exposure; action classification and subject binding; error redaction; refresh/invalidation behavior; accessibility; and audit correlation before implementation. A new public kind/tool/route requires the appropriate owner plus `procedure.mcp.add-tool`/Website/API confirmation.

## Closed participant view and control boundaries

The fixed participant view identifies its campaign from the authenticated route/context only where the policy permits it. Its closed result may contain, after owner approval:

- campaign/session display identity and active/ended state only;
- one bounded C5 party-safe campaign-context section;
- one caller-owned CH14-authorized character summary and session-participation indicator;
- a small fixed list of declared, available controls by stable semantic label—not mechanic IDs, audit IDs, or capability internals; and
- standard literal next-state/error information.

It must omit other roster members, control grants, player identity, hidden campaign/world/quest data, source components, checkpoints, raw recap/audit/action history, rules/mechanic metadata, exact authorization policy, and all data unsupported by an owner projection. If no active session, no enrolled controlled character, or no authorization exists, return a safe fixed empty/denied state rather than trying to discover alternatives.

For the later control, S8 passes the transport-derived principal context and current campaign/session scope to the existing CH14/S6 action entry. It never accepts authorization claims, `sessionId`/`characterId` overrides, arbitrary action/mechanic IDs, raw effect/outcome payloads, queue/retry flags, or UI state as action truth. CH14 authorizes before projection/selection and S6 validates active session/roster before the action root; all failures retain their normal no-effect semantics.

## Dependency graph and slices

~~~text
Identity + authorization policy + C5 audience projection                 [missing security roots]
├─ CH14 active principal-to-character control grant
├─ S5 enrolled active character + C8 active-session projection
├─ owner-approved fixed participant field allowlist
├─ Website/API authenticated read delivery and error/SSE policy
│  └─ Slice 1: read-only policy-gated participant view
└─ S6 session-compatible + CH14 player-safe action classification         [later control leaf]
   └─ Website/API semantic-write decision and shared action adapter
      └─ Slice 2: one delegated table control with authoritative refresh
         └─ S9 live/concurrent collaboration remains separate
~~~

### Slice 1 — fixed read-only participant view

**Prerequisites:** verified identity/authorization context; accepted C5 policy semantics; accepted CH14 control grant; accepted S5/C8 active-session projection; approved field/redaction allowlist; Website/API read exposure decision.

1. Compose the one fixed view server-side through C5/C8/CH14/character owner projections, with policy checked before each projection boundary.
2. Render/read it through the approved MCP/HTTP consumer path with server-rendered fallback where applicable; encode all dynamic content as data, not executable markup.
3. Test anonymous/expired/revoked/wrong-campaign/wrong-audience/no-control/no-session/not-enrolled/retired/archived/ended and malformed/stale request cases. Prove denial before leakage and no other-character or hidden data in every result.
4. Test policy revision and session/roster/lifecycle changes against stale pages/caches; use only post-commit invalidation plus fresh authorized reload.

**Exit:** one authorized participant can obtain one bounded, useful active-session view; every non-authorized or inactive boundary returns safe data or a denial with no state change.

### Slice 2 — one delegated player-safe table control

**Prerequisites:** Slice 1; accepted Website/API semantic-write boundary; one CH14 player-safe, S6 session-compatible action with exact actor/scope policy; action/audit/refresh contracts verified.

1. Expose one labelled control that submits the selected owner’s closed request through the shared authenticated action adapter.
2. Prove S8 performs no direct write, mechanic selection, state prediction, retry, or session/roster mutation; successful refresh occurs only after the action root commits.
3. Test disabled/unavailable display versus server denial, forged UI/route/context, wrong/other actor, session end/roster removal/revocation/retirement races, action/audit/notification rollback, duplicate submit, cancellation, timeout, and MCP/HTTP parity.
4. Run focused authorization/action/browser tests, full suite at acceptance, catalog validation if catalog changes, and protocol/security walks when registrations or public surfaces change.

**Exit:** one authorized player can invoke exactly one existing, classified session action through a table control; every other control or direct write path is unavailable and denied.

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Authorization first | Authentication, campaign audience, CH14 control, active lifecycle, and S5 membership are checked before any participant/session/character projection or action selection. |
| View isolation | The result is a fixed owner-approved projection for the caller’s current campaign/session/character only; it reveals no roster, principal, hidden, raw, or arbitrary-history data. |
| No session authority | S8 creates no session state, roster/presence/control grant, narrative, action history, or browser-owned game state. It consumes authoritative sources. |
| Control isolation | The one control delegates an existing CH14/S6/action contract and creates one ordinary root only. No generic command, direct effect, wrapper transaction, optimistic outcome, retry, or replay exists. |
| Fresh permission | Each read/control reauthorizes. Revocation, retirement, session end, roster removal, or policy revision denies safely even from an open/stale page. |
| Transport/UI safety | MCP and HTTP/browser use the same internal authorization/service boundary; output is safely encoded, server-rendered fallback works, and notifications refresh only after commit. |
| Scope discipline | More actions, roster/presence UI, player administration, chat, maps, remote access, or live synchronization require a dedicated owner/plan. |

## Evidence and change control

The implementation receipt records confirmed field allowlists, identity/policy/CH14/C5/C8/S5/S6 dependencies, pre-projection enforcement placement, canonical allowed and denied fixtures, redaction proof, stale/revoke/retire/session-end races, action delegation proof, rollback/SSE ordering, transport parity, accessibility/rendering checks, full-suite result, catalog validation where applicable, and protocol/security evidence. It stores no credentials, tokens, player identity data, raw hidden projections, mechanics/effects, or audit IDs in page/client state.

Amend S8 before adding a second control/action, selecting characters or sessions, player roster/presence/ready controls, party/GM administration, chat/notes, map/combat/travel control, public/remote access, a client state engine, optimistic actions, generic search/history, or live synchronization. These belong to CH14/C5/C8/S5/S6/S7/S9, the chosen action/world/quest/ruleset owner, Website/API/security/deployment, or a dedicated collaboration plan.

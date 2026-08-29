# Session Feature S6 dependency plan — session-scoped gameplay handoff and audit correlation

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Planned; implementation awaits accepted S1–S5, one registered session-compatible action, and a confirmed ActionRunner/audit context extension.**  
Last updated: 2026-08-20

## Execution rule

This is a planning-only repository artifact. It follows AGENTS.md, `procedure.system.modify`, `procedure.mcp.add-tool`, the [Session Operations Plan](../../SESSION_OPERATIONS_PLAN.md), S1–S5, `procedure.action.run`, the existing ActionRunner/audit contracts, relevant ruleset/world/quest action procedures, and [Campaign Feature C8](../../campaign/feature-08/CAMPAIGN-FEATURE-08-DEPENDENCY-PLAN.md). It writes no runtime artifact.

S6 is an integration boundary: it validates that a declared actor action belongs to an active session/roster and correlates the existing root action audit with that session. It never calls an action inside another action, owns game mechanics, stores an activity list on a session, or replays an action to make a recap.

## Target capability

A trusted host can perform one explicitly session-compatible, already-governed action for an eligible character enrolled in one active session. The shared action entry validates the active session and roster before mechanic selection, runs the ordinary selected mechanic and its one root transaction unchanged, and records the session reference only in the root operation/audit context. The resulting action is discoverable as part of that session’s bounded audit correlation without creating session-owned gameplay state.

The first fixture is one S5-enrolled active character and one verified non-spellcasting action already safe for the actor (for example, a supported ability check). It proves a reusable opt-in action-context pattern, not universal action authorization, combat turns, player control, encounter membership, or a session activity journal.

### Included

- One opt-in action capability classification for the first action owner, requiring active session context and an enrolled active actor role.
- A trusted session-context envelope/adapter that validates S1/S5/CH13 scope before ActionRunner mechanic selection and never forwards session metadata as ruleset mechanic input.
- Root-audit correlation by canonical session identity, bounded session-action readback through existing audit/history owner, and no persistent session component/link per action.
- Failure/replay/stale/retire/end-session/cancellation/timeout/rollback/fresh-host evidence.

### Excluded

- A second session action command, nested commit, workflow/transaction wrapper, raw effect endpoint, action queue, scheduler, retries, action replay, automatic recap, or a session-owned activity/event log.
- Selecting mechanics, rolling dice, deciding outcomes, changing action input/output, combat/turn/encounter membership, travel/time, quest/world progression, resource rules, or character/item state.
- Player authorization/control, player free selection of any action, roster/presence changes, multi-character/group action, chat/collaboration, browser write, or generic action exposure.

## Ownership and correlation boundary

| Concern | Owner and S6 rule |
| --- | --- |
| Session/roster/lifecycle | S1/S5/CH13. S6 requires active session and currently active enrolled actor; it writes none of these records. |
| Campaign scope | Campaign attachment owners. Session and actor scope must resolve identically; no caller campaign ID or name matching is used. |
| Action selection/input/mechanics/effects/randomness | Existing ActionRunner and the named action owner. S6 gates before selection and then delegates one unchanged root. |
| Session-compatible classification | Each action owner plus shared authorization/action policy. An action is unavailable in session context until it opts in with exact role/scope requirements. |
| Audit correlation | System audit/history owner. Session ID is root context metadata/relation only after confirmation; it is not copied into mechanics, action input, session state, event payload, or recap. |
| Session action readback | Existing bounded history/audit projection extended only after confirmation; no free-form event/action search or raw audit dump. |
| Player control | CH14/S8. Initial S6 is trusted-host; roster reference does not authorize a player. |

The session correlation must be attached to the same root action audit that proves the outcome. A separate “session activity” entity/list would drift from failed/rolled-back actions and is forbidden. If the audit model cannot safely carry a validated contextual reference, S6 stops at session eligibility and leaves correlation for a dedicated audit-model plan.

## Proposed permanent vocabulary — confirmation required

| Role | Proposed boundary |
| --- | --- |
| Session action context | Exact action-envelope field/adapter ID intentionally undecided. It is trusted transport/application context, parsed before mechanic input, and contains only canonical `sessionId`. |
| Opt-in capability | Exact action registration/policy metadata is owned by the ActionRunner/action-owner contract. It declares a required enrolled actor role and active session scope; no global “all actions” default. |
| Audit correlation | Exact audit metadata field or approved relationship is owned by audit/history. It stores a canonical session reference, never raw action input/effects or session component data. |
| Session activity reader | A bounded confirmed audit/history projection, not a new session component or generic history query. |

Confirm action envelope compatibility, selection ordering, trusted versus caller context, action opt-in registration, audit schema/reference direction, retention/read bounds, event correlation, query route, error semantics, and C8/CH14 interaction before implementation. A public kind/tool requires `procedure.mcp.add-tool` confirmation; no generic `session-action` kind is presumed.

## Closed action boundary

The underlying action retains its own closed request and mechanic input. The session adapter receives only a canonical active `sessionId` in confirmed transport/application context; it must not be placed in the mechanic input object or accepted by action code as an arbitrary rule parameter. The actor subject is resolved by the selected action’s declared role and must match an S5 roster reference.

S6 rejects missing/malformed/unknown/ended/corrupt session, absent/malformed/cross-campaign roster/attachment, retired/archived actor, non-opted-in action, wrong/missing actor role, or denied policy before mechanic candidate selection, randomness, projections beyond safe eligibility, or effects. It returns the underlying action’s normal result/error only after the session gate passes, plus the canonical session correlation where the audit owner permits it. It accepts no campaign/player identity, mechanic ID, raw effect, action outcome, audit/event ID, retry/queue flag, roster mutation, or recap field.

When the session is ended/retired/revoked while an action is in flight, the shared root rechecks all validated guards before commit. If its guard fails, the ordinary root action rolls back; S6 never lets a stale session context commit after closure.

## Resolution and transaction rules

1. Resolve canonical session context before ActionRunner intent/mechanic selection. Require exactly one active S1 session and campaign scope, then resolve one declared action actor role against S5 roster, campaign attachment, and CH13 lifecycle.
2. Validate that the requested action is explicitly session-compatible and its role binding/scope policy matches this actor. Do not select a fallback action if the intended action is not opted in.
3. Delegate the unchanged action request to the ordinary ActionRunner. It validates mechanics/input, performs dice/effects/guards/reactions, and owns the root transaction exactly as outside a session.
4. On a successful root, write the approved session reference into its audit context as part of the same root transaction/audit. The audit correlation cannot appear if action effects roll back. On action failure, no session activity/correlation success record appears.
5. Bounded session-action history reads only verified correlated successful/failed audit summaries according to the audit owner’s safe policy; it never claims that uncorrelated actions did not occur or derives recap facts automatically.

If audit correlation requires a second independent write after action commit, S6 is blocked. It must not report an action as session-scoped until the same root durable audit proves both outcome and validated session context.

## Dependency graph and slices

~~~text
S1 active session + S5 roster + CH13 lifecycle
├─ one verified action owner that opts into session context               [first action leaf]
├─ ActionRunner pre-selection trusted context and guard extension        [shared platform leaf]
├─ audit/history contextual reference and bounded reader                 [shared audit leaf]
├─ campaign attachment scope proof                                       [cross-owner prerequisite]
└─ CH14/C5 only for later player-controlled execution                    [separate gate]
   ├─ Slice 1: zero-effect eligibility and action opt-in proof
   └─ Slice 2: one correlated root action and bounded readback
      └─ S7 recap artifacts and S8 player-safe controls
~~~

### Slice 1 — session eligibility before action selection

**Prerequisites:** S1–S5 accepted; exact first action/actor role and opt-in policy confirmed; trusted context placement and guard ordering are accepted.

1. Add the session-context adapter/guard without modifying mechanic input or session state.
2. Prove active session/roster/attachment/lifecycle and action opt-in are checked before mechanic selection/randomness/effects.
3. Test valid eligibility, no/ended/corrupt session, missing/duplicate/cross-scope roster, wrong/retired actor, non-opted-in/ambiguous action, malformed context, policy denial, and zero action execution on every rejection.
4. Run focused ActionRunner/projection tests and protocol walk if registration changes.

**Exit:** one session context can authorize selection of one explicitly compatible action only when all active scope/roster guards are true; invalid context cannot reach a mechanic.

### Slice 2 — atomic correlated action fixture

**Prerequisites:** Slice 1; confirmed audit correlation and bounded history projection; action owner/root transaction supports guard recheck and failure injection.

1. Execute the first action through the ordinary root and attach session audit correlation atomically on success.
2. Query bounded correlation/readback from a fresh host and compare it with the underlying root audit without adding a session activity record.
3. Inject action, guard/reaction, audit correlation, event/notification/audit, end/retire/revoke race, cancellation, timeout, replay, and rollback failures. Prove no dangling correlation, no action rerun, and no external session/roster mutation.
4. Run focused tests, full suite at acceptance, catalog validation if catalog changes, and protocol/security walk when relevant.

**Exit:** one eligible session action produces one ordinary durable outcome whose root audit is correctly correlated to the active session; failed/stale actions produce neither outcome nor correlation.

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Pre-selection gate | Invalid session/roster/scope/lifecycle/opt-in state is denied before mechanic selection, dice, effects, or outcome narration. |
| Single root | The ordinary action retains one transaction, rule, effects, events, and audit. S6 introduces no nested commit, wrapper transaction, queue, or replay. |
| Correlation integrity | A successful root audit carries one validated session reference; a failed/rolled-back action carries no success correlation. No session activity list duplicates audit history. |
| Scope/lifecycle | Actor must be enrolled in the exact active session and same campaign and remain active through commit. End/retire/revoke races fail safely. |
| Action breadth | Only each action owner’s explicit opt-in is session-compatible. Roster membership does not expose all actions, combat, travel, or group actions. |
| Readback | Bounded audit/history projection shows only confirmed correlated summaries under policy; it is not a transcript, raw event dump, or automatic recap. |
| Authorization | Trusted host initially. CH14/C5 policy is required before a player invokes or sees a session action. |

## Evidence and change control

The implementation receipt records the selected action/role, opt-in and context/audit IDs, guard placement proof, canonical fixtures, fresh-host correlation readback, pre-selection denials, end/retire/revoke races, all rollback/cancellation/timeout cases, catalog validation where applicable, full-suite result, and protocol evidence. It stores no raw effects, transcript, player data, duplicate activity state, or audit ID in a session component.

Amend S6 before adding another action family, multi-actor/group action, action queue/retry/replay, action summary/recap generation, combat/turn/travel orchestration, player self-service, browser write, generic history search, or a new session action kind. These belong to each action owner, S7/S8/S9, CH14, ruleset/World/Quest, audit/history, Website/API, or `procedure.mcp.add-tool` confirmation.

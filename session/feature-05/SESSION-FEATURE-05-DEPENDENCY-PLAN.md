# Session Feature S5 dependency plan — session roster and active-character eligibility

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Planned; implementation awaits accepted S1–S3, campaign character-attachment/participation ownership, and CH13 lifecycle evidence. Player-controlled exposure additionally awaits CH14.**  
Last updated: 2026-08-20

## Execution rule

This is a planning-only repository artifact. It follows AGENTS.md, `procedure.system.create-feature`, `procedure.system.modify`, `procedure.mcp.add-tool`, the [Session Operations Plan](../../SESSION_OPERATIONS_PLAN.md), S1–S4, the [Character Creation Plan](../../CHARACTER_CREATION_PLAN.md), CH13–CH14, and [Campaign Feature C8](../../campaign/feature-08/CAMPAIGN-FEATURE-08-DEPENDENCY-PLAN.md). It writes no runtime artifact.

S5 records declared session participation by reference. Campaign attachment, character lifecycle, player-control grants, inventory, location, and gameplay eligibility remain with their existing owners. A roster is not a party copy, attendance tracker, or authorization list.

## Target capability

A trusted host can validate and enroll one eligible active campaign character in one active campaign session. The session keeps one immutable scoped roster reference for historical/read context, while active eligibility is always rechecked through campaign attachment and CH13 lifecycle. A character may be enrolled once per session; the roster stays immutable after the session ends. Player-controlled discovery later requires CH14, but the initial fixture stores no player identity.

The first fixture is one active S1 session and one active campaign-attached character. It proves a reusable session-to-character reference boundary, not a presence tracker, player account binding, NPC roster, combat roster, party manager, location group, or turn order.

### Included

- One empty-data session-to-character roster relationship with same-campaign, active-session, active-character, and duplicate checks.
- A closed trusted-host `validate`/`enroll` operation, bounded reference roster read, readback, replay/corrupt-state/rollback/cancellation/timeout evidence.
- Historical roster references for ended sessions, with current lifecycle/control always read from their owners.
- A CH14/C5 gate for later player-safe read/discovery only.

### Excluded

- Player identity/control, self-enrollment, participant roles, co-control, ready checks, attendance/departure/rejoin/presence, chat/voice/video, votes, or collaboration.
- Removal/correction of the immutable roster reference; NPC/monster/hireling enrollment; combat/initiative/turn state; travel/formation/location; shared inventory; or any world/quest/character/item/action/session lifecycle mutation.
- Browser writes or new public transport without confirmation.

The first roster is intentionally an immutable declaration, not live attendance. A character later retired or archived remains a historical reference but cannot be newly enrolled or treated as currently playable. A future presence lifecycle must preserve history rather than delete links.

## Ownership and reference boundary

| Concern | Owner and S5 rule |
| --- | --- |
| Session identity/lifecycle | S1/C8. Only one valid active session accepts enrollment; S5 never starts/ends/repairs it. |
| Campaign character attachment/participation | Campaign/CH1 owner. It proves session and character share one campaign; S5 stores no campaign ID or membership state. |
| Character lifecycle | CH13. Only active character actors are enrollable. |
| Player-to-character control | CH14. Initial trusted host has no principal in the request; later player read/discovery is authorized before projection. |
| Roster reference | S5/C8. An empty-data relationship is historical participation reference only, not status, role, inventory, or attendance state. |
| Display/mechanical data | CH1/CH6/ruleset/Items owners. S5 consumes only approved bounded display data and never copies it. |
| Gameplay/turn eligibility | S6 and individual action/encounter owners. Roster membership conveys no action authority. |

## Proposed permanent vocabulary — confirmation required

| Role | Proposed ID and boundary |
| --- | --- |
| Roster relationship | `game.core.campaign.session.includes-character`, directed from session to character with empty data. A character occurs once per session. |
| Governing contract | `procedure.campaign.session.participants`, governing scope/lifecycle validation, immutable enrollment, bounded roster projection, and recovery. |
| Enrollment mechanic | `mechanic.game.core.campaign.session.enroll-character`, returning exactly one relationship-create effect after all owner checks. |
| Roster projection | Exact existing query/result surface is undecided pending C8/CH6/C5/CH14. It must be bounded and projection-based, never a raw graph dump. |

Confirm relationship direction, actor marker/attachment model, list bound/order, display fields, event/audit behavior, trusted-host policy, and whether Campaign already owns a reusable roster relationship. Reuse a real existing owner rather than adding a duplicate.

## Closed enrollment/result boundary

Proposed initial request:

~~~text
{
  operation: "validate" | "enroll",
  sessionId: canonical existing active session entity ID,
  characterId: canonical existing active character entity ID
}
~~~

Campaign scope derives independently from both attachments. The request accepts no campaign/player/principal ID, role/status, profile text, item/location, action/turn/world/quest data, raw relationship/effect, audit/event, or retry field. Missing/null/extra/non-object/malformed IDs, inactive/ended/corrupt session, wrong actor marker, retired/archived character, missing/duplicate/cross-scope attachment, existing roster link, malformed roster link, or list-bound failure rejects before effects.

`validate` has zero effects; `enroll` rechecks everything in one root transaction. Replays return stable already-enrolled correction and never create another link. Success returns only `sessionId`, `characterId`, `enrolled: true`, bounded roster count, and literal next action. It conveys neither player identity nor gameplay authority.

The roster reader validates the session/lifecycle/audience first, orders records by canonical character ID, respects the confirmed bound, and obtains safe display fields only through their owners. It accepts no arbitrary filters, graph traversal, components, history, player, or audience assertion.

## Resolution and transaction rules

1. Resolve exactly one active session and campaign scope, then one approved active character with exactly one campaign attachment and CH13 active lifecycle.
2. Compare scopes; reject absent, duplicate, malformed, or cross-campaign state. Reject duplicate/nonempty/dangling/wrong-kind roster links and excessive count.
3. Player paths, if later exposed, invoke CH14/C5 before character or roster projection. Initial S5 does not infer player authority from profile or visibility.
4. `validate` returns zero effects. `enroll` creates exactly one empty-data session→character relationship. It changes no component, containment, campaign/character state, or external owner.
5. Event/audit behavior follows the root transaction. Failure, cancellation, or timeout rolls back the link. Ended sessions preserve valid roster links for history only.

If the attachment owner cannot prove same-campaign scope atomically, S5 is blocked; caller campaign IDs, entity names, or co-location are not substitutes.

## Dependency graph and slices

~~~text
S1 active session + S3 historical session behavior
├─ CH1/campaign character attachment and participation projection       [cross-owner prerequisite]
├─ CH13 lifecycle projection                                            [character prerequisite]
├─ confirmed roster link/projection vocabulary and bounds               [semantic gate]
├─ CH14/C5 for later player-safe exposure                               [separate consumer gate]
└─ C8 root relationship/event/audit composition                         [shared gate]
   ├─ Slice 1: trusted-host validate/enroll and bounded roster read
   └─ Slice 2: historical reader and CH14 policy integration
      └─ S6 gameplay handoff and S8 table controls
~~~

### Slice 1 — trusted-host immutable enrollment

**Prerequisites:** S1/S3 accepted; character attachment and CH13 lifecycle semantics verified; permanent vocabulary, bounds, and safe display fields confirmed.

1. Add confirmed relationship/contract/mechanic and zero-effect validation.
2. Prove scope/lifecycle before display projection; write no player field or generic roster state.
3. Test valid enrollment, duplicate/replay, no/ended/corrupt session, invalid/retired/archived actor, absent/duplicate/cross-campaign attachment, malformed roster, bounds/order, no-write validate, rollback/cancellation/timeout, and fresh readback.
4. Run focused tests and `roleplay validate catalog` after catalog changes.

**Exit:** one trusted host enrolls one eligible character once into one same-campaign active session; roster read contains references, not copied character/player state.

### Slice 2 — historical and controlled consumer integration

**Prerequisites:** Slice 1; S3 historical read and CH14/C5 policy are verified; surface adaptation confirmed.

1. Permit bounded historical roster inspection for ended sessions under policy.
2. Integrate policy before a player discovers their controlled enrolled character; do not introduce self-enrollment or action authority.
3. Test allow/deny/redaction, revoke/retire after enrollment, no leakage before authorization, active/ended reads, fresh-host result, and transport parity if exposed.
4. Run focused tests and full suite at acceptance.

**Exit:** roster remains historical evidence while current player exposure obeys real policy; enrollment still does not grant an action or turn.

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Same-scope enrollment | One active character links once to one active session only after exact campaign scope/lifecycle checks. Duplicate/cross-scope/invalid link fails unchanged. |
| No copied state | Relationship data is empty. No campaign/profile/player/class/item/location/role/status/turn/attendance data becomes roster truth. |
| Lifecycle | Active character may enroll; retired/archived may not. Historical link survives lifecycle change but grants no current eligibility. |
| Session | Active session accepts enrollment; ended session preserves/read roster only. S5 never changes session lifecycle. |
| Authorization | Trusted-host first. CH14/C5 gates player exposure; profile/visibility/link is not permission. |
| Atomicity | Link/event/audit commit or roll back together. Failure/cancel/timeout creates no link or external mutation. |
| Gameplay | Enrollment selects no mechanic, permits no action, creates no encounter/turn, moves no character, and changes no world/quest state. |

## Evidence and change control

The receipt records confirmed link/attachment/lifecycle IDs, scope proof, fixtures/readback, policy decision, invalid/replay/retire/revoke/rollback/cancellation/timeout cases, catalog validation, full-suite result, and protocol evidence. It contains no player accounts, profile/item/world copies, attendance narrative, raw effects, or audit IDs in roster state.

Amend S5 before participant roles, removal/withdrawal/rejoin/presence, player self-service/co-control, NPC/hireling types, party/encounter/turn mechanics, location/travel formation, inventory, chat/collaboration, gameplay authorization, browser writes, or new public surface. These require dedicated presence/party, CH14, S6/S8/S9, Items/World/ruleset, or public-surface owner plans.

# Character Feature 14 dependency plan — authenticated player-to-character control

Status: **Planned; implementation awaits a real identity/principal capability, enforceable authorization policy hook, and verified CH6/CH13 character lifecycle evidence.**  
Last updated: 2026-08-20

## Execution rule

This is a planning-only repository artifact. It follows AGENTS.md, `procedure.system.create-feature`, `procedure.system.modify`, `procedure.mcp.add-tool`, the [Character Creation Plan](../../CHARACTER_CREATION_PLAN.md), CH5–CH8, CH13, the Campaign audience-policy boundary, and the Website/API exposure plan. It writes no runtime artifact.

CH14 consumes verified identity and authorization services. It binds one authenticated principal to one active campaign character through a scoped control grant, then enforces that grant at the shared command/query boundary. A profile name, campaign attachment, possession, visibility label, browser session field, or caller-provided principal ID is never authentication or permission.

## Target capability

An authenticated player may discover and use only one explicitly granted active character in an authorized campaign through the same governed character/action contracts as a trusted host. A separately authorized campaign administrator can grant or revoke that control. Every read and action resolves the authenticated principal from trusted request context, checks campaign scope, audience policy, active character lifecycle, and the current control grant before any character projection, mechanics selection, or state change. Revocation and retirement immediately prevent future player-character use while preserving all character and campaign history.

The first fixture is one authenticated principal, one active campaign, one active character, one active player-control grant, one administrator grant/revoke path, one bounded character read, and one already-supported player-safe action. It proves reusable scoped control enforcement, not general roles/teams, public sharing, co-control, account registration, remote deployment, or every gameplay action.

### Included

- A source-independent, campaign-scoped player-control grant reference from a character to an identity-owner principal, with no copied user/account information.
- One authenticated principal context propagated from MCP/HTTP transport to shared command/query authorization middleware; it is not an optional payload field.
- One administrator-only grant/revoke operation, exact active-character/campaign/lifecycle checks, bounded player character discovery/readback, and a single permitted player action path.
- Enforcement before all CH6 character inspection/hand-off, CH7 correction, CH8 guide/create consumer, CH9–CH12 advancement, CH13 retirement/archive, and selected ordinary action resolution as each action declares its character-control policy.
- Atomic retirement/revocation composition, grant/revoke audit history, stale/replay/cross-principal/cross-campaign denial, and MCP/HTTP authorization parity evidence.

### Excluded

- Building identity providers, registration/login/passwords, token issuance/storage/refresh, OAuth, MFA, account recovery, directory sync, public internet exposure, device/session management, rate limiting, or generic RBAC/ABAC language. Those belong to the identity/authorization and deployment owners.
- Player creation of arbitrary characters, self-grant, self-revoke, player-to-player transfer, co-control, delegation, party management, GM powers, spectator sharing, account/profile data on a character, or a user-facing administration website.
- Security by `visibility`, `party`, `gm`, profile naming, client-side route guards, hidden buttons, caller input, or browser cookies alone.
- Changing item possession, campaign membership, character lifecycle, source/class/ability/mechanical state, action rules, action transport mechanics, or audit history ownership.

## Ownership and authorization result

| Concern | Authoritative owner and CH14 rule |
| --- | --- |
| Principal identity/authentication/session/token verification | Identity capability. It supplies one trusted canonical principal context (or an anonymous/unauthenticated result) to every adapter; CH14 never stores credentials or trusts a payload principal ID. |
| Policy evaluation/enforcement hook | Authorization capability. It evaluates principal, action/read capability, campaign scope, audience, and control grant before application logic. UI filtering is never the gate. |
| Character-control binding | CH14, using an identity-compatible directed grant relationship after confirmation. It links a character to a canonical principal and has no copied campaign, email, display name, permissions array, token, or session data. |
| Campaign scope/administrator authority | Campaign plus authorization policy owners. The character's existing campaign attachment defines scope; policy decides who may administer a grant. CH14 does not invent a GM identifier/list. |
| Character eligibility | CH13 lifecycle: only `active` characters may carry an active player-control grant. Retiring invokes the confirmed revocation child transition or an equivalent atomic policy invalidation. |
| Character/action semantics | CH5–CH12 and individual ruleset action owners. CH14 authorizes entry; it neither selects mechanics nor duplicates gameplay validation/effects. |
| Reads and audience filtering | CH6/CH8 and Campaign audience-policy capability. A control grant does not itself reveal hidden campaign/world/quest facts. |
| Transport/browser exposure | MCP/HTTP adapters and Website/API plan. Both pass trusted principal context to one internal authorization service; the browser never calls raw MCP/DB or declares permission. |

The authorization hook must run before read projection and before `ActionRunner` chooses a mechanic. If the current action/query surface cannot receive a trusted principal context without changing every public contract, stop and implement the shared authentication/authorization adapter first. Do not add an optional `playerId`, `isGm`, or `authorized` request field as a shortcut.

## Control-grant model and confirmation boundary

The provisional relationship is `game.core.character.controlled-by-principal`, directed from the character to the identity-owner principal, with relationship data exactly `{ "role": "player", "status": "active" | "revoked" }`. Campaign scope comes solely from the character's existing campaign attachment. The initial policy allows exactly one active player grant per active character and one active controlled character for that principal within that campaign; its cardinality is a policy rule, not an unbounded schema assumption. Revoked grants remain historical evidence but cannot be reactivated by CH14.

The precise relationship ID/direction/data, principal reference form, relationship visibility, identity lifetime, cardinality policy, administrator capability, and whether the authorization owner already has a generic grant primitive all require confirmation. If the identity/authorization owner supplies its own binding record, CH14 uses it and does not create a parallel relationship. If future co-control/delegation is desired, it needs a separate policy/data-model plan; it is not enabled by multiple links.

## Proposed permanent vocabulary — confirmation required

| Role | Proposed ID and boundary |
| --- | --- |
| Character-control relationship | `game.core.character.controlled-by-principal`, only if the identity owner has no generic binding. It references a canonical principal and has closed role/status data only. |
| Governing contract | `procedure.character.control`, governing grant/revoke, lifecycle coupling, scope/cardinality checks, recovery, and audit inputs. |
| Control mechanism | `mechanic.dnd2024.character.control`, an administrator-authorized semantic operation returning a typed relationship transition, not a generic effect endpoint. |
| Shared policy capability | Exact identity/authentication/authorization procedure, principal context type, and middleware IDs intentionally remain owned by that external capability. |

Confirm all permanent IDs, direction/cardinality, principal context propagation, capability names, administrator rule, grant history semantics, CH13 revocation composition, existing-surface adaptation, and MCP/HTTP error/audit mapping under `procedure.system.modify` and `procedure.mcp.add-tool` where relevant. A relationship to an unauthenticated string, an account display-name, or a browser session is invalid by design.

## Closed administration and player boundaries

The administrator control request is a schema-bound object:

~~~text
{
  operation: "validate" | "grant" | "revoke",
  characterId: canonical existing character entity ID,
  principalRef: identity-owner canonical principal reference,
  expectedGrantStatus: "absent" | "active"
}
~~~

`principalRef` is permitted only in this administrator grant/revoke command and is resolved/validated by the identity owner; it is never accepted by player actions or reads. `characterId` is resolved to campaign scope through its attachment. `expectedGrantStatus` is a stale-intent guard: grant requires absent, revoke requires active. Missing/null/extra/non-object/malformed values, anonymous or unknown principal, wrong administrator scope, retired/archived character, inactive/invalid campaign, cross-campaign grant, cardinality conflict, duplicate/revoked binding, or policy denial fail before effects.

For player reads/actions, the request contains no principal reference or authorization claim. The adapter provides principal context; authorization derives the active controlled character(s) in the requested campaign and passes a bound character role to the existing CH6/action service. A request naming another character is denied before any projection/mechanic selection. An absent/anonymous principal has no player-control capability. `validate` on the administrator request has zero durable grant effects; grant/revoke re-evaluate policy in one root transaction and never rely on an earlier preview.

Canonical administrator results contain only `characterId`, `grantStatus`, `controlCapability`, and literal recovery/next action. Player results retain the underlying bounded CH6/action result only after authorization succeeds. Neither path exposes tokens, internal policy rules, other principals, account data, hidden campaign records, raw effects, a raw relationship payload, or audit/event IDs.

## Enforcement and transaction rules

1. At adapter entry, authenticate the request and construct/verify one trusted principal context. Reject missing, expired, invalid, or anonymous context for player capabilities before request parsing can select a character/action.
2. For an administration operation, authorization verifies the caller's campaign-scoped grant-management capability, resolves the character's single campaign attachment and lifecycle, resolves canonical target principal, and applies closed cardinality/scope/status checks. For a player operation, resolve the controlled active character from the authenticated principal and campaign scope; never from client assertion.
3. For any character projection or action, policy evaluates explicit capability, audience restrictions, character lifecycle, active control relationship, campaign scope, and the named underlying operation. It denies before running CH5–CH13 or ActionRunner selection. Underlying owners then perform their ordinary validation and transaction behavior.
4. `validate` returns no durable effects. `grant`/`revoke` execute a typed control relationship transition under one root action/audit. Grant is active only after commit. Revocation is effective only after commit; failed/audited/cancelled operations leave the prior control state unchanged.
5. CH13 retirement composes a revoke/policy-invalidating child transition with its campaign participation/lifecycle effects. If the identity owner cannot join it atomically, retirement must block when an active grant exists rather than leave a retired but player-controlled actor.
6. After commit, ordinary event/audit/history records the authorized root. Cache/session/SSE consumers only refresh after successful commit and must reauthorize on every read/action; an old page or capability result is not a durable permission.

The first player action is chosen only after its ruleset owner confirms a player-control capability classification and target/scope constraints. A generic `action` call with a controlled actor role is not automatically safe for a player. Each newly exposed mechanic must opt into the shared authorization policy and receives its own CH6/owner evidence.

## Dependency graph and slices

~~~text
Verified CH6 character/action handoff + CH13 lifecycle
├─ identity provider and canonical trusted principal context                  [missing primary leaf]
├─ authorization policy + pre-projection/pre-mechanic enforcement hook       [missing primary leaf]
├─ campaign-scoped administrator/party/audience policy                       [external campaign leaf]
├─ CH1 campaign attachment and CH13 retirement participation transition      [character/campaign prerequisite]
├─ MCP/HTTP shared command adapters and Website/API exposure decision        [transport gate]
└─ one ruleset action classified player-safe                                  [action-owner leaf]
   └─ Slice 1: principal/policy adapter and zero-effect authorization proof
      └─ Slice 2: administrator grant/revoke and CH13 revocation composition
         └─ Slice 3: one player discovery/read/action fixture with transport parity
            └─ Later action exposure, co-control, remote deployment, and richer roles
~~~

### Slice 1 — trusted context and authorization proof

**Prerequisites:** Identity and authorization owners have accepted canonical principal/authentication/error/policy contracts; campaign administrator/audience semantics and permanent vocabulary are confirmed.

1. Integrate a trusted principal context with MCP/HTTP adapters and the shared query/action entry points; no character binding is created yet.
2. Define/verify policy evaluation before projections and mechanic selection, with a deny-by-default anonymous/unknown path and safe error envelope.
3. Test invalid/expired/anonymous context, forged payload identity, missing policy, wrong campaign/audience, deny-before-projection/selection, transport parity, caching/retry, and no user/account persistence in character data.
4. Run identity/adapter-focused tests and protocol walk only if surface/service registration changes.

**Exit:** every relevant entry point has a trusted principal context and denies unauthorised reads/actions before character data or mechanics can leak/run.

### Slice 2 — one administrative control grant

**Prerequisites:** Slice 1 accepted; CH1 attachment and CH13 lifecycle/campaign participation contracts are verified; exact relationship/grant owner and atomic composition are confirmed.

1. Add the confirmed control grant/revoke contract and typed relationship transition; apply only to active characters in an active campaign.
2. Compose CH13 retirement with revoke/policy invalidation atomically, including existing active grant/no-grant cases.
3. Test grant/revoke, duplicate/stale/replay, cross-principal/cross-campaign, cardinality, retired/archived, denied administrator, relationship corruption, CH13 rollover, rollback, cancellation, timeout, and fresh readback/history.
4. Run focused tests and `roleplay validate catalog` after catalog changes.

**Exit:** an authorized administrator can grant and revoke one scoped active control binding, and retirement cannot leave an active player capability behind.

### Slice 3 — one controlled character action

**Prerequisites:** Slice 2 accepted; one underlying action has a confirmed player-safe capability classification; CH6 result/redaction and MCP/HTTP/browser exposure decision are accepted.

1. Expose one bounded discovery/read path and one real player action through the shared authorization service; bind subject roles to the authenticated principal's controlled active character.
2. Prove allowed self access/action, denied other-character/self-after-revoke/retired/archived/anonymous/cross-campaign access, no leakage before denial, and equivalent MCP/HTTP result/error behavior.
3. Test concurrency with revoke/retire versus an action, stale pages/tokens, event/audit ordering, cancellation/timeout, reauthorization after refresh, and readback across a fresh host.
4. Run focused tests, full suite at acceptance, security/adapter tests, and protocol walk if MCP dependency registration changes.

**Exit:** one authenticated player can use exactly one granted active character through a real governed action, while every other character and unauthorized path is denied before execution.

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Authentication | Player capability requires trusted authenticated principal context. Caller IDs, profile fields, visibility labels, client routing, or cookies alone never authorize. |
| Binding scope | One active control grant binds one canonical principal to one active character in its campaign under confirmed cardinality. It stores no copied campaign/account/permission data. |
| Administrator boundary | Only policy-authorized campaign administrators grant/revoke; self-service, forged target principal, cross-campaign, duplicate, stale, and denied operations cause no relationship change. |
| Enforcement order | Policy denies before entity projection, source/secret exposure, mechanics selection, randomness, or effects. Underlying mechanics still enforce their own rules after authorization. |
| Lifecycle coupling | Retired/archived characters cannot have usable player control. Retirement and revocation are atomic or retirement blocks; revoked bindings never regain power in CH14. |
| Read/action bounds | The first player sees only bounded approved character data and performs one classified safe action. A binding does not disclose campaign/world/quest facts or automatically expose every action. |
| Transport parity | MCP and HTTP adapters supply the same trusted principal context to one internal policy service and return equivalent allow/deny semantics; neither browser nor MCP client can bypass it. |
| Atomicity/history | Failed/cancelled/timed-out grant/revoke/retire/action leaves grants and game state unchanged. Successful control transitions remain auditable without credentials/tokens or personal data in history. |

## Evidence and change control

The implementation receipt records confirmed identity/policy/grant IDs, principal-context and enforcement placement proof, campaign administrator/scope decision, CH13 composition proof, one bounded player action classification, allow/deny/redaction fixtures, forged/stale/revoke/retire/concurrency/rollback cases, MCP/HTTP parity, security tests, catalog validation where applicable, full-suite result, and protocol walk evidence. It does not record credentials, tokens, personal data, policy secrets, raw effects, source rules, or audit IDs.

Amend CH14 before adding another player action, co-control/delegation, self-service transfer, party/role administration, public sharing, remote exposure, new identity provider/session behavior, account UI, player-created characters, item/world/quest control, or a generic action authorization rule. Those belong to the action owner plus authorization policy, a dedicated delegation/role plan, Website/API deployment/security plan, identity owner, CH5/CH8, or the respective subsystem owner.

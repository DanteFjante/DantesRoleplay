# Session Feature S9 dependency plan — concurrent, live, and remote session collaboration

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Planned; blocked by accepted S8, authenticated remote deployment/security design, verified C5/CH14 policy, a bounded SSE catch-up contract, and one action-owner concurrency fixture.**  
Last updated: 2026-08-20

## Execution rule

This is a planning-only repository artifact. It follows AGENTS.md, `procedure.system.modify`, `procedure.mcp.add-tool`, the [Session Operations Plan](../../SESSION_OPERATIONS_PLAN.md), S1–S8, C5/C8, CH14, the [Website/API plan](../../WEBSITE_AND_API_PLAN.md), deployment/identity/authorization contracts, ActionRunner, and the selected action owner. It writes no runtime artifact.

S9 is the remote-delivery and concurrent-consumer boundary for an already correct S8 participant experience. It authorizes each remote connection and request, delivers only post-commit invalidations, and delegates every read/action to the existing policy and action roots. It never turns an SSE connection, browser tab, network session, client clock, or chat/presence signal into game/session authority; nor does it add a universal session mutex, action queue, or second transaction coordinator.

## Target capability

Two authenticated remote participants can concurrently open the same campaign’s fixed S8 session-table view. Each receives only the policy-approved view for their own current control grant. After an existing relevant root commits, the server emits a compact, authorized invalidation; each client reauthorizes and reloads the authoritative view. A disconnected client can reconnect and recover by a normal fresh read, without depending on a perfect event stream.

After that read-only fixture is accepted, one remote participant can invoke the one S8 delegated player-safe action. Concurrent/conflicting attempts are decided solely by the selected action owner’s declared revision/guard/transaction rules. The client receives the ordinary committed result or a safe conflict/denial and reloads; S9 never chooses an outcome, retries an action, serializes unrelated play, or declares which participant is the table host.

The first fixture is two authenticated principals with separately controlled active characters enrolled in one active session, two remote browser connections, the fixed S8 view, one authorized invalidation resource, and one existing player-safe session action with a verified concurrency guard. It proves remote participant freshness and one action conflict, not co-GM control, chat, voice/video, collaborative editing, combat turns, map manipulation, or arbitrary public hosting.

### Included

- A ratified remote threat/deployment boundary: trusted identity transport, TLS/host/origin policy, authenticated request and stream admission, session-cookie/token handling by the identity owner, rate/abuse/logging/incident responsibility, and rollback/deployment procedure.
- Two concurrently connected, policy-gated S8 readers; per-request and per-stream authorization; compact post-commit invalidations; reconnect/catch-up/manual-refresh behavior; and no sensitive event payload.
- One later remote delegation of an existing CH14/S6 player-safe action, with each client request reauthorized and normal action-owner concurrency/transaction checks retained.
- Cross-client stale-page, revoke/retire/session-end/roster-change/policy-revision, disconnect/reconnect, duplicated delivery, missed delivery, partial deployment, and action-conflict evidence.

### Excluded

- Building authentication/identity providers, credential/token/session issuance, account recovery, generic authorization, hosting provider selection, DNS, certificates, secrets management, DDoS protection, observability platform, or data-residency policy. S9 consumes these external guarantees.
- A host/GM lease, co-GM election, leader failover, session ownership transfer, participant presence/typing/ready state, chat/transcript, voice/video, push notifications, shared cursor, collaborative text/artifact editing, WebSocket protocol, or peer-to-peer transport.
- A global session lock, generic action serialiser/queue/retry, client-side conflict resolution, optimistic permanent state, replay, direct database/MCP access, browser mechanics/effects, or an S9 event/activity history.
- Remote exposure of arbitrary campaign/session/character/roster/history/audit data, other-player control, public/spectator sharing, extra actions, tactical/map/combat controls, or automatic narrative progression.

## Ownership and concurrency boundary

| Concern | Owner and S9 rule |
| --- | --- |
| Remote identity, transport security, deployment | Identity/authorization and deployment/security owners. S9 requires a verified trusted principal context on every HTTP/SSE/action request; it stores no credential, token, IP, device, or network-session state. |
| Campaign audience and player control | C5 and CH14. Policy is evaluated independently for each connection/request; a live connection never preserves permission after revocation or scope change. |
| Session and participant state | C8/S1–S5. S9 reads S8’s approved projection and cannot mutate lifecycle, roster, recap, checkpoint, or host authority. |
| Player-safe view/control | S8. S9 exposes the already fixed view and its one already delegated control; it must not widen fields or action capability. |
| Action semantics and conflicts | CH14/S6/ActionRunner and the selected action owner. Target revisions, stale intent, guards, randomness, effects, root transaction, and audit are owned there—not by connection order or S9. |
| Live refresh | Website/API notification owner. Emit invalidation only after committed roots; clients re-read/re-authorize. An invalidation is neither a fact nor a reliable command/acknowledgement channel. |
| Concurrent database behavior | Persistence/action owners. S9 tests the chosen action’s actual transaction and conflict behavior; it does not disguise SQLite/process limits with a fake distributed lock. |
| Multi-host authority | A separate collaboration/governance plan. S9’s participant fixture does not imply that multiple hosts may start/end/restore or adjudicate a session. |

The server treats every stream message as a hint to re-fetch. It emits only a fixed authorized resource-invalidation label plus the minimum confirmed version/correlation data, never session contents, narrative text, player identities, action inputs/outcomes, hidden IDs, authorization decisions, or audit records. A client that misses, duplicates, reorders, or forges a message remains correct after a fresh authorized read.

## Proposed public/deployment boundary — confirmation required

S9 proposes no durable collaboration component, participant-presence record, host lease, session relationship, action wrapper, or global lock. Its durable facts remain owned by C8/S5/S6/action/audit owners.

| Need | Confirmation boundary |
| --- | --- |
| Remote transport | Deployment/security owner confirms HTTPS/TLS termination, trusted forwarding headers, binding/network scope, allowed origin/CORS/CSRF model, secrets and incident/rollback policy, request/stream quotas, logging redaction, and remote availability model. Loopback-only remains the rule until then. |
| Authenticated view/action | C5/CH14/S8 confirm trusted principal propagation, policy revision/revocation point, session/campaign scope resolution, fixed error redaction, and per-request authorization. |
| Live invalidation | Website/API confirms one authorized resource subscription, post-commit publication, `Last-Event-ID` or equivalent bounded catch-up semantics, replay window/expiry, reconnect/backoff, and no sensitive identifiers in events. WebSockets are not presumed. |
| Conflict fixture | The selected S8/CH14/S6 action owner confirms its exact revision/guard field, conflict error, idempotency/replay behavior, transaction boundary, and testable simultaneous-request fixture. |
| Public routes/tools | Exact HTTP/SSE route names and any MCP surface remain owned by Website/API and `procedure.mcp.add-tool`; S9 does not reserve a generic `session-live`, `presence`, or `session-control` kind. |

Confirm all of these together before a remote endpoint is exposed. If remote transport cannot preserve trusted principal context, policy cannot reauthorize each request/stream, an invalidation leaks resource meaning, or the selected action lacks a real conflict contract, S9 remains blocked. No temporary shared password, query-string identity, client-supplied role/session, obscured endpoint, or “trusted browser” exception is acceptable.

## Closed remote read, event, and action boundaries

Remote read uses the fixed S8 participant-view request with authenticated transport context. It accepts no caller-supplied principal, character/control-grant, audience, roster, arbitrary session, field selection, history range, query, pagination, or cache-bypass permission. Campaign/session selection is resolved only through policy-approved route/context and the active-session rule.

The live subscription accepts no campaign/session/entity/action filter supplied by the client. After transport authentication and policy resolution, the server subscribes that connection only to the fixed participant-view invalidation class it is allowed to refresh. On authorization failure, scope/lifecycle transition, replay-window expiration, reconnect, or uncertain delivery, it closes/denies safely and requires a fresh authorized view; it does not replay hidden state.

The later action request is precisely the existing S8/CH14/S6/action-owner closed request, with trusted transport context. S9 adds no client sequence number as game authority, no session/character override, no raw effect/mechanic ID, no batch/queue/retry, and no conflict-resolution choice. On a normal action-owner conflict, rejection, or cancellation, no success invalidation is emitted; clients obtain truth through a fresh read. On success, the ordinary committed root provides audit and then publishes an authorized invalidation.

## Dependency graph and slices

~~~text
Accepted S8 fixed view and delegated control
├─ identity/authorization remote principal context + C5/CH14 reauthorization    [security roots]
├─ deployment/security threat model and HTTPS/origin/secret/operability policy  [remote gate]
├─ Website/API authenticated SSE + bounded catch-up/invalidation contract        [delivery gate]
├─ C8/S5 lifecycle/roster and policy-change invalidation sources                 [session gate]
└─ one S6/CH14 action-owner conflict contract                                    [action gate]
   ├─ Slice 0: ratify remote security, stream, and conflict boundaries
   ├─ Slice 1: two-client authenticated live read freshness
   └─ Slice 2: one remote concurrent action conflict fixture
      └─ Later host coordination/chat/WebSockets/collaborative editing need successors
~~~

### Slice 0 — ratify remotely safe collaboration prerequisites

**Prerequisites:** accepted S8 shape; deployment/security, identity, C5/CH14, Website/API, and selected action owners available for decision.

1. Record the deployment topology and security threat model, trusted principal propagation, transport/origin/CSRF/secret/logging/rate-limit responsibility, policy revocation point, and remote rollback/incident boundary.
2. Confirm the fixed S8 resource-invalidation vocabulary, stream authorization/catch-up/expiry/reconnect rules, and that event metadata cannot disclose hidden scope or player information.
3. Select one S8 action and document its actual optimistic-concurrency guard/revision/idempotency behavior. Reject the slice if it relies only on client ordering or a process-local lock.
4. Confirm public endpoint/tool naming and the first two-client fixture. No remote endpoint, component, or database state is created in this slice.

**Exit:** there is one approved remote trust, invalidation, and action-conflict contract; unresolved security or concurrency choices are named blockers, not implicit behavior.

### Slice 1 — two-client authenticated live read freshness

**Prerequisites:** Slice 0; verified remote deployment and principal context; accepted C5/CH14/S8 projection; Website/API post-commit stream and bounded catch-up implementation.

1. Admit two independently authenticated participant connections only after their individual policy checks; render the fixed S8 view using server-authoritative data.
2. Publish a compact authorized invalidation after relevant committed state changes, then require each client to reauthorize and re-fetch rather than push state.
3. Test allowed/denied connections, cross-campaign/character isolation, revoked/retired/not-enrolled/session-ended users, policy revision, forged/malformed subscription, dropped/reordered/duplicated/missed event, reconnect/catch-up expiry, server restart, and no pre-commit/rollback notification.
4. Exercise real remote TLS/origin/logging/redaction/rate-limit configuration and server-rendered/manual-refresh accessibility; run focused security/transport tests.

**Exit:** two remote authorized clients independently converge on their own current fixed views after commits or reconnects, while an unauthorized/stale client learns no protected state.

### Slice 2 — one remote action concurrency fixture

**Prerequisites:** Slice 1; one selected player-safe S8/CH14/S6 action with verified target revision/guard and failure injection; action and notification transactions demonstrably compose.

1. Invoke that existing action through two deliberately simultaneous remote requests in the declared conflict configuration; preserve its normal action root and audit correlation.
2. Prove the owner’s exact intended outcome: either one commit and one stated stale/conflict denial, or the documented independent outcomes when targets are intentionally disjoint. Never claim serial order from network arrival.
3. Test concurrent revoke/retire/session-end/roster removal/policy revision, duplicate submit, client disconnect after send, timeout/cancellation, worker/process restart, action/audit/event rollback, stale/reconnect readback, and no accidental retry/replay.
4. Run focused action/authorization/SSE/end-to-end remote tests, full suite at acceptance, catalog validation if catalog changes, and protocol/security/deployment walks when relevant.

**Exit:** the chosen remote action preserves its authoritative conflict and transaction semantics under real concurrent clients; all clients regain truthful state through authorized reads.

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Remote security | No remote service exists before the confirmed deployment/security boundary. Every request and stream obtains trusted principal context and policy checks; no token/identity/scope claim comes from page or payload. |
| View isolation | Each participant receives only the fixed S8 projection authorized at that request. Another client’s connection, roster, control, session, or campaign cannot widen it. |
| Live correctness | Events are post-commit, minimal invalidation hints. Missed/reordered/duplicated events, reconnect, restart, and expiry are safe because a fresh authorized read is the recovery path. |
| Permission freshness | Revocation, retirement, roster/session/lifecycle change, and policy revision take effect on the next read/action/stream authorization; stale tabs have no continuing authority. |
| Concurrent action | The selected action owner’s declared revision/guard and one root transaction decide conflicts. S9 adds neither queue/lock/retry nor a network-arrival ordering rule. |
| Failure atomicity | Failed/cancelled/timed-out/conflicted or rolled-back actions publish no success invalidation and leave no collaboration/presence/activity record. Successful roots retain their ordinary audit only. |
| Scope discipline | Co-hosting, chat, live cursors, collaboration editing, WebSockets, public hosting, combat/map control, and any extra action remain separately planned capabilities. |

## Evidence and change control

The implementation receipt records the approved threat/deployment model, public transport boundaries, principal and policy enforcement placement, event redaction/catch-up contract, remote two-client fixtures, denied/leak tests, post-commit/rollback ordering, reconnect/restart behavior, selected action concurrency proof, revoke/retire/session-end races, conflict outcomes, logs/secrets redaction evidence, full-suite result, catalog validation where applicable, and protocol/security/deployment walk evidence. It stores no credentials, tokens, personal data, raw stream payload, presence/chat state, copied session facts, or duplicate action/audit history.

Amend S9 before adding multi-host/GM authority or failover, presence/chat/voice/video, collaborative narrative/page editing, a WebSocket or peer-to-peer protocol, more actions or group/turn/combat/map interactions, client-side conflict resolution/optimistic state, public/spectator access, a new hosting topology, or cross-campaign collaboration. Those require dedicated governance/identity/authorization, messaging/media, artifact/editor, action/ruleset/world/quest, Website/API/deployment, and data-lifecycle plans.

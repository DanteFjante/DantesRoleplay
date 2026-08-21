# E9 dependency plan — trusted principal context and authorization hook

Status: **Planned and intentionally deferred for the playable-prototype phase. No implementation
slice is authorised until the identity-provider and shared authorization-boundary decisions are
confirmed.**
Last updated: 2026-08-21

## Execution rule

This is planning only. E9 is not needed for local gameplay or the local E10 feedback loop, and is
deferred while playable game development is the priority. Security semantics need a human-selected
identity-provider and authorization boundary before implementation. A later pass must re-read `procedure.system.modify`, relevant
transport/command/query contracts, audit/privacy requirements, Campaign authority plans, and CH14;
then implement one reviewed slice with deny-by-default and transport-parity tests. It must stop on
any ambiguous principal, trust source, or recovery authority.

For the prototype-priority rationale, re-entry checklist, and the tentative self-hosted
username/password direction, see [E10 future development](../e10/E10-FUTURE-DEVELOPMENT.md).

## Target capability

Every command and read can receive one trusted principal context from an approved identity provider
and pass it through one shared authorization hook before application logic, allowing campaign/GM
and player-control features to make enforceable scoped decisions without trusting caller payload.

### Included

- A provider-neutral trusted principal result, anonymous/unauthenticated result, request
  propagation, shared policy-evaluation hook, denial/audit semantics, and transport parity.
- Future consumers: campaign GM operations, Feature 38 social adjudication, and Character CH14
  control grants.

### Excluded

- Choosing or building an identity provider; credentials, accounts, passwords, OAuth, token
  issuance/storage, sessions, MFA, registration, deployment, generic RBAC/ABAC, role editor,
  campaign membership, player control grants, or game mechanics.

## Existing evidence and owner decision

Campaign operations use a trusted-host boundary today, and Character CH14 correctly refuses to
treat a caller principal ID, profile, visibility label, or campaign attachment as authentication.
There is no accepted identity-provider or enforceable shared authorization interceptor. This is a
security/product boundary, so a catalog-only placeholder would create false authority.

## Dependency graph

~~~text
E9 trusted principal and authorization hook                          [blocked parent]
├─ identity-provider selection and trusted request context           [external missing decision]
├─ shared policy/interceptor boundary                                 [missing after provider decision]
├─ principal propagation through MCP/HTTP/shared runners             [blocked]
├─ denial/audit/recovery semantics                                    [blocked]
├─ campaign GM policy consumer                                       [blocked]
├─ F38 social adjudication consumer                                  [blocked]
└─ CH14 player-control consumer                                      [blocked]
~~~

## Ownership decisions

1. Authentication is supplied only by a real selected identity provider; the platform receives a
   canonical principal or an unauthenticated result. It never accepts identity from action input.
2. Authorization is one shared pre-application hook. Game features declare the capability/scope
   they require and consume its allow/deny result; they do not implement bespoke transport checks.
3. Campaign scope, social adjudication, and character control are separate consumer policies.
   E9 provides neither a GM list nor a player-control relationship.
4. A denial must occur before mechanic selection/projection/effects and leave no partial game
   state. Existing audit rules continue to record the attempted operation without exposing secrets.

## Required decision before Slice 1

Human confirmation must select the identity-provider boundary, principal identifier/lifecycle,
anonymous behavior, MCP/HTTP transport trust model, administrator bootstrap/recovery model, and
privacy/audit constraints. Until then, no schema, tool input, fake `isGm` flag, catalog fixture, or
partial authorization middleware may be implemented.

### Decision record required for implementation

The future implementation request must contain a short approved decision record answering all of
the following. “Trusted host” is not an answer: it identifies a current operating convention, not
an authenticated principal or enforceable policy boundary.

| Decision | Must name | Why it cannot be inferred |
| --- | --- | --- |
| Provider and verifier | External/provider-owned issuer or host adapter, token/session verification point, key/claim refresh, and which process may construct a principal. | Caller input, profile name, or campaign attachment is forgeable identity data. |
| Canonical principal | Immutable opaque identifier, tenant/environment namespace, display-name handling, deactivation/reuse rule, and service-principal rule. | Mutable, reused, or display-derived IDs break audit and revocation semantics. |
| Transport trust | Exact MCP, HTTP, CLI/test, background-job, and internal-call adapters; which are authenticated; and how each passes verified context downstream. | A browser-only or MCP-only check leaves another command path open. |
| Anonymous/default | Which reads, if any, permit anonymous access; otherwise response/status for missing, expired, malformed, or unverified credentials. | Absence of this choice can become allow-by-default. |
| Bootstrap/recovery | First administrator/campaign creator path, break-glass authority, approval/audit/expiry, revocation, and outage behavior. | Recovery is itself a privileged authorization path. |
| Policy/data | Initial named capabilities, scope vocabulary, policy-data owner, membership/grant revocation semantics, and consumer separation. | E9 must not smuggle GM lists or CH14 grants into middleware. |
| Audit/privacy | Denial/audit fields, retention/access rule, correlation behavior, PII/redaction rule, and client-safe error format. | Evidence must help operators without leaking membership or hidden world data. |

If any row is undecided, record it as an explicit blocker and stop. A temporary development test
adapter is permitted only when it is process-local, named as test-only, excluded from production
composition, and unreachable through every external transport.

## Contract boundary for the approved first slice

Once the decision record is approved, Slice 1 introduces no game authorization policy. It establishes
only these provider-neutral concepts:

1. `TrustedPrincipalContext`: either a verified canonical principal plus a non-secret immutable
   authentication-context summary, or an explicit unauthenticated result. A selected transport
   adapter constructs it; command/query payload cannot deserialize it.
2. `AuthorizationRequest`: operation/capability identifier, declared scope references, operation
   correlation identifier, and trusted context. It contains no raw credential, client-selected role,
   profile, or game-specific grant.
3. `AuthorizationDecision`: `allow` or stable `deny` code plus client-safe recovery. It does not
   expose policy rules, hidden entity existence, membership, or secrets.
4. One pre-application interceptor for every selected initial command/read adapter. It evaluates
   before mechanic lookup, state projection, effects, events, notifications, operation success, or
   result caching. Internal calls use the same hook or an explicitly declared audited service
   principal; they never receive an implicit bypass.

Context has request lifetime only. It is not copied into component data, event payloads, effect
data, catalog records, random seeds, or consumer procedure inputs. Audit stores only the approved
minimum principal reference/pseudonym and decision evidence.

## Slice 1 — trusted context and deny-by-default hook

### Scope

Implement one selected command adapter and one selected read adapter through the shared boundary,
with a test-only verified/unauthenticated provider fixture. The policy fixture permits exactly one
named harmless test capability for one verified principal and denies every other request. This proves
propagation and ordering; it establishes no GM, campaign, player-control, or social policy.

### Mandatory behavior

- Missing, malformed, expired, unverified, ambiguous, wrong-tenant, inactive, or unavailable
  provider context produces a stable deny before application. Provider outage follows the approved
  fail-closed decision; it cannot degrade to anonymous access.
- The interceptor runs once per external request at the approved boundary. Retried requests retain
  ordinary idempotency/correlation behavior but are authorized anew unless the approved decision
  record explicitly defines a short-lived verified-context cache.
- An allowed result cannot be replayed for a different operation, scope, principal, transport, or
  correlation. A denial never reaches mechanic selection/projection and never creates game state,
  event, notification, execution, or success audit evidence.
- Audit records approved safe attempt/decision correlation for allow and deny. Client responses are
  transport-parity safe and do not distinguish hidden-resource absence from forbidden access unless
  the approved decision record permits that disclosure.

### Slice 1 acceptance matrix

| Area | Required proof |
| --- | --- |
| Trusted source | Verified principal reaches policy; caller-supplied principal/profile/campaign/`isGm` fields are ignored or rejected and cannot change the decision. |
| Deny default | Missing, malformed, expired, invalid-signature, wrong-issuer/tenant, inactive, ambiguous, and provider-unavailable contexts deny before application. |
| Ordering/atomicity | Instrument mechanic lookup, projection, effect/event/notification generation, persistence, cache, operation receipt, and commit to prove a denial touches none; fault injection during authorization leaves no partial result. |
| Scope binding | A decision for one capability/scope/correlation cannot authorize another; canonical IDs remain opaque and display-name changes do not alter identity. |
| Privacy/audit | Approved minimal audit evidence is present for allow/deny; logs/responses contain no raw credential, hidden membership, policy expression, or unauthorized entity detail. |
| Transport parity | The selected command/read adapters produce the same stable decision category and safe recovery for equivalent verified, anonymous, and invalid inputs. |
| Repository | Focused tests and full suite pass; security-sensitive configuration review, whitespace search, and diff check pass. |

### Exit gate

Stop after the two adapter fixtures prove the shared boundary. Do not persist membership, grant a
GM role, add a control relationship, expose user/account operations, or migrate campaign, Feature
38, or CH14 consumers.

## Later slice order

| Slice | Starts only when | Exit gate |
| --- | --- | --- |
| 1. Trusted context and deny-by-default hook | Required decision and `procedure.system.modify` confirmation | One adapter passes canonical authenticated/unauthenticated context to shared policy; missing/invalid context denies before application. |
| 2. Transport/audit parity | Slice 1 plus every production adapter enumerated in the decision record | Every production transport makes the same allow/deny decision and exposes identical safe recovery/audit evidence. |
| 3. Consumer policies | Slice 2 plus each consumer plan | One campaign GM capability, F38 disposition route, or CH14 grant path enforces its declared scope through the one hook. |

## Slice 2 — all-transport parity and recovery proof

For each adapter named in the approved decision record, connect verified context extraction and the
same interceptor. Test equivalent valid, anonymous, expired, revoked, wrong-scope, and provider-
outage requests across transports. Test restart, key-refresh, and session-revocation behavior as the
selected provider documents it, including an approved fail-closed result during uncertainty.

Exercise administrator bootstrap and break-glass only through its approved distinct capability,
expiry, audit, and revocation path. This slice ends when no production path can invoke an application
operation without a reviewed trusted context and decision; it does not add a game policy.

## Consumer-policy handoff requirements

Each later consumer must declare a permanent capability ID, exact scope references, policy-data
owner, read/write/administrative distinction, revocation point, client-safe denial behavior, and
tests through the E9 hook. Campaign GM authority, Feature 38 social adjudication, and CH14 control
grants are separate policy/data features. They must not share an `isGm` boolean, place authority in
profile or campaign attachment data, or bypass the hook for trusted-host workflows.

## Plan-quality audit

- Authentication, authorization, consumer policy, and campaign/character state have distinct
  owners: yes.
- The missing external decision is explicit; no fake identity or catalog-only authority is
  authorised: yes.
- Future slices require denial, scope, stale/revocation, privacy/audit, and transport-parity
  evidence before consumer adoption: yes.
- The approved first slice has closed interfaces, explicit context lifetime, ordering, failure,
  privacy, test, and stop conditions without claiming a game policy: yes.

## Plan-change rule

Do not start implementation with a caller-supplied principal ID, a profile name, a campaign
reference, a browser-only guard, or a static “trusted” feature flag. Re-plan if the provider or
authorization model changes; security semantics cannot be migrated as routine catalog data.

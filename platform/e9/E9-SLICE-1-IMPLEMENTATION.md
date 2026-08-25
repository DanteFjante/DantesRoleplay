# E9 Slice 1 implementation — private-operator trusted context and deny-default hook

Status: **accepted for the private-host profile**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [E9 trusted context and deny-by-default hook](E9-DEPENDENCY-PLAN.md)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Translate the accepted local/Tailscale web identity into one pseudonymous trusted context
and require a shared private-operator authorization decision before web read or modification logic.  
Exclusions: Accounts, passwords, token issuance, sessions, roles, grants, migrations, public access,
remote MCP, application administration kinds, game/campaign/player policy, and AI work.  
Allowed files/areas: `src/system/authorization/`, web security composition/tests/component metadata,
E9 roadmap/decision/receipt documents, and status-only owner updates.  
Stop point: Stop when local and allowlisted Tailscale web reads/modifications pass one deny-default
hook, invalid identities cannot invoke endpoint logic, and focused/full evidence is recorded.

## Confirmed decisions

[E9-0 private-operator decision](E9-0-PRIVATE-OPERATOR-DECISION.md) closes the provider, principal,
transport, anonymous, bootstrap, policy, and privacy rows for this deliberately private profile.
The accepted [web Slice 5 receipt](../../web/WEB-INTERFACE-SLICE-5-RECEIPT.md) proves the underlying
loopback/Tailscale authentication and remote `/mcp` exclusion.

## Runtime artifacts

- Provider-neutral trusted/unauthenticated principal context, closed read/modify capability class,
  private-host authorization request/decision, policy port, and bounded audit evidence.
- A deny-default single-operator policy: only a verified principal in the exact private-host scope
  may read or modify; missing/wrong scope/authentication denies with a stable safe code.
- A web adapter that derives an opaque principal reference from the accepted access decision,
  evaluates authorization once, and sets `HttpContext.User` only after allow.
- Existing endpoint filters invoke this adapter before endpoint handlers. No route or result schema
  changes on success.

## Authoritative state and closed input

TCP loopback, configured Tailscale host/login allowlist, and proxy-supplied verified login remain
the authentication authorities. HTTP bodies, route/query fields, page content, and MCP tool input
cannot supply a principal, access mode, capability, scope, or authorization decision. The adapter
derives capability from the HTTP method and uses the fixed private-host scope.

## Behavior and failure contract

Authentication resolves first. Allowed identity is pseudonymized, then the shared policy evaluates
read versus modify before endpoint code. Denial returns the existing authentication code when
authentication failed or `PRIVATE_OPERATOR_DENIED` for policy denial. It invokes no handler and
makes no persistent change. Audit evidence is bounded and contains no raw login/header/credential.
Repeated requests are authorized anew; decisions are not cached or replayable.

## Implementation sequence and acceptance

1. Add the generic authorization component contracts, policy, validation, and focused tests.
2. Add the thin web principal/guard adapter and compose it into the existing security filter.
3. Extend web tests for opaque identity, allow/deny, read/modify mapping, and no-handler-on-deny.
4. Run focused authorization/web tests, protocol guards, full shared/local-AI suites, build, and
   `git diff --check`; write the receipt and update E9 status.

Acceptance requires verified local/Tailscale allow, anonymous/wrong-scope deny, opaque stable
principal references, no caller authority field, handler ordering proof, unchanged remote MCP 404,
no migration, and no game/application vocabulary in the authorization component.

## Completion receipt and exit gate

Acceptance evidence is recorded in [the Slice 1 receipt](E9-SLICE-1-RECEIPT.md). Stop before MCP authorization integration or any
application/campaign/player consumer policy.

# E9 Slice 1 receipt — private-operator trusted context and deny-default hook

Status: **accepted for the private-host profile**  
Completed: 2026-08-24  
Decision: [E9-0 private operator](E9-0-PRIVATE-OPERATOR-DECISION.md)  
Accepted implementation: [E9 Slice 1](E9-SLICE-1-IMPLEMENTATION.md)

## Delivered

- Added a ruleset-neutral `authorization` system component with request-lifetime verified or
  unauthenticated principal context, closed read/modify capability, exact private-host scope,
  deny-default policy, stable safe decisions, and bounded audit evidence.
- Required principal references to be opaque lowercase SHA-256 identifiers. The web adapter
  domain-separates and hashes the accepted local-operator marker or normalized verified Tailscale
  login; authorization evidence contains no raw login or identity header.
- Composed the shared policy after the accepted loopback/Tailscale authentication check and before
  every private web endpoint handler. HTTP GET/HEAD maps to read; all other methods map to modify;
  request input cannot nominate either capability, scope, principal, or decision.
- Preserved existing authentication failures and verified that authorization denial never invokes
  endpoint logic or creates an authenticated `HttpContext.User`.
- Kept remote `/mcp` unavailable, loopback MCP unchanged, and added no account, password, token,
  role, grant, migration, game policy, or AI behavior.

## Evidence

- Focused authorization and web tests: 33 passed, 0 failed.
- Protocol/guard and live catalog walk: 14 passed, 0 failed.
- Full shared suite: 554 passed, 0 failed.
- Standalone local-AI suite: 19 passed, 0 failed.
- Solution build: passed with 0 warnings and 0 errors.
- Security vocabulary scan found no game, campaign, player, GM, password, token, or provider model
  vocabulary in the authorization component.
- `git diff --check`: passed; Git emitted line-ending notices only.

## Deliberate exclusions and next gate

This profile does not claim stable cross-provider identity, multi-user accounts, remote MCP,
persistent authorization grants/audit, campaign/GM/player policy, public deployment, or full E9
transport parity. The next application-kernel administrative protocol slice must explicitly adapt
the loopback MCP request into this shared policy and prove read/modify parity before exposing any
administrative `system.*` kind.

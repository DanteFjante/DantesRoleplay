# E9-0 decision — private single-operator identity and authorization

Status: **accepted for the private-host profile**  
Confirmed: 2026-08-24  
Owner: [E9 dependency plan](E9-DEPENDENCY-PLAN.md)  
Ruleset alignment: **ruleset-neutral**

## Decision

The user selected basic authentication and authorization for a server that is not intended for
public deployment, then supplied the accepted private-access implementation as the provider
boundary. This decision adopts that boundary instead of introducing accounts or another token:

| Concern | Private-host decision |
| --- | --- |
| Provider/verifier | Direct access is accepted only from an ASP.NET loopback peer. Private remote web access is verified by Tailscale Serve using the exact configured `.ts.net` host and `Tailscale-User-Login` allowlist. Caller-supplied identity headers are trusted only through that already accepted loopback-proxy/host boundary. |
| Canonical principal | Authorization receives only `principal.<sha256>` derived from a domain-separated local-operator marker or the normalized verified Tailscale login. Raw login remains available only to the existing `/api/session` response and is not copied into authorization evidence. Credential/login changes may change this pseudonym; this profile makes no cross-provider identity-lifecycle claim. |
| Transport trust | Web requests use the accepted local/Tailscale filter. Tailscale requests cannot reach `/mcp`; MCP remains loopback-only and is not changed in the first slice. CLI/background work gains no identity or bypass claim. |
| Anonymous/default | Missing, malformed, disallowed, non-loopback, or unavailable identity fails closed before endpoint logic. There are no anonymous private-web operations. Existing public catalog MCP reads remain unchanged. |
| Bootstrap/recovery | Local operating-system access is the bootstrap/recovery boundary. The foreground launcher derives and allowlists the signed-in Tailscale login; stopping the launcher removes only its own Serve state. Removing a login or disabling remote access revokes the next request. There is no remote break-glass route. |
| Policy/data | One authenticated private operator may perform private-host reads and modifications. No role database, campaign membership, game grant, generic RBAC editor, or caller-selected authority is introduced. |
| Audit/privacy | Authorization context contains the pseudonymous principal, authentication method, request correlation, capability class, decision, and safe reason only. It contains no raw header, login, credential, hidden data, or policy expression. This slice proves the evidence contract without adding persistence or a migration. |

## Limit and re-entry gate

This is intentionally a single-operator private-host profile, not general multi-user E9 acceptance.
Public internet deployment, remote MCP, campaign/player authorization, persistent grants, stable
cross-provider identity, or account recovery requires a new reviewed E9 decision and later slices.


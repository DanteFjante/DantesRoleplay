# Web Interface Feature 1 Slice 5 implementation — private remote access

Status: **accepted — delivered by [Slice 5 receipt](WEB-INTERFACE-SLICE-5-RECEIPT.md)**  
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md)  
Dependency tree/leaf: [Private remote access](WEB-INTERFACE-DEPENDENCY-TREE.md#ordered-leaves)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Allow one operator to reach the locally hosted web interface from their private Tailscale
network while retaining Tailscale user identity and excluding MCP and all other host routes.  
Exclusions: Public internet/Funnel exposure, cloud/container hosting, account/password/OAuth storage,
anonymous or tagged-device access, shared administration, changes to MCP or local AI, hostile-HTML
sandboxing, game-state writes, and Codex as an AI provider.  
Allowed files/areas: `web/`, `src/system/web-interface/`, the shared host's web middleware
composition, focused web tests, and the web component manifest. No database migration or non-web
semantic change.  
Stop point: Focused/full tests and a private-access HTTP walk pass; a foreground launch helper can
start and stop Tailscale Serve without persisting identity or overwriting an existing Serve
configuration; record the receipt and mark Slice 5 accepted.

## Confirmed decisions

- On 2026-08-24 the user asked to continue with Slice 5 after selecting local hosting as the
  realistic default. The selected provider is Tailscale Serve: the ASP.NET host and SQLite remain
  local while private tailnet HTTPS supplies transport and identity.
- Remote mode is opt-in. It requires an exact configured Tailscale DNS hostname and at least one
  allowed Tailscale login. Missing, incomplete, or mismatched configuration fails closed.
- Tailscale identity headers are trusted only from a loopback peer and only when the request Host
  exactly matches the configured Tailscale hostname. Kestrel remains loopback-only.
- Direct loopback access through `localhost`, IPv4 loopback, or IPv6 loopback remains valid and is
  identified as local access.
- A Tailscale-hosted request may reach only `/ui`, `/api/pages`, `/api/data`, `/api/changes`, and a
  read-only `/api/session` description. `/mcp` and every other path fail before endpoint dispatch.
- A foreground PowerShell helper derives the current Tailscale hostname/login at runtime, configures
  the child host without saving personal identity, refuses to replace pre-existing Serve state,
  and resets the mapping when the host exits.

## D&D 5e 2024 alignment

No D&D rule, term, formula, eligibility decision, state, or outcome is introduced.

## External implementation reference

No Foundry review is relevant to ruleset-neutral private HTTP access. Tailscale's official Serve
contract is the implementation reference: Serve is tailnet-private, strips caller-supplied identity
headers before adding verified user headers, and recommends a localhost-only backend when those
headers are trusted.

## Prerequisite evidence

- [Slice 4 receipt](WEB-INTERFACE-SLICE-4-RECEIPT.md) proves the loopback-only web surface, browser
  policy, quotas, and unchanged MCP compatibility.
- `MapDantesRoleplayWeb` owns the complete web route family, while the host separately maps `/mcp`.
- The selected workstation has a running, signed-in Tailscale client and no existing Serve mapping;
  the implementation stores neither its hostname nor its user login in the repository.

## Runtime artifacts

- Web-owned remote-access options with exact host and login validation.
- One host middleware boundary that rejects non-web routes through the remote hostname.
- One endpoint filter that resolves local or Tailscale identity and sets an authenticated principal.
- One read-only `/api/session` endpoint exposing the current access mode and login to authored pages.
- One foreground private-launch helper and revised usage documentation.
- No catalog ID, game schema, mechanic, procedure, MCP kind, database table, index, or migration.

## Authoritative state and closed input

- ASP.NET's TCP peer address remains authoritative: all web requests must arrive from loopback.
- The exact configured Tailscale hostname selects remote mode. The proxy-provided
  `Tailscale-User-Login` header supplies identity; it must exactly match the configured allowlist.
- Callers cannot select access mode, nominate an identity, change the allowlist, or use forwarded
  address headers. The launch helper derives its configuration from the signed-in local client.
- Existing SQLite/page/world owners remain authoritative. Access resolution persists no state.

## Behavior, result, and typed effects

- Direct local web requests retain Slice 4 behavior and receive a local authenticated principal.
- A request for the exact remote hostname succeeds only when remote access is enabled and its
  Tailscale login is allowed; the request receives a Tailscale authenticated principal.
- `/api/session` returns `accessMode` (`local` or `tailscale`) and the Tailscale login only when
  present. It returns no display name, profile image, host configuration, or allowlist.
- Before routing, remote-host requests outside the closed web paths return `404`; this includes
  `/mcp`. Local MCP behavior remains unchanged.
- The foreground helper enables tailnet-private Serve on HTTPS, starts the host on IPv4 loopback,
  and removes only the Serve mapping it created when the host exits.

## Failure, replay, and rollback contract

- Non-loopback peers retain `403 LOCAL_ACCESS_REQUIRED`.
- Unknown `.ts.net` hosts, incomplete remote configuration, absent identity, tagged-device requests,
  and disallowed logins return `403 REMOTE_IDENTITY_REQUIRED` or `403 REMOTE_ACCESS_DENIED` before
  route execution.
- A configured remote hostname on a non-web route returns `404`; no MCP operation is invoked.
- Repeated accepted requests are read-only except for the existing explicit page upload routes.
- Rejected requests make no persistent change. Helper failure or host shutdown resets only the
  newly created Serve mapping; it refuses to overwrite an existing configuration.

## Implementation sequence

1. Add closed remote options, access resolution, remote route boundary, and focused unit tests.
2. Map session description and compose the boundary before the MCP/web endpoints.
3. Add the foreground Tailscale launcher and private-access documentation.
4. Run focused tests, build/full suite, protocol compatibility, HTTP access walk, and write the
   receipt.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Positive | Direct local access and an allowed Tailscale host/login reach every web route. |
| Identity | `/api/session` distinguishes local and Tailscale access without exposing configuration. |
| Negative | Missing/disallowed Tailscale identity and unknown remote hosts fail before route execution. |
| Isolation | The remote hostname cannot reach `/mcp` or any path outside the web allowlist. |
| Boundary | Host matching is exact and case-insensitive; `.ts.net` suffix tricks do not pass. |
| Rollback/replay | Rejections write nothing; the launch helper leaves pre-existing Serve state alone and removes its own mapping. |
| Compatibility | Local web, MCP, database, and local-AI behavior remain unchanged; build and full suite pass. |

## Verification commands

- Focused `WebInterfaceTests`.
- `dotnet build DantesRoleplay.slnx --no-restore`.
- Full solution tests.
- Existing protocol/manifest-guard tests because the shared host gains middleware composition.
- Local and simulated Tailscale HTTP access/identity/isolation walk against a disposable database.
- Launch-helper static checks plus Tailscale Serve status preservation check.
- `git diff --check`.

## Completion receipt and exit gate

Delivered behavior and verification are recorded in
[`WEB-INTERFACE-SLICE-5-RECEIPT.md`](WEB-INTERFACE-SLICE-5-RECEIPT.md). The selected web interface
is complete; public hosting, MCP identity, multi-user administration, and AI-provider refactoring
remain excluded.

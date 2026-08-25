# Web Interface Feature 1 Slice 5 receipt — private remote access

Status: **Verified and accepted; selected web interface complete**

## Delivered boundary

- Added opt-in Tailscale Serve identity handling with an exact `.ts.net` hostname and explicit
  Tailscale-login allowlist. Incomplete, absent, unknown-host, missing-identity, and disallowed-user
  cases fail closed.
- Kept Kestrel loopback-only and retained direct local access. Accepted requests receive an
  authenticated local or Tailscale principal.
- Added `/api/session`, which reports only `local` or `tailscale` access and the accepted remote
  login when applicable.
- Added a host-level remote route boundary. The private hostname may reach only `/ui`, `/api/pages`,
  `/api/data`, `/api/changes`, and `/api/session`; `/mcp` and all other host paths return 404 before
  endpoint dispatch.
- Added a foreground PowerShell launcher that derives the current signed-in Tailscale hostname and
  login without persisting either, refuses to replace existing Serve state, and removes its own
  unchanged Serve configuration when the host exits.
- Updated web ownership, usage, roadmap, and dependency evidence without adding a database schema,
  migration, account store, public host, or AI-provider change.

## Evidence

- Focused web tests: **26 passed**, including local access, exact/case-insensitive hostname matching,
  allowed identity, disabled/missing/disallowed remote identity, authenticated principals, and the
  web-only remote route list.
- Solution build: **succeeded with 0 warnings and 0 errors**.
- Protocol and manifest-guard compatibility checks: **13 passed**.
- Full suite: local-AI **19 passed**; shared suite **547 passed**, with no failures.
- Real Tailscale Serve HTTPS walk:
  - `/api/session` returned `200`, `accessMode: tailscale`, and the current signed-in identity;
  - the same private hostname returned `404` for `/mcp`;
  - the response included the web content-security policy;
  - direct loopback `/api/session` returned `200` and `accessMode: local`;
  - missing and disallowed remote identities both returned `403`;
  - the temporary Serve mapping was reset and the prior empty configuration was restored.
- Launcher syntax check: **0 parser errors**. The selected client was running with one resolvable
  signed-in user; no hostname or login was written to the repository.
- `git diff --check`: **passed**; reported only existing line-ending conversion notices.

## Deployment state

Private deployment is implemented and was verified over real tailnet HTTPS. It is intentionally not
left running after verification. Run `src/system/web-interface/scripts/Start-PrivateWeb.ps1` to make
it available for the duration of the foreground host process.

## Deliberate exclusions

No Tailscale Funnel/public-internet exposure, always-on service installation, cloud/container host,
account/password/OAuth database, tagged-device or anonymous access, shared identity administration,
MCP authentication/exposure change, hostile-content sandbox, game-state write endpoint, D&D rule,
or Codex/local-AI provider replacement was added.

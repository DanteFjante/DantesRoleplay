# Control-center Slice 11 — root entry route

Status: **accepted**  
Ruleset alignment: **ruleset-neutral**  
Source: **not applicable**

## Outcome

Serve the currently active `control-center` page bundle at `GET /`, so the operator can open the
control center without typing a `/ui/...` path. The existing direct page URL
`/ui/control-center/index.html` remains available as a recovery and revision-scoped page route.

## Confirmed boundary

The 2026-08-24 request confirms this public web-route addition. It does not change the MCP route:
`/mcp` remains owned by the MCP host and remains outside the private remote web surface.

## Ownership and design

- `IWebPageStore` remains the authority for the active page revision. The root handler reads the
  existing fixed page ID `control-center`; it does not seed, copy, upload, or cache HTML.
- `WebInterfaceEndpoints` owns route adaptation. `GET /` delegates to the existing page response
  path and keeps its content type, 404 behavior, endpoint filter, and read rate-limit policy.
- `WebAccessPolicy` owns the remote route allowlist. It must admit `/` under the existing
  authenticated private-web boundary while continuing to reject `/mcp`.
- The browser bundle, page upload API, generic `/ui/{id}` routes, and control API route family are
  unchanged. A root request is not a redirect and does not introduce caller-selected page IDs.

## Allowed changes

- Root route and fixed control-center lookup in `WebInterfaceEndpoints`.
- Root-path admission in the existing remote web-route policy.
- Focused route/policy tests, operator README wording, this plan, dependency/roadmap status, and a
  completion receipt.

## Exclusions

- No database migration, new page ID, startup seeding, catalog change, or synchronization/upload.
- No change to MCP exposure, Tailscale identity checks, page-revision notifications, or generic
  page upload response URLs.
- No static-file fallback, reverse-proxy configuration, hosting/deployment, or frontend rewrite.

## Failure and security behavior

- If no active `control-center` page is stored, `GET /` returns the existing page-not-found result.
- Root receives the same existing web read authorization, security filter, and rate limit as
  `/ui/control-center/index.html`.
- Remote candidates may reach `/` only after the existing private access policy accepts their
  identity. `/mcp` remains denied by the remote web-route policy.

## Acceptance evidence

- Route-map coverage proves `GET /` is registered by the web endpoint mapper.
- Remote boundary coverage proves `/` is admitted and `/mcp` remains rejected.
- Focused web tests and the full solution test suite pass, apart from any documented unrelated
  baseline failure.

## Delivered evidence

[Slice 11 receipt](WEB-CONTROL-CENTER-SLICE-11-RECEIPT.md) records the accepted root route,
remote-boundary coverage, and verification results.

# Control-center Slice 12 receipt — reachable page navigation

Status: **accepted**  
Date: **2026-08-24**  
Scope: **ruleset-neutral web navigation**

## Delivered boundary

- Root now reads the active `home` page through the existing protected page-store path.
- Home links directly to `/ui/control-center/index.html`.
- Site Editor renders an `Open live page` link for every listed page using the existing direct,
  URI-encoded `/ui/{id}/index.html` route.

## Live synchronization and verification

- Rebuilt and restarted the local MCP/web server against its established
  `DantesRoleplay.MCPServer/data/dantesroleplay.db` store.
- Published reviewed `home` revision 3 and `control-center` revision 2 through the existing page
  upload endpoints.
- Local HTTP checks returned 200 for `/`, `/ui/home/index.html`, and
  `/ui/control-center/index.html`; the returned home HTML contains the control-center link and the
  returned control-center HTML contains the live-page-link behavior.
- Focused web tests passed: 67/67. The full solution test command completed successfully from an
  isolated output location, together with the 20/20 local-AI tests; isolated outputs avoided the
  running server’s executable lock.
- `git diff --check` passed (line-ending notices only).

## Exclusions

No page-list schema, migration, catalog record, generic page route, MCP route, remote policy,
hosting, or deployment was changed. This remains a small launcher/navigation surface, not a new
general routing or application-registration system.

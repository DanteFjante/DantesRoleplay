# Control-center Slice 12 — reachable page navigation

Status: **accepted**  
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md)  
Dependency tree/leaf: Control-center dependency plan, Slice 12  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**

## Outcome and boundary

Make the root website a home page with a direct control-center entry, and make every existing page
listed by Site Editor directly reachable. The user confirmed this navigation change on 2026-08-24.

The root route reads the existing active `home` page. The home page links to the direct
`control-center` page. Site Editor adds an ordinary live-page link for each listed existing page,
while retaining revision preview, draft, publish, rollback, and download behavior.

## Exclusions

- No page list endpoint/schema change, new page IDs, migration, catalog change, page-layout model,
  dynamic navigation registry, or application embedding.
- No generic change to `/ui/{id}` behavior, page activation rules, MCP, access identity, or remote
  route policy.
- No hosting or deployment; the pages remain locally/private hosted and database-authored.

## Prerequisite evidence

- Slice 4 established bounded page discovery and immutable revision/page controls.
- Slice 11 established a root route through the active page store under the existing read boundary.
- `home` and `control-center` are existing page IDs; their source bundles are already maintained by
  the web-interface owner.

## Runtime artifacts and authoritative state

- `IWebPageStore` remains authoritative for active `home` and `control-center` revisions.
- `WebInterfaceEndpoints` changes only its fixed root page ID from `control-center` to `home`.
- The existing page-list projection is authoritative for Site Editor. It supplies each existing
  page ID, which is URI-encoded into the existing direct page route.
- Source bundles remain reviewed file artifacts until this explicit local synchronization boundary
  uploads and activates their next immutable revisions in the running page store.

## Behavior and failure contract

- `GET /` retains the existing security/filter/rate limit and returns the active `home` HTML, or
  the existing 404 when no active home page exists.
- Home provides root/overview navigation and a direct `/ui/control-center/index.html` link.
- Site Editor provides `Open live page` for every listed page at `/ui/{encoded-id}/index.html`.
  Opening a page is a navigation only; it does not publish or modify a revision.
- Missing/deleted/unavailable pages keep the existing 404 behavior. Existing Site Editor load and
  authorization failures remain unchanged.

## Allowed files and stop point

- `WebInterfaceEndpoints.cs`, focused web tests, `examples/home.html`,
  `examples/control-center/index.html`, web README, this plan, dependency/roadmap status, and a
  receipt.
- Upload the reviewed home and control-center bundles to the user-running local server after source
  verification.
- Stop once root maps home and both direct navigation surfaces are verified. Do not add page
  creation, applications, routing frameworks, or further navigation features.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Root | The mapper has one read-only `GET /` route and uses active `home`. |
| Home | Source contains direct root/overview and control-center links. |
| Site Editor | Source renders a URI-encoded live-page link for each listed item. |
| Boundaries | `/ui` remains the direct route; `/mcp` is absent from web mapping and remote policy is unchanged. |
| Running server | Published home and control-center revisions return 200 through their direct routes. |

## Verification commands

- Focused `WebInterfaceTests`.
- Full solution test suite and `git diff --check`.
- Local HTTP checks after bundle synchronization; restart the host before checking root route code.

## Completion receipt and exit gate

Record source/test evidence and the two activated page revisions in
`WEB-CONTROL-CENTER-SLICE-12-RECEIPT.md`. The slice ends at reachable home/control-center/page
links; it does not create a general navigation model.

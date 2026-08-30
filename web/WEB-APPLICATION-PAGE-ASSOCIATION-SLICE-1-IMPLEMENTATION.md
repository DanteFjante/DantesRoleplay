# Application-page association Slice 1 implementation — automatic direct application pages

Status: **implemented; acceptance confirmation pending 2026-08-27**
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), application-aware workspace follow-on
Dependency evidence: [Application-aware workspace Slice H receipt](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-H-RECEIPT.md), confirmed application-to-page association boundary
Ruleset alignment: **ruleset-neutral**
Source ID and locator: **not applicable**
Outcome: every registered application receives the deterministic direct page URL
`/ui/<application-id>-play`; shared navigation links to that URL rather than the control center.
An existing authored page at that ID remains authoritative (including D&D's existing
`dnd2024-play` page). Where no authored page exists, the existing web route serves a generic,
read-only application landing page after exact registry verification.
Exclusions: application registration payload changes, application/page database association records,
migrations, new HTTP routes or MCP kinds, application/source activation, state-space creation or
migration, page uploads, game rules, D&D-specific branching, entity reads, actions, and writes.
Allowed files/areas: web page/path helpers, existing web page route, shared navigation browser
component, focused web tests, this implementation document/receipt, and roadmap status.
Stop point: the D&D navbar opens `/ui/dnd2024-play`; any other registered application has a
safe direct landing page at its derived URL; unknown/invalid pages retain normal 404 behavior.

## Confirmed decisions

The user's 2026-08-27 request confirms the application-to-page association contract previously
deferred by the application-aware workspace plan: a newly created application is automatically
assigned a direct page and the navbar must reference it. The association is a deterministic generic
convention, `pageId = applicationId + "-play"`, not a new durable relationship or a D&D special
case. Existing authored pages take precedence over the generated landing page.

## Prerequisite evidence

- The existing `GET /ui/{id}` and `GET /ui/{id}/index.html` page routes already own page serving.
- `WebPageStore.GetActiveAsync` supplies immutable authored-page truth, while `IApplicationRegistry`
  supplies registered-application truth.
- `<system-navigation>` already discovers registered applications through the existing bounded
  read endpoint but currently hard-codes control-center deep links.
- The D&D page is already an authored active page at the proposed conventional ID
  `dnd2024-play`; no special mapping is required.

## Runtime artifacts

- Add a ruleset-neutral `ApplicationPageId` helper that derives and recognizes the bounded page ID
  from an existing `ApplicationIdentifier`.
- Add a generic HTML landing-page renderer for a registered application with no authored page at
  its derived ID. It receives only the registered ID, display name, and description; all text is
  HTML encoded and it exposes no data/action/administration surface.
- Revise the existing page route to prefer stored active content, then serve the generated landing
  page only for the exact registered application-derived ID; every other missing page remains 404.
- Revise `<system-navigation>` to construct the direct derived page URL for every discovered
  registered application.

## Authoritative state and closed input

Application ID, display name, and description are authoritative registry values. The browser sends
no association ID, page ID, content, registration metadata, source profile, state-space ID, or
game data. The existing route receives a page ID only; the server derives a candidate application
ID from the closed `-play` suffix and checks exact registry membership before rendering a fallback.

## Behavior, result, and typed effects

1. The navbar renders each registered application as `/ui/<encoded-id>-play`.
2. The page route first returns an active authored page if one exists at that ID.
3. If no authored page exists, the route recognizes only a valid conventional page ID, derives its
   application ID, and reads that exact registry record.
4. A registered application receives the neutral landing HTML; an unknown, malformed, or unrelated
   page ID returns 404 unchanged.

All behavior is read-only. No effect, transaction, event, notification, source selection, catalog
lookup, or application/page write occurs.

## Failure, replay, and rollback contract

An invalid page ID, an unregistered derived application, a missing page, registry failure, or
cancelled request returns the existing safe route failure and writes nothing. A manually authored
page always wins over the generated fallback. Repeated reads are deterministic for the same page
and registry revision, and no retry or stale condition can change a page or registry record.

## Implementation sequence

1. Add focused route/navigation assertions for the direct convention, authored-page precedence,
   registered fallback, and unknown-page 404 boundary.
2. Add the generic helper/renderer and route fallback, then revise shared navigation.
3. Run focused web tests, source syntax checks, build, live route/navbar read-back, and record the
   receipt.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Existing D&D page | `/ui/dnd2024-play` serves the authored game page and navigation points there. |
| New registered application | Its derived `-play` URL returns a generic application landing page without a page upload. |
| Authored precedence | A stored active page at a derived URL is returned unchanged instead of the fallback. |
| Isolation | The landing page contains no control-center, D&D, state, source, action, or write behavior. |
| Negative | Invalid/unknown/unregistered page IDs return 404 and perform no write. |
| Compatibility | Home, control-center, stored page revisions/assets, registered application discovery, and route security remain unchanged. |

## Verification commands

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --filter "FullyQualifiedName~WebInterfaceTests" --no-restore`
- `dotnet build DantesRoleplay.slnx --no-restore`
- `git diff --check`
- Local live checks for Home, D&D page, and a registered application's derived page.

## Completion receipt and exit gate

Write `WEB-APPLICATION-PAGE-ASSOCIATION-SLICE-1-RECEIPT.md` after the stated evidence. Stop after
the direct-page convention and navigation correction; persisted custom page ownership, automatic
page uploads, template editing, game-specific page generation, and application creation workflow
changes require separate slices.

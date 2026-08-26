# Application-aware workspace Slice B implementation — shared system navigation

Status: **accepted**  
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 4  
Dependency tree/leaf: [Application-aware workspace](WEB-APPLICATION-AWARE-WORKSPACE-DEPENDENCY-PLAN.md), Slice B  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: add one host-owned browser module and shared navigation element backed by bounded application discovery.  
Exclusions: system chat, application chat changes, action execution, capability catalogs, page/application association, database changes, migrations, and live page activation.  
Allowed files/areas: web-interface component ownership, endpoint mapping and remote route boundary, one new browser element owner, home/control-center/example application HTML, focused web tests, and Feature 4 documents.  
Stop point: focused tests and receipt complete; stop before Slice C.

## Confirmed decisions

The accepted parent plan and the user's instruction to continue confirm the permanent public module
route `/components/system-workspace.js` and permanent custom-element ID `<system-navigation>`.
The element's optional `application-id` attribute is presentation-only: it may identify the current
application link but grants no scope or authority.

Application links use the existing control-center deep link
`/ui/control-center/index.html#/applications/{encoded application ID}` until a later slice defines
page/application association. Home and Control center remain available when application discovery
is empty or unavailable. The control center retains its distinct internal workspace navigation.

## Prerequisite evidence

- [Slice A receipt](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-A-RECEIPT.md) proves two applications are
  discoverable through the existing registry surface.
- `ControlStructureExplorer.ListApplications` owns bounded pagination and opaque cursors.
- `WebInterfaceEndpoints` and `WebAccessPolicy` own web route mapping, local/private access, and
  read rate limiting.
- `ApplicationConversationElement` provides the established host-served custom-element pattern;
  Slice B does not change its behavior.

## Runtime artifacts

- New route: `GET /components/system-workspace.js`, secured by the existing read filter/rate limit
  and available through the existing private remote web boundary.
- New custom element: `<system-navigation application-id="optional-id">`.
- Existing read owner: `GET /api/control/structure/applications?limit=100&cursor=...`.
- No database record, catalog ID, schema, migration, or authorization capability is added.

## Authoritative state and closed input

The application registry is authoritative for displayed applications. The element accepts only an
optional bounded application ID for selected-state presentation. It derives route state from the
current URL and receives display name/description from the server. It accepts no endpoint, method,
provider, prompt, authorization, file, SQL, action, or effect input.

## Behavior

1. Render Home and Control center immediately so navigation survives discovery failure.
2. Fetch application pages sequentially with `limit=100`, following opaque `nextCursor` values.
3. Bound discovery to 10 pages and 1,000 unique application IDs; reject a repeated cursor or
   malformed response as unavailable rather than looping.
4. Sort the complete unique application set by display name and stable ID.
5. Render safe DOM nodes with `textContent`; application hrefs use `encodeURIComponent`.
6. Mark Home, Control center, or the matching application as `aria-current="page"`, updating for
   `hashchange` and `popstate`.
7. When empty, keep the fixed links and expose “No applications registered.” When unavailable,
   keep them, show a retry button, and expose only a stable safe error code.
8. Dispatch bubbling/composed `system-progress` events for `loading` and `ready`, and a
   `system-error` event with `APPLICATION_DISCOVERY_UNAVAILABLE` for failure.
9. Expose CSS custom properties and stable `part` names without inheriting host execution logic.

The browser performs no mutation. Each request remains an independently authorized bounded read.

## Failure, replay, and rollback contract

Malformed JSON, non-success status, non-array items, invalid IDs, duplicate cursors, or page/count
limit exhaustion produces the unavailable state without hiding Home or Control center. Retry starts
a fresh read and cannot mutate state. Reconnecting an element removes old listeners before adding
new ones and does not duplicate navigation. Because the slice is read-only, rollback is removal of
the route/imports; no durable data needs reversal.

## Implementation sequence

1. Declare this exact active boundary and close Slice A evidence.
2. Add the generic element owner and private read-only route.
3. Replace duplicated global navigation on Home and compose the same element into Control center
   and one application-page fixture while preserving page-specific navigation.
4. Add focused route, script-contract, composition, empty/paged/unavailable, remote-boundary, and
   authority-negative tests.
5. Run focused verification, inspect the diff, write the receipt, and stop.

## Acceptance matrix

| Area | Required evidence |
| --- | --- |
| Route | Exactly one GET module route uses web security and read rate limiting. |
| Base navigation | Home and Control center render before registry discovery and remain on errors. |
| Applications | Opaque pagination is followed within closed limits; labels are server-owned and links are encoded deep links. |
| States | Current, empty, loading, unavailable, retry, hash-change, and reconnect behavior are explicit. |
| Composition | Home, Control center, and application fixture import the same module and host the same element; inline global application navigation is absent. |
| Authority | Script contains no MCP, SQL, model/provider, filesystem, mutation method, raw endpoint attribute, or application rule vocabulary. |
| Remote access | The component route is allowed remotely while `/mcp` remains unavailable. |
| Compatibility | Existing application conversation route and control-center internal router remain unchanged. |

## Verification commands

- Focused `WebInterfaceTests` for shared workspace route, element contract, composition, and remote
  route boundary.
- Existing control-center shell/application deep-link and home-dashboard tests.
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore` with a focused filter.
- `git diff --check` over Slice B files.

No catalog validation, protocol walk, migration run, live database mutation, or full suite is
required because this slice changes only a read-only web module and example composition. Full
cross-slice acceptance remains Slice H.

## Completion receipt and exit gate

Record delivered files, focused commands/results, public identifiers, and deliberate exclusions in
`WEB-APPLICATION-AWARE-WORKSPACE-SLICE-B-RECEIPT.md`. Mark Slice B implemented and awaiting user
acceptance, then stop before Slice C.

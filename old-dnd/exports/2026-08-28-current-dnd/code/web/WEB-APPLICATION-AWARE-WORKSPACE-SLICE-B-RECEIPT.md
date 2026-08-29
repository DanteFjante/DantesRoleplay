# Application-aware workspace Slice B receipt — shared system navigation

Status: **accepted by user instruction to continue on 2026-08-25**  
Completed: **2026-08-25**  
Implementation: [Slice B](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-B-IMPLEMENTATION.md)  
Parent: [Application-aware workspace dependency plan](WEB-APPLICATION-AWARE-WORKSPACE-DEPENDENCY-PLAN.md)  
Ruleset alignment: **ruleset-neutral web infrastructure**

## Delivered boundary

- Added the private, read-only `GET /components/system-workspace.js` module route through the
  existing web security filter and read rate limiter.
- Added the permanent `<system-navigation>` browser element. It renders Home and Control center
  before discovery, follows the existing application's opaque pagination, builds encoded
  application deep links, updates selected state, and has bounded empty/unavailable/retry behavior.
- Added bubbling and composed `system-progress` and `system-error` events, stable CSS variables,
  and `::part` seams without giving the element action or model authority.
- Composed the same element into the authored home page, control center, and an application-page
  fixture. The control center's internal workspace router remains independent and unchanged.
- Extended the private remote-web allowlist to the `/components` path segment so both host-served
  component modules load through Tailscale Serve while `/mcp` remains unavailable.
- Declared shared browser-native workspace/navigation ownership in the existing `web-interface`
  component; no new system directory, database owner, application, or game rule was created.

## Closed behavior and authority evidence

Application discovery is limited to 100 items per request, ten pages, 1,000 unique application
IDs, and 1,024-character opaque cursors. Repeated cursors, duplicate or malformed IDs, malformed
pages, failed HTTP responses, and bound exhaustion fail into one safe unavailable state while Home
and Control center remain usable. Retry starts a new aborted-safe read.

The element accepts only an optional `application-id` for selected-state presentation. It does not
accept a URL, method, provider, prompt, authorization token, SQL, filesystem path, action, or effect;
it contains no MCP or application/game vocabulary and performs no mutation.

## Verification

- Focused Slice B acceptance and compatibility set: **7 passed, 0 failed**.
- All `WebInterfaceTests` plus `ApplicationConversationTests`: **96 passed, 0 failed**.
- The component module passed Node syntax checking and a registration smoke check for the exact
  `system-navigation` custom-element ID.
- The solution test build completed successfully during the focused run.
- Scoped `git diff --check` passed; line-ending notices were informational only.

## Deliberate exclusions and next gate

This slice does not activate a new live page revision, add system or application chat behavior,
associate pages with applications, add a system capability catalog, execute actions, mutate the
normal database, add migrations, or change the existing application conversation contract. Page
activation and exact application-chat composition remain Slice G; combined live/browser acceptance
remains Slice H.

The user accepted Slice B by instructing implementation to continue. Slice C may proceed under its
own bounded implementation document.

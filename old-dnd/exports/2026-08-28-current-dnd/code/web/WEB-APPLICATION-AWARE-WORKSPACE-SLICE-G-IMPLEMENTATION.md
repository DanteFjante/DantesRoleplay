# Application-aware workspace Slice G implementation — scoped page composition

Status: **accepted**  
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 4  
Dependency tree/leaf: [Application-aware workspace](WEB-APPLICATION-AWARE-WORKSPACE-DEPENDENCY-PLAN.md), Slice G  
Ruleset alignment: **ruleset-neutral page composition**  
Source ID and locator: **not applicable**  
Outcome: compose the accepted shared navigation, general system chat, and existing application
conversation into the authored home, control-center application workspace, and application-page
fixture while preserving exact system/application authority separation.  
Exclusions: new routes, custom-element IDs, page/application association records, migrations,
capabilities, model behavior, system/application coordinator changes, game rules, application ECS
changes, live normal-database page activation, and combined feature acceptance.  
Allowed files/areas: authored web-interface example pages, web-interface component ownership text,
focused web/page-composition tests, Feature 4 planning status, and the Slice G receipt.  
Stop point: source composition, exact-scope tests, extracted-script checks, disposable-host real
browser verification, focused regressions, build, receipt, and acceptance request complete; stop
before Slice H and normal live-page activation.

## Confirmed decisions

The parent plan and the user's direction to continue with Slice G confirm use of the already public
`/components/system-workspace.js`, `/components/application-conversation.js`,
`<system-navigation>`, `<system-chat>`, and `<application-conversation>` surfaces. No new permanent
ID or route is introduced.

The parent deliberately defers an application-to-page association contract. Therefore registered
application navigation continues to target the existing control-center deep link
`#/applications/{encodedApplicationId}`. Slice G makes that exact workspace conversational; it does
not invent per-application URLs or infer a page from an application name.

The authored examples are updated here. The normal SQLite-backed active pages remain unchanged
until Slice H's separately confirmed combined live-smoke boundary.

## Prerequisite evidence and owners

- [Slice A receipt](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-A-RECEIPT.md) proves the reviewed
  applications and exact state spaces exist and are discoverable through the current structure
  surface.
- [Slice B receipt](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-B-RECEIPT.md) proves shared navigation,
  bounded application discovery, deep links, theming, and resilient unavailable behavior.
- [Slice D receipt](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-D-RECEIPT.md) proves `<system-chat>` has a
  distinct durable system scope and accepts no application/state binding.
- [Slice F receipt](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-F-RECEIPT.md) proves all shared system
  controls coexist in the accepted module without expanding page authority.
- `ApplicationConversationService` remains the exact application/state-space conversation owner.
  It verifies the operator, rejects state-space/application mismatch, and retains execution behind
  its existing explicit confirmation path.
- `web-interface` owns page composition, selectors, accessible status, module loading, and
  presentation only.

## Composition artifacts

### Home

Retain the personalized green/vine dashboard, browser-local notes, clock, and existing application
selector. Add one visibly distinct general `<system-chat>` card without attributes. The existing
application chat remains mounted only after an application and one of its server-discovered state
spaces are selected.

### Control center

The Assistants workspace hosts one general `<system-chat>` section alongside the existing advisory
local/Codex conversations. It receives no application, state-space, session, provider, model, route,
or authorization attributes.

The existing application deep link continues to load the ECS/catalog workspace. After the exact
application and its state spaces are returned by the server, add an application-chat section whose
selector contains only those returned state-space IDs. Mount `<application-conversation>` with the
exact routed application ID, selected state-space ID, and a fresh bounded browser session-context
ID. Changing the state space replaces the ephemeral conversation rather than reusing it across
scope.

### Application-page fixture

Demonstrate shared navigation, one system chat without bindings, and one application conversation
with explicit application, state-space, and session-context attributes. The fixture remains authored
evidence, not a new live page association or game-state authority.

## Authoritative state and closed input

Application IDs come from the already bounded registry discovery or the existing validated route.
State-space choices come only from the selected application's structure response. Browser code may
choose among those values and generate an opaque session-context ID; it cannot provide application
state, effects, schemas, contracts, tools, model configuration, authorization, or confirmation
truth.

`<system-chat>` receives no application or state-space value. `<application-conversation>` receives
all three of its existing exact binding values. The two elements have separate routes and cannot
call one another as fallback.

## Behavior and failure contract

1. Navigation continues to make every registered application reachable at the existing deep link.
2. System chat remains usable independently of the selected application workspace.
3. Application chat is not mounted until an exact discovered state space exists.
4. Changing the selected state space removes the old element and creates a fresh exact binding.
5. Empty state-space results display a no-change message and no application conversation.
6. Stale route/application results are discarded by the existing route check.
7. Component errors remain local to their card/section and do not disable shared navigation or
   unrelated workspaces.
8. No page composition automatically sends a turn, prepares an action, confirms a proposal, or
   writes application/system state.

## Implementation sequence

1. Accept Slice F and activate this exact document.
2. Compose the no-attribute general system chat into home and the control-center Assistants panel.
3. Add exact discovered-state application chat to the control-center application deep link.
4. Complete the application-page fixture with both isolated chat surfaces.
5. Add focused source-contract and scope-isolation tests.
6. Verify syntax, disposable-host rendering, accessibility, deep-link binding, compatibility, and
   build; write the receipt and stop before H.

## Acceptance matrix

| Case | Required evidence |
| --- | --- |
| Home system scope | One visible `<system-chat>` has no application/state/provider/model binding and does not replace notes or application chat. |
| Control system scope | Assistants renders system chat independently of local/Codex advisory conversations and without application bindings. |
| Application route | Each bounded navigation deep link opens the existing exact application workspace. |
| State selection | Only state spaces returned for that exact application appear; empty results mount no chat. |
| Application binding | Mounted application chat carries exact application, selected state-space, and fresh session-context values. |
| Scope change | Selecting another state space replaces the conversation element and session context. |
| Fixture | Application-page fixture loads both accepted modules and demonstrates system/application isolation. |
| No automatic action | Initial render and selection create at most an ephemeral conversation; no turn, proposal confirmation, or execution occurs. |
| Accessibility | Chat regions, selectors, labels, status messages, and navigation remain keyboard/screen-reader discoverable. |
| Compatibility | Existing home notes/clock/app picker, control workspaces, assistant conversations, ECS reads, and component routes remain intact. |
| Isolation | No new API, MCP, migration, catalog, game rule, live database write, or app-to-page association exists. |

## Verification commands

- focused `WebInterfaceTests` and `ApplicationConversationTests`;
- extracted system-workspace and application-conversation JavaScript syntax validation;
- disposable-host real-browser smoke for home, Assistants, an application deep link, exact selector
  binding, component isolation, and accessible labels/status;
- `dotnet build DantesRoleplay.slnx --no-restore --nologo`;
- scoped `git diff --check`.

No catalog, migration, MCP, dependency-registration, or live normal-database artifact changes in
this slice, so catalog validation, protocol walk, migration checks, and live activation are not
required. Full combined tests, privacy audit, and live activation remain Slice H.

## Completion receipt and exit gate

Record exact composition, scope evidence, browser observations, focused counts, normal-database
non-use, and exclusions in `WEB-APPLICATION-AWARE-WORKSPACE-SLICE-G-RECEIPT.md`. Mark G implemented
awaiting acceptance and stop before Slice H.

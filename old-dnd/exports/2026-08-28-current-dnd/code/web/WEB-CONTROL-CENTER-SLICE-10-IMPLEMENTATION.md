# Web Interface Feature 2 Slice 10 implementation — persistent application workspace

Status: **accepted**  
Receipt: [Slice 10 receipt](WEB-CONTROL-CENTER-SLICE-10-RECEIPT.md)  
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md)  
Dependency tree/leaf: [Control-center dependency plan](WEB-CONTROL-CENTER-DEPENDENCY-PLAN.md), persistent sidebar and application workspace  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: not applicable  
Outcome: Replace the long scrolling control-center layout with a persistent navigation sidebar and
one routed main workspace; choosing a registered application opens its existing structure view in
that workspace without removing navigation.  
Exclusions: embedding or launching an application's own web page, application-to-page mappings,
new server routes/capabilities/records, iframe or `postMessage` authority, backend persistence,
catalog/game-state mutation, frontend dependencies/build tooling, and changes to existing panel
semantics.  
Allowed areas: `src/system/web-interface/examples/control-center/index.html`, its focused web tests,
web component metadata/README when wording changes, and this plan/roadmap/receipt set.  
Stop point: closed hash routing, persistent desktop sidebar, usable compact/mobile navigation,
single visible workspace, application deep-link/back-forward behavior, and regression evidence pass;
stop before any application website content is embedded.

## Confirmed decisions

The user's 2026-08-24 request confirms this client-visible navigation contract after the distinction
between structure view and embedded application websites was explained. This slice takes the
recommended simplest alternative: existing application/ECS/contracts evidence opens inside the
control center. A later embedded website would require its own application-to-page and isolation
contract.

No custom-element ID, control capability, HTTP route, request/response schema, database record, or
migration changes. The confirmed client routes are `#/settings`, `#/effects`, `#/assistants`,
`#/applications`, `#/applications/{encodedApplicationId}`, and `#/site-editor`.

## Prerequisite evidence

- [Slice 1 receipt](WEB-CONTROL-CENTER-SLICE-1-RECEIPT.md) owns the independently loading control
  shell and stable custom-element IDs.
- [Slice 3 receipt](WEB-CONTROL-CENTER-SLICE-3-RECEIPT.md) owns bounded application/ECS/schema and
  explicitly public catalog reads.
- [Slice 9 receipt](WEB-CONTROL-CENTER-SLICE-9-RECEIPT.md) verifies every existing panel and the
  completed assistant approval controls before this presentation-only refactor.

## Runtime artifacts

- Recompose the existing page as a responsive shell with one navigation `<aside>` and one `<main>`
  workspace. Preserve `server-settings`, `effect-history`, `assistant`, `ecs-explorer`, and
  `site-editor` as the exact custom-element IDs consumed by status and focused tests.
- Add closed, client-only hash parsing and route selection. Unknown, malformed, empty, or oversized
  routes deterministically show Settings without issuing an application read.
- Add an active navigation indication using `aria-current="page"`, a visible workspace heading,
  and a compact/mobile layout that keeps navigation reachable without horizontal page overflow.
- Extend `<ecs-explorer>` only with route-aware application selection. Existing application,
  state-space, entity, component, schema, and catalog reads and display helpers remain authoritative.

## Authoritative state and closed input

The fixed route table owns panel selection. A route may supply one URL-decoded application ID only;
the existing structure endpoint validates whether it exists and remains authoritative for all
displayed application data. Hash text cannot select a server route, capability, provider, schema,
state space, entity, component, or catalog record directly.

The current local/Tailscale operator boundary, same-origin fetches, panel status projection, and
each panel's mutation contract are unchanged. Hidden panels stay connected so in-progress local UI
state is retained when the operator changes workspace.

## Behavior

1. The initial empty hash renders Settings and normalizes the URL to `#/settings` without a page
   reload. A known hash selects exactly one custom-element workspace.
2. Navigation links change the hash. Hash changes and browser back/forward update the active link,
   workspace heading, and hidden state without reconstructing the panels.
3. `#/applications` shows the application list. Choosing an application writes only
   `#/applications/{encodedApplicationId}` and loads its existing structure inside the same
   `<ecs-explorer>` workspace.
4. A direct application deep link waits for the application list read, then opens the requested
   structure. Returning to `#/applications` clears detail and restores the cached list.
5. Desktop navigation remains sticky at the left. Narrow layouts place a compact, horizontally
   scrollable navigation band above the workspace while maintaining keyboard access and visible
   focus/active state.

## Failure, replay, and rollback contract

- Unknown/malformed/oversized hashes fall back to Settings and make no application request.
- Missing application IDs display the existing bounded unavailable detail; they do not affect the
  shell, history, or other panels.
- Repeated selection and hash replay do not create backend writes. Back/forward never reloads the
  document or duplicates custom elements.
- A panel failure remains isolated and retryable. Switching workspaces does not grant capabilities,
  alter request bodies, or bypass Host/Origin/operator checks.
- Removing this page revision rolls back the feature completely; there is no new durable runtime
  state.

## Implementation sequence

1. Add focused structural assertions for the shell, closed routes, stable panel IDs, active-state
   accessibility, and route-aware application selection.
2. Recompose HTML/CSS into the responsive sidebar/workspace without changing panel implementations.
3. Add the closed router and minimal `<ecs-explorer>` route bridge.
4. Run focused web tests, clean build, full suite, and whitespace checks; record acceptance once.

## Acceptance matrix

- Each fixed navigation choice displays exactly its existing panel and exposes `aria-current`.
- Empty/invalid routes fall back safely; browser hash changes do not reload or duplicate panels.
- Application list selection and a direct encoded application hash open structure in the main
  workspace while sidebar navigation remains present.
- Returning to Applications restores the list; missing IDs leave the shell usable.
- Desktop and narrow CSS preserve reachable navigation and a single non-overflowing workspace.
- Existing settings, effects, assistants/approvals, ECS/contracts, editor, security, and API tests
  remain unchanged and pass.

## Verification commands

- focused `WebInterfaceTests` selection;
- `dotnet build DantesRoleplay.slnx --no-restore --verbosity:minimal`;
- `dotnet test DantesRoleplay.slnx --no-build --logger "console;verbosity=minimal"`;
- `git diff --check`.

No catalog validation, migration drift run, MCP protocol walk, live model call, remote publish, or
browser automation is required because this slice changes only the existing local page bundle and
adds no catalog/database/protocol surface.

## Completion receipt and exit gate

Record evidence in `WEB-CONTROL-CENTER-SLICE-10-RECEIPT.md`, mark Slice 10 accepted in the dependency
plan and roadmap, and stop before embedding application website content or adding a new server
contract.

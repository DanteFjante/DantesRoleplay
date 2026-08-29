# Personal dashboard Slice 0 implementation — local outer chat, notes, and clock

Status: **accepted**  
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md)  
Dependency tree/leaf: Existing `home` page under the accepted web-interface page-authoring boundary; this is a small same-owner content slice.  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**

## Outcome and boundary

Turn the existing `home` page into a private personal dashboard with a minimal green, vine-inspired
visual theme. It provides an entry to the existing application conversation component while the host
selects the already-configured local outer provider, a browser-local notes area, and a live local date
and time display.

The dashboard discovers applications and their state spaces only through existing read-only
`/api/control/structure/*` endpoints. Starting a chat uses the existing application-conversation
component and its existing protected routes. Notes and the most recently selected application/state
space are stored only in the browser's `localStorage` under a namespaced key; no note content is sent
to the server.

## Exclusions

- No database table, migration, catalog record, route, component registration, provider-selection
  API, authentication change, or remote model fallback.
- No game-specific application or state-space identifier is authored into the page.
- No change to conversation planning, execution, consent, learning, receipts, or the selected outer
  provider contract.
- No cross-browser/cross-device note synchronisation; that requires a separately confirmed durable
  personal-data owner.

## Prerequisite evidence

- The existing `home` page is the active root page under accepted web-interface Slice 12.
- `ApplicationConversationElement` already owns browser conversation creation, turns, confirmation,
  execution, and optional learning through protected application routes.
- The existing control structure explorer already exposes paged applications and application-bound
  state spaces to a verified local/private browser.
- The current development host configuration explicitly selects `InteractionOuter:Provider: local`.

## Runtime artifacts and authoritative state

| Concern | Existing owner | This slice's use |
| --- | --- | --- |
| Root page revision | `IWebPageStore`, page ID `home` | Replace only reviewed source HTML and publish a new immutable `home` revision. |
| Local outer selection | Host `InteractionOuter` configuration | Display the existing conversation component; do not select or alter a provider from the browser. |
| Application/state-space list | `ControlStructureExplorer` | Read only for browser selections. |
| Conversation state and execution | `ApplicationConversationService` | Use unchanged custom element and protected endpoints. |
| Personal notes | Browser `localStorage` | Private browser-only draft; never authoritative server state. |

## Behavior and failure contract

1. The clock renders the browser's local date and time immediately and refreshes every second.
2. The notes textarea restores its browser-local value, saves on input, and reports that it is local
   to this browser. Storage errors leave the editable note available and show a local warning.
3. The chat setup reads existing applications, then reads paged state spaces for the selected
   application. It keeps only valid selections and creates the existing `<application-conversation>`
   element after both selections exist.
4. If structure discovery, a missing state space, or the local model prevents a chat, the dashboard
   reports the existing safe error and does not substitute another provider or send a request remotely.
5. Existing conversation confirmation, execution, receipts, and optional route learning remain
   entirely owned by the existing element and service.

## Allowed files and stop point

- `src/system/web-interface/examples/home.html`
- `src/system/web-interface/tests/WebInterfaceTests.cs`
- this implementation document, the web-interface roadmap, and its completion receipt

After focused tests and source review, upload the reviewed self-contained HTML to the existing local
`home` page through its existing page upload route if the local host is available. Stop after the
active root page has the dashboard; do not add durable notes or modify backend contracts.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Theme | Source uses a minimal green palette and decorative vine-only CSS without external assets. |
| Clock | Source exposes an accessible live date/time display and a one-second updater. |
| Notes | Notes use namespaced browser storage, never `fetch`, and remain editable if storage fails. |
| Chat | Source loads the existing `application-conversation` element only after selecting an application and state space from existing read-only routes. |
| Provider safety | Source has no remote-provider URL, API key, provider-selection request, or fallback behavior. |
| Boundaries | Existing root, page, control, MCP, and conversation endpoint mappings are unchanged. |

## Verification commands

- Focused `WebInterfaceTests`.
- Focused `ApplicationConversationTests`.
- `git diff --check`.
- If the running local host is available, upload then fetch `/` and `/ui/home/index.html` to confirm
  the new immutable active revision is served.

## Completion receipt and exit gate

Record the source, test, and any live-page revision evidence in
`WEB-PERSONAL-DASHBOARD-SLICE-0-RECEIPT.md`. This slice ends at the browser-local dashboard;
durable notes, provider control, and new AI capabilities require a later slice.

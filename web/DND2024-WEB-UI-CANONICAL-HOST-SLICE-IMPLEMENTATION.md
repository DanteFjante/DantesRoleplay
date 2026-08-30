# D&D 2024 web UI canonical-host slice implementation — remove prototype iframe

Status: **accepted 2026-08-30**
Superseded: **2026-08-30 by `DND2024-WEB-UI-REACT-SERVER-BUNDLE-SLICE-IMPLEMENTATION.md`**
Owner/roadmap: `web/WEB-INTERFACE-ROADMAP.md`, Feature 5
Dependency tree/leaf: `prototype/dnd2024/planning/DND2024-WEB-INFORMATION-HUB-DEPENDENCY-TREE.md`, H3/H4 local activation correction
Ruleset alignment: **dnd2024-compatible presentation**
Source ID and locator: not applicable; this slice changes hosting only and implements no D&D rule
Outcome: make `/ui/dnd2024-play` load the canonical server-owned `<dnd2024-workspace>` and same-origin component assets directly
Exclusions: UI redesign, React feature migration, game-state changes, catalog changes, authorization changes, new routes or IDs, mechanics, and hosted-Site changes
Allowed files/areas: the existing authored `dnd2024-play` page, focused web-interface tests, this plan and receipt, roadmap wording, and live page-store publication
Stop point: stop once source and live page contain the canonical workspace, contain no iframe or port 5173 reference, all required component assets respond from port 6217, and focused tests pass

## Confirmed decisions

- The user explicitly rejected the ChatGPT Site/prototype as the owner of the canonical play page and requested that the page be loaded from the DantesRoleplay server.
- Reuse the accepted `dnd2024-play`, `/components/dnd2024-workspace.js`, and `<dnd2024-workspace>` identities. No permanent identifier changes.
- The previously retained `index.rev4-restored.html` is reviewed recovery evidence for the last server-owned page and may replace the iframe entry page.

## D&D 5e 2024 alignment

No D&D calculation, eligibility, outcome, or state transition changes. The browser component continues to present the existing server-authorized application state.

## External implementation reference

No Foundry dnd5e reference applies because this is a same-origin hosting correction.

## Prerequisite evidence

- Feature 5 already accepts the same-origin browser-component asset host and canonical `<dnd2024-workspace>`.
- `index.rev4-restored.html` loads the canonical workspace and server-owned supporting components.
- The current `index.html` and its focused test explicitly require the incorrect `http://localhost:5173/` iframe.

## Runtime artifacts

Revise the existing authored page and its focused assertion only. Publish a new revision of the existing live page through the current page-store API. Add no route, component ID, database schema, migration, catalog record, or mechanic.

## Authoritative state and closed input

The server-owned browser component remains the data reader and receives only its existing application/state selection attributes. The page supplies no game records, audience identity, derived values, or database connection.

## Behavior, result, and typed effects

The play route renders `<dnd2024-workspace application-id="dnd2024">` and loads its scripts from same-origin `/components/*` URLs. The optional companion remains server-owned. The page contains no iframe and no dependency on a separate development or hosted Site runtime. Publication creates one normal authored-page revision; it does not mutate game state.

## Failure, replay, and rollback contract

A missing component asset leaves the existing loading state visible and is caught by same-origin asset checks. Repeating the same page publication is content-idempotent. The retained restored page and prior page-store revision provide rollback evidence. No game-state transaction exists.

## Implementation sequence

1. Replace the iframe entry source with the reviewed canonical server-owned page.
2. Reverse the focused test so iframe/port 5173 are forbidden and canonical component assets are required.
3. Run focused tests and script syntax checks, then publish the exact page to the running server.
4. Read back the live page and component URLs, write the receipt, update the roadmap, and stop.

## Acceptance matrix

| Case | Expected result |
| --- | --- |
| Source page | canonical workspace and same-origin component scripts are present |
| External runtime | no iframe, `localhost:5173`, or ChatGPT Site URL is present |
| Live page | `/ui/dnd2024-play` matches the server-owned source after publication |
| Assets | workspace and supporting component URLs return success from port 6217 |
| Authority | no fixture/game record, rule, or browser-selected audience is introduced |
| Compatibility | focused web-interface tests and JavaScript syntax pass |

## Verification commands

- `node --check src/system/web-interface/DantesRoleplay.Web/BrowserComponents/dnd2024-workspace.js`
- focused `WebInterfaceTests` for `Dnd2024_play_page_*`
- same-origin HTTP readback of `/ui/dnd2024-play` and its four component assets
- full website-host tests only if the focused change exposes a shared-host regression

Catalog validation and MCP protocol walk are not required because no catalog or MCP surface changes.

## Completion receipt and exit gate

Write `web/DND2024-WEB-UI-CANONICAL-HOST-SLICE-RECEIPT.md`, mark this slice accepted, update the Feature 5 hosting sentence, and stop without migrating or redesigning the workspace.

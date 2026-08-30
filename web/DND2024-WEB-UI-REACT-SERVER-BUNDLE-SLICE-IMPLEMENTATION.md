# D&D 2024 web UI React server-bundle slice implementation

Status: **accepted 2026-08-30**
Owner/roadmap: `web/WEB-INTERFACE-ROADMAP.md`, Feature 5
Dependency tree/leaf: `prototype/dnd2024/planning/DND2024-WEB-INFORMATION-HUB-DEPENDENCY-TREE.md`, H3/H4 local activation
Ruleset alignment: **dnd2024-compatible presentation**
Source ID and locator: not applicable; this slice changes presentation hosting and no D&D rule
Outcome: serve the actual React information hub, its styles, and reviewed map images as the active `dnd2024-play` bundle from the DantesRoleplay server on port 6217
Exclusions: hosted Sites deployment, iframe/reverse proxy, separate Node runtime, UI redesign, game-state writes, new API routes, catalog/schema/mechanic changes, and cleanup/deletion
Allowed files/areas: the existing React information-hub components and projection adapter, a bounded same-origin browser bootstrap and Vite server-bundle config, package scripts, the authored play-page fixture/tests, the generic web CSP and focused assertion, this plan/receipt and Feature 5 roadmap wording, generated bundle output, and live page-bundle publication
Stop point: stop after the React page and assets load solely from `/ui/dnd2024-play` on port 6217, live World/Campaign data and DM/Player switching use same-origin authorized reads, port 5173 remains absent, tests/build pass, and the published bundle is read back

## Confirmed decisions

- The user explicitly requested the actual React website be hosted by the canonical DantesRoleplay server.
- Preserve the established React design and components; do not substitute the older custom-element workspace.
- Reuse the existing `dnd2024-play` page identity and `/api/pages/{id}/bundle` owner. No new public route or permanent ID is introduced.
- The page remains a trusted local table/operator surface. The existing local DM-seat behavior and Player-preview filtering are preserved; this is not a separately distributable player security boundary.
- The user requested local server hosting, so the Sites skill ends at the validated local bundle and does not republish the retired hosted Site.

## D&D 5e 2024 alignment

No rule, formula, eligibility, timing, or outcome moves into the browser or C#. Existing live records and audience projections remain unchanged.

## External implementation reference

No Foundry dnd5e reference applies because this is a presentation packaging change.

## Prerequisite evidence

- The React information hub already passes its full website suite and production build and projects live campaign, World, map, and audience data.
- The canonical server already owns bounded ZIP page bundles with root `index.html`, `assets/*`, atomic save/activation, MIME resolution, and same-origin reads.
- The previous canonical-host receipt proves `/ui/dnd2024-play` and all server APIs/assets respond on port 6217 without port 5173.

## Runtime artifacts

- Add a browser bootstrap that obtains the same connected envelope from `window.location.origin`, applies the existing connected projection, and mounts `DndInformationHub` with React.
- Let `DndInformationHub` accept an optional in-process envelope loader; its existing `/api/hub` fetch remains the default for the hosted/Vinext build.
- Let the map projector accept a presentation asset base; the default remains `/`, while the server bundle uses `/ui/dnd2024-play/assets/`.
- Add a separate Vite build configuration that emits only `index.html` and bounded `assets/*` page-bundle files. Copy only the reviewed map files used by the live asset registry.
- Revise the canonical authored fixture/test to identify the React mount and bundled assets rather than the obsolete custom element.

## Authoritative state and closed input

The browser bootstrap selects only the same server origin and the accepted local DM seat. Campaign and perspective choices pass through the existing closed `readGameServerContext` and `connectedCampaignToHubEnvelope` validation/filtering. The page accepts no database path, state-space ID, actor ID, raw component JSON, URL override, authorization credential, or game result.

## Behavior, result, and typed effects

Initial load requests the default Player projection, renders the existing unavailable state on failure, and otherwise mounts the exact React hub. Campaign and DM/Player switches reuse the same loader without navigation or `/api/hub`. All JavaScript, CSS, fonts, and maps resolve under the active page's `assets/` route. Publication creates one atomic page-bundle revision and no game-state effect.

## Failure, replay, and rollback contract

Unavailable/denied live context renders the existing unavailable view and cannot fall back to fixture data. Missing or unknown map assets fail closed as before. A failed bundle build/upload leaves the active page revision unchanged. Repeating the same ZIP publication is content-idempotent and creates only normal page revision evidence. Prior page revisions remain rollback evidence.

## Implementation sequence

1. Add the optional loader and asset-base seams with focused tests.
2. Add the same-origin React bootstrap and isolated server-bundle build.
3. Build, test, inspect the emitted index/assets, and create a bounded ZIP.
4. Publish the ZIP, read back the live page/assets/data markers, update the fixture/test/docs, write the receipt, and stop.

## Acceptance matrix

| Case | Expected result |
| --- | --- |
| React identity | live page contains the React root/bootstrap and established hub CSS |
| Same-origin hosting | all emitted URLs begin under `/ui/dnd2024-play/assets/` |
| No second runtime | no iframe, ChatGPT Site URL, `/api/hub`, or port 5173 dependency in the server bundle |
| Live data | bootstrap reads only `window.location.origin` and renders the current live campaign/World envelope |
| Perspective/campaign switching | optional loader supplies a validated ready envelope and preserves current view on failure |
| Map media | live asset keys resolve below the page bundle asset base; unknown keys still fail closed |
| Failure | denied/unavailable context renders `HubUnavailable`, never fixture data |
| Compatibility | existing Vinext build/default loader and full website tests remain green |
| Atomic activation | invalid/oversize upload cannot replace the active page; valid bundle activates and reads back |

## Verification commands

- `npm test` in `prototype/dnd2024`
- `npm run build` in `prototype/dnd2024`
- `npm run build:server` in `prototype/dnd2024`
- focused web-interface bundle/page tests
- ZIP inventory and size-limit checks
- same-origin HTTP readback from `/ui/dnd2024-play` and every referenced emitted asset

Catalog validation and MCP protocol walk are not required because this slice changes no catalog or MCP surface.

## Completion receipt and exit gate

Write `web/DND2024-WEB-UI-REACT-SERVER-BUNDLE-SLICE-RECEIPT.md`, mark this plan accepted, update Feature 5 once, and stop before deleting either implementation or the hosted Site.

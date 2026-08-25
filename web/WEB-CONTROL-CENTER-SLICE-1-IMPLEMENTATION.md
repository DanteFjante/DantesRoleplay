# Web Interface Feature 2 Slice 1 implementation — read-only control-center shell

Status: **accepted with recorded repository-level test exception — delivered by [Slice 1 receipt](WEB-CONTROL-CENTER-SLICE-1-RECEIPT.md)**  
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md)  
Dependency tree/leaf: [Operator control-center dependency plan](WEB-CONTROL-CENTER-DEPENDENCY-PLAN.md#ordered-leaves)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Deliver an uploadable, browser-native `control-center` page bundle and one bounded,
authenticated read-only status endpoint. Each confirmed panel renders independently and honestly
states that its future capability is unavailable.  
Exclusions: Effect/event reads, ECS/catalog reads, page/editor APIs, settings reads/writes, host
configuration, conversation persistence, local-model calls, Codex process/approvals, page mutation,
database changes, migrations, MCP changes, and host composition changes.  
Allowed files/areas: `src/system/web-interface/`, focused web tests, `web/WEB-CONTROL-CENTER-*`, and
the web roadmap. No user-authored existing page change and no database write beyond an operator later
uploading the supplied bundle through the existing page API.  
Stop point: The static bundle source, status projection/route, documentation, focused/full
verification, receipt, and status updates are complete. Do not upload/seed a live page or start any
later panel slice.

## Confirmed decisions

- On 2026-08-24 the user said to continue after Slice 0. This activates the already-confirmed page
  ID `control-center`, five custom-element tags, and read-only `/api/control/status` route.
- `GET /api/control/status` is the sole runtime control route in this slice. It is mapped only through
  `MapDantesRoleplayControlGet`, therefore uses `control.read`, the existing read rate limiter, and
  the Slice 0 private-operator boundary.
- The response has this exact bounded JSON shape:

  ```json
  {
    "status": "ready",
    "access": { "mode": "local|tailscale", "login": "string|null" },
    "panels": [
      { "id": "server-settings|effect-history|assistant|ecs-explorer|site-editor", "state": "unavailable", "message": "bounded static explanation" }
    ]
  }
  ```

  It reports only the web control boundary and panel delivery state. It is not a process-health,
  host-configuration, world-state, provider, or Codex health API. Its response is `Cache-Control:
  no-store` because the access identity can differ by caller.
- The source bundle contains one root `index.html`, with inline CSS/JavaScript and no external assets
  or build tooling. Operators upload it using the existing ZIP bundle endpoint; the host never seeds
  it into SQLite automatically.
- Each panel remains a browser-native custom element: `<server-settings-panel>`,
  `<effect-history-panel>`, `<assistant-panel>`, `<ecs-explorer>`, and `<site-editor>`. Its own
  loading/unavailable/forbidden/retry state is independent; no shared panel data is introduced.

## D&D 5e 2024 alignment

No D&D rule, vocabulary, formula, eligibility, outcome, or catalog content is involved. This is a
ruleset-neutral web projection and static operator page.

## External implementation reference

No Foundry dnd5e review is relevant to this ruleset-neutral browser page. The implementation uses
the browser's native custom-elements, fetch, and DOM APIs already permitted by the existing web
boundary.

## Prerequisite evidence

- [Slice 0 receipt](WEB-CONTROL-CENTER-SLICE-0-RECEIPT.md) verifies `control.read`, authenticated
  local/exact-Tailscale access, control route confinement, rate limiting, and rejection before a
  control handler runs.
- `WebPageBundle`/`WebPageStore` own immutable, revision-scoped HTML/assets and activation.
- `WebControlEndpointConventions` owns control route mapping and `WebControlRequestFilter` owns the
  authenticated control-read boundary.
- [Feature 1 Slice 5 receipt](WEB-INTERFACE-SLICE-5-RECEIPT.md) verifies the remote web-only path
  boundary and Tailscale identity semantics that Slice 1 reuses.

## Runtime artifacts

- New public status records `ControlCenterStatusDocument`, `ControlCenterAccessStatus`, and
  `ControlCenterPanelStatus` plus a web-owned factory that derives them from the authenticated
  principal.
- One `GET /api/control/status` endpoint returning that document and setting `Cache-Control: no-store`.
- One source bundle at `src/system/web-interface/examples/control-center/index.html`.
- Revised web README, component manifest, roadmap/dependency status, tests, and completion receipt.
- No page is uploaded, no source is authoritative game state, and no database/API schema migration is
  created.

## Authoritative state and closed input

- The Slice 0 filter sets `HttpContext.User`; the browser cannot choose `access.mode` or `access.login`.
- Panel IDs, states, order, and messages are server-authored constants. The browser cannot supply a
  panel, capability, world/entity/component ID, provider, or configuration key.
- The endpoint accepts no route/query/body input. It never reads the database or host configuration.
- The static bundle receives only this status document and treats every value as display text, never
  HTML. It has no mutating fetch call or EventSource connection.

## Behavior, result, and typed effects

1. `MapDantesRoleplayWeb` maps exactly `/api/control/status` with the existing control GET helper.
2. The control filter authenticates and applies `control.read`; denied callers receive its stable
   403 response before the status handler.
3. The handler derives local/tailscale access from the authenticated principal, writes `no-store`, and
   returns `status: "ready"` plus the ordered five unavailable panel records.
4. The bundle fetches the endpoint once through a shared in-page promise. Each custom element handles
   success, unavailable, forbidden, malformed response, and retry independently.
5. Navigation anchors scroll between the five panels even if one fetch/render path fails.

The slice has no transaction and no typed effect.

## Failure, replay, and rollback contract

- Unauthenticated/wrong-host/disallowed-Tailscale callers retain Slice 0's denial before the handler.
- The endpoint has no input, database access, or mutation. Repeated reads return the same bounded
  panel contract for the same access mode and make no change.
- A non-JSON, failed, forbidden, malformed, or unavailable status response changes only the relevant
  browser element to an unavailable/forbidden/retry state; it does not prevent navigation or other
  elements from rendering.
- Uploading the supplied bundle remains an existing append-and-activate page transaction. It is not
  performed by this slice. A later upload is its own immutable revision and leaves prior revisions.
- Deleting the source bundle or not uploading it makes no server-state change. Existing CLI/page-upload
  paths remain the recovery route for a broken control-center revision.

## Implementation sequence

1. Add the closed status records/factory and control GET mapping with focused contract/route tests.
2. Add the one-file bundle source with five independent custom elements and source/upload guidance.
3. Run focused web tests, web project build, full shared tests, and `git diff --check`.
4. Write the receipt, mark Slice 1 accepted only with the verification evidence available in the
   current worktree, update the dependency plan/roadmap, and stop.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Status shape | Local/tailscale access shape, `ready` state, exact ordered five IDs, unavailable state, bounded messages, and no secrets/configuration pass. |
| Read boundary | `/api/control/status` maps GET/control.read only; it has no body, database call, or mutation route. |
| Access | Local/exact allowed Tailscale read succeeds; denied identity is stopped by the existing filter before the handler. |
| Cache | The route sends `Cache-Control: no-store`. |
| Shell | All five tags, navigation anchors, status fetch, individual retry paths, loading/unavailable/forbidden states, and no external/build dependency exist in the source bundle. |
| Isolation | A failed status load affects each element independently and does not remove navigation or static panel content. |
| Compatibility | Existing page revision, upload, session, data, SSE, remote-MCP exclusion, and control mutation behavior remain unchanged. |

## Verification commands

- Focused `WebInterfaceTests`.
- `dotnet build src/system/web-interface/DantesRoleplay.Web/DantesRoleplay.Web.csproj --no-restore`.
- Full shared test assembly; record any unrelated moving-worktree exception without modifying that
  owner.
- `git diff --check` on Slice 1 files.

## Completion receipt and exit gate

Delivered behavior and the repository-level test exception are recorded in
[`WEB-CONTROL-CENTER-SLICE-1-RECEIPT.md`](WEB-CONTROL-CENTER-SLICE-1-RECEIPT.md). Slice 2 needs its
separate effect-history authority confirmation; later slices remain inactive.

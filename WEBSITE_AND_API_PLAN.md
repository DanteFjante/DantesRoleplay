# Website and API plan

Status: **Draft — for design discussion; no implementation is authorised by this plan**  
Last updated: 2026-08-20

## Goal

Add a local human-facing campaign/world website and a small HTTP API to the existing ASP.NET host. Pages must be complete, useful server-rendered HTML; small JavaScript modules enhance independently-owned regions after load. The API exposes stable engine concepts while preserving dynamically authored entities, components, mechanics, pages, and events as data.

This follows the decisions in `ARCHITECTURE.md` §§4.2–4.4 and P14: no SPA framework, no Node toolchain, no frontend build step, no direct browser-to-database access, and no game authority in the presentation layer.

## Decisions already agreed

1. The first site is a **read-only campaign/world viewer for humans**, not the maintainer control room described in the earlier architecture record. That control room remains valuable, but is a later concern. This deliberately revises the P14 implementation order in `ARCHITECTURE.md`; update that record when this direction is ratified.
2. `DantesRoleplay.MCPServer` remains the one ASP.NET process hosting MCP, HTML pages, static browser assets, the HTTP API, and later SSE.
3. Every page has an immediately useful server-rendered first view. JavaScript is progressive enhancement, not a prerequisite for seeing or using the page.
4. JavaScript is organised as small native ES modules, attached through `data-component` and `data-*` configuration attributes. There is no React, Svelte, client-side router, bundler, or generated frontend client.
5. API paths are stable; dynamically added database records change returned JSON, **not** the server's set of route names or the database schema exposed to a browser.
6. Dynamic records use a stable envelope. For example, an entity returns stable identity and revision fields plus a list of typed component records whose `data` is JSON.
7. The first release exposes no browser writes. A later interactive feature may use semantic application commands, but no endpoint may accept arbitrary SQL, unrestricted filtering, raw effect application, or direct table CRUD.
8. Live UI refreshes use SSE notifications after successful transaction commit. A notification invalidates a known resource; the component then fetches fresh authoritative data. The server does not push executable JavaScript or trusted HTML into the DOM.
9. Until an explicit security/exposure design exists, web/API/SSE endpoints are loopback-only.

## Target request flow

```text
GET /world
  -> complete server-rendered HTML including initial campaign/world information
  -> /assets/pages/world.js attaches world-summary and entity-browser components

User filters or opens a detail pane
  -> component calls a narrow /api/... endpoint
  -> component redraws only its own region from returned JSON or a safe server-rendered partial

Engine operation commits
  -> database transaction commits its world/audit/event changes
  -> SSE publishes a compact resource-invalidated notification
  -> interested components fetch current data again
```

## API shape

### Read resources

Start with stable HTTP resources for the human-facing world:

- `/api/world`
- `/api/entities` and `/api/entities/{id}`
- `/api/pages/{id}` when declarative page specs are introduced

The later maintainer control room may separately expose procedures, operations, mechanics, events,
subscriptions, and notifications to an appropriately authorised audience.

Every collection declares its supported filter, sort, pagination, and maximum page size. Complex or cross-record reads are named query capabilities, validated in the engine, rather than an open-ended query language.

### Future writes

Interactive features are deliberately deferred. When a later map, travel, battle, or other player interaction needs a write, its endpoint must map to an existing governed command or procedure and return the normal operation envelope and resource revisions. Each command must retain the same validation, transaction, audit, and error behaviour as its MCP equivalent.

### Dynamic data envelope

The stable entity envelope is deliberately compatible with new component definitions:

```json
{
  "id": "creature.orban",
  "kind": "creature",
  "revision": 8,
  "components": [
    { "type": "vitals", "revision": 3, "data": { "hp": 12, "maxHp": 12 } }
  ]
}
```

The component definition supplies display metadata and optional schema. A renderer must show unknown components safely instead of assuming a compile-time type.

## Live-update contract

- A single endpoint, initially `/api/stream`, emits named SSE events such as `resource-invalidated` and `notification-created`.
- Events identify a stable resource type, resource ID where applicable, revision, operation ID, and correlation ID. They do not contain sensitive state by default.
- The producer queues the notification only after the root transaction is known to have committed. A rolled-back operation emits no browser notification.
- Components subscribe by declared resource interest, de-duplicate repeated invalidations, and reload using their regular read endpoint. They must cope with missed events after reconnect.
- `Last-Event-ID` support and a bounded replay/catch-up policy are required before SSE is relied on for more than convenience. A manual refresh remains correct at all times.
- Start without WebSockets. Reconsider only for a genuine bidirectional live feature, such as collaborative editing.

## Delivery slices

### Slice 0 — settle the contracts

Before code, write the first page/user-flow brief, HTTP error envelope, pagination convention, static-asset layout, component lifecycle convention, and loopback binding policy. Decide whether API responses are JSON-only or whether selected endpoints may return safe HTML fragments.

**Acceptance:** one documented example traces a rendered world page, a filter request, and a post-commit invalidation. It contains no browser write.

### Slice 1 — host and safe rendering foundation

Configure the existing host for routing, static assets, HTML rendering, consistent errors, correlation IDs, development diagnostics, and loopback-only access. Introduce an intentionally simple campaign/world navigation layout.

**Acceptance:** `/` renders a static but complete campaign/world shell; `/mcp` continues to work; the host has tests for endpoint binding and response security headers.

### Slice 2 — first vertical feature: read-only world explorer

Build `/world`, an entity-detail page, and matching JSON read endpoints. Server-render the initial world summary and selected entity detail from existing governed query services. Add only the components this page requires: filter/search, entity selection, component display, and refresh. Ensure empty, error, and unknown-data states are present.

**Acceptance:** browser and endpoint tests prove that displayed world data is the same data available through the governed query capability; the page remains readable without JavaScript.

### Slice 3 — read API discipline

Add shared endpoint helpers that convert validated application results to HTTP JSON, map stable errors, and enforce bounded list querying. Do not duplicate business rules from MCP handlers in page/API code.

**Acceptance:** external callers cannot construct a database query or mutation through the read API; malformed and out-of-bounds requests receive stable errors.

### Slice 4 — world context and history

Add read-only activity/history views relevant to the human-facing experience, such as recent world changes, known locations, and discovered relationships. Keep internal procedure and raw-operation inspection out of this surface.

**Acceptance:** a user can understand the current world and recent relevant changes without needing internal development concepts.

### Slice 5 — SSE-based freshness

Implement the post-commit notification bridge, `/api/stream`, reconnect behaviour, and component invalidation registry. Start by refreshing world and entity components only.

**Acceptance:** a committed change made through either MCP or the web UI updates an open related page without a full-page reload; a rolled-back action produces no update; reconnect/missed-event behaviour remains correct.

### Slice 6 — declarative page and view specs

Introduce the closed display vocabulary, page/view-spec storage, renderer, validation, and catalog/import/export behaviour specified in the architecture. Move new human-facing screens to specs as the vocabulary earns them; do not allow arbitrary authored HTML.

**Acceptance:** a page spec can safely render a new screen over dynamic component data, and an unknown component remains inspectable rather than breaking the page.

### Slice 7 — maps and future interaction

First introduce a read-only map view once the location/containment contracts and the [World Feature 9 map-layout contract](world/feature-09/WORLD-FEATURE-09-DEPENDENCY-PLAN.md) are verified. The map is a component over server-provided world data, so it can update through SSE without a full-page reload.

Only after the read-only map is useful, plan interactive travel and battle maps as separate features. Each needs a clear intent model, valid-target discovery, an authoritative command, transaction/audit behaviour, conflict/revision handling, and accessible non-map controls.

The later maintainer control room (procedures, operations, mechanics, event chains, approvals, and rollback) is a separate set of read/write screens and should not be mixed into the human-facing campaign UI.

**Acceptance:** the read-only map accurately presents supported world relationships and refreshes after relevant committed changes. No click on the map changes world state.

## Test and operational requirements

- Unit-test endpoint contracts, error mapping, pagination limits, authorization/binding policy, SSE post-commit ordering, reconnection, and component data transformations.
- Add end-to-end browser tests for server-render-only use, enhancement behaviour, and live refresh with no console errors.
- Test all external JSON and HTML rendering for encoding/XSS safety. Dynamic component `data` is data, never executable browser source.
- Keep API and page request logs correlated with existing operation/audit records.
- Document local startup, browser address, API overview, and security boundary before the first usable screen ships.

## Decisions still needed

1. Is the human-facing site local-machine-only for the first release, or should we plan an authenticated remote deployment path now?
2. For component refreshes, should the API return JSON only, or may it also return narrowly scoped server-rendered HTML fragments? JSON-only has one rendering model; fragments minimise browser rendering code.
3. Which read-only first page matters most: a world overview, an entity browser, a location browser, a session recap, or a map placeholder?
4. What information is player-visible versus GM-visible, and should the first site support either distinction?
5. What is the minimal spatial model that a later map may assume: named locations and containment only, or coordinates, distance, and terrain?
6. How should dynamically authored page/view specs be approved, versioned, and rolled back before the page-spec slice begins?

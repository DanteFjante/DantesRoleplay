# Web Interface Feature 1 Slice 3 implementation — live SSE invalidation

Status: **accepted — delivered by [Slice 3 receipt](WEB-INTERFACE-SLICE-3-RECEIPT.md)**  
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md)  
Dependency tree/leaf: [Live SSE invalidation](WEB-INTERFACE-DEPENDENCY-TREE.md#ordered-leaves)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Let a local browser keep one server-sent-event connection open, refetch dynamic data after
committed database changes, and optionally learn when one page's active revision changes.  
Exclusions: Durable event replay, entity/component filters, exact change payloads, client-side data
patching, WebSockets, browser framework code, authentication, authorization, isolation, CSP,
connection quotas, remote binding/deployment, game-state writes, and UI polish.  
Allowed files/areas: `web/`, `src/system/web-interface/`, focused web tests, and status/link-only
changes in the web roadmap and dependency tree. No migration or non-web subsystem edit.  
Stop point: Focused tests and solution build pass; existing page/data routes and MCP surface remain
compatible; record the receipt and stop before security or remote deployment.

## Confirmed decisions

- The user's 2026-08-24 instruction to continue after accepting Slice 2 authorizes the roadmap's
  next ordered slice and the public SSE route required to deliver it.
- The permanent route is `GET /api/changes`; optional query `page={pageId}` enables page-revision
  notices without changing the global invalidation behavior.
- The stream uses standard `text/event-stream`. It sends `invalidate` events, optional
  `page-revision` events, and SSE comment keepalives.
- Invalidation is deliberately coarse. A committed SQLite change may cause a browser refetch even
  when the data it displays did not change. The stream never claims an entity or component changed.
- The web layer observes SQLite's connection-local `PRAGMA data_version` commit token. It reads no
  kernel table and does not join or alter the game-state transaction owner.
- Reconnection has no replay cursor. Every connection begins with an `invalidate` event carrying
  reason `connected`, so a browser refetch closes any disconnected interval.

## D&D 5e 2024 alignment

No D&D rule, term, formula, eligibility decision, state, or outcome is introduced.

## External implementation reference

No Foundry review is relevant to ruleset-neutral HTTP streaming and SQLite commit observation.

## Prerequisite evidence

- [Slice 2 receipt](WEB-INTERFACE-SLICE-2-RECEIPT.md) proves active page revisions, asset bundles,
  shared SQLite hosting, HTTP composition, and unchanged MCP behavior.
- `WebContentDbContext` owns web page state and already connects to the same local SQLite database
  as the generic kernel.
- `DynamicDataReader` remains the only web reader of entity/component content through `IWorldStore`.

## Runtime artifacts

- One web-owned `SqliteWebChangeFeed` that holds a read connection for a stream and observes the
  SQLite commit token at a bounded interval.
- One ruleset-neutral change-event record and SSE formatter/writer behavior.
- One `GET /api/changes` endpoint and service registration.
- No catalog ID, component schema, mechanic, procedure, application ID, MCP kind, database table,
  index, or migration.

## Authoritative state and closed input

- SQLite remains authoritative. The change feed is transient notification only and never becomes
  a second state store.
- Callers may supply only an optional route-safe page ID. They cannot supply a database cursor,
  page revision, event type, entity ID, component ID, polling interval, or SQL.
- The backend derives the current SQLite data version and optional active page revision.
- The default observation interval is one second and the keepalive interval is fifteen seconds.

## Behavior, result, and typed effects

- A valid connection returns `200`, `text/event-stream`, disables response buffering/cache where
  supported, writes a two-second SSE retry hint, then immediately emits:
  `event: invalidate` with JSON containing `reason: "connected"`, `databaseVersion`, and optional
  `pageId`/`pageRevision`.
- After any observed commit token change, the stream emits `invalidate` with reason
  `database-commit`. This is a refetch hint, not a change description.
- When `page` is present and its active revision changed, the same observation also emits
  `page-revision` with page ID, nullable revision, and the page URL when it exists.
- Keepalive comments contain no application event and do not instruct the browser to refetch.
- Client cancellation ends the stream without a write or error response.

## Failure, replay, and rollback contract

- An invalid page query returns stable `400 INVALID_PAGE_ID` before SSE headers are committed.
- A disconnected client receives no replay. Its next connection's immediate invalidation requires
  a full refetch and therefore closes the observation gap.
- Rolled-back work does not advance SQLite's data version and produces no commit invalidation.
- Multiple commits between polls may coalesce into one invalidation. Since the browser refetches
  authoritative current state, coalescing cannot make the displayed result stale after refetch.
- Storage/query failures terminate the stream; the browser's native `EventSource` retry opens a
  fresh stream and receives the initial invalidation.

## Implementation sequence

1. Add the transient change-event/feed seam and focused commit/rollback/coalescing tests.
2. Add SSE formatting, endpoint mapping, registration, and invalid-input tests.
3. Update the local usage documentation without modifying the user-owned example page.
4. Run focused tests, solution build, compatibility checks, HTTP streaming walk, and write receipt.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Positive | A second connection commits a change; the open feed emits one database invalidation. |
| Page | A page activation emits both invalidation and the new page revision for a page-filtered feed. |
| Initial/replay | Every new feed emits an immediate connected invalidation with current revision. |
| Rollback | A rolled-back transaction emits no invalidation. |
| Coalescing | Several commits before observation may produce one refetch signal without stale payload claims. |
| Boundary | Invalid page IDs return `400` before streaming; cancellation ends normally. |
| Compatibility | Existing HTML, bundle, asset, dynamic JSON, solution build, and MCP surface remain unchanged. |

## Verification commands

- Focused `WebInterfaceTests`.
- `dotnet build DantesRoleplay.slnx --no-restore`.
- Existing protocol/manifest-guard tests because the shared host gains an HTTP route, not MCP kinds.
- Local HTTP stream walk against a disposable database.
- `git diff --check`.

## Completion receipt and exit gate

Delivered behavior and verification are recorded in
[`WEB-INTERFACE-SLICE-3-RECEIPT.md`](WEB-INTERFACE-SLICE-3-RECEIPT.md). Slice 3 is accepted; stop
before authentication, isolation, CSP, quotas, or remote deployment.

# Web Interface Feature 1 Slice 1 implementation — trusted HTML and dynamic JSON

Status: **accepted with repository-level test exception**
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md)
Dependency tree/leaf: [Trusted dynamic HTML pages](WEB-INTERFACE-DEPENDENCY-TREE.md)
Ruleset alignment: **ruleset-neutral**
Source ID and locator: **not applicable**
Outcome: Upload, version, activate, and serve complete HTML documents; read an entity or one typed
component as dynamic JSON.
Exclusions: Authentication, authorization, sandboxing, CSP design, remote binding, ZIP/assets,
HTML parsing/sanitizing, page-layout schemas, SSE, game-state writes, and UI polish.
Allowed files/areas: `web/`, `src/system/web-interface/`, the new web project, focused web tests,
and the minimal solution/host/test project registration seams.
Stop point: Focused tests and solution build pass; MCP remains mapped only at `/mcp` and its
three-verb surface is unchanged.

## Confirmed decisions

- Permanent project/component names: `DantesRoleplay.Web` and `web-interface`.
- Persistent tables: `web_page` and `web_page_revision`, with a separate EF migration-history
  table for the web context in the shared SQLite database.
- Public routes:
  - `PUT /api/pages/{id}` accepts a complete `text/html` body and activates a new revision.
  - `GET /ui/{id}` returns the active document as `text/html`.
  - `GET /api/data/entity/{id}` returns a generic dynamic entity envelope.
  - `GET /api/data/{componentType}/{entityId}` returns the stored component JSON object.
- Uploaded HTML is trusted and returned unchanged. Security is a later feature.

## D&D 5e 2024 alignment

No D&D rule, term, formula, eligibility decision, or outcome is introduced.

## External implementation reference

No Foundry review is relevant to generic HTML hosting and opaque component reads.

## Prerequisite evidence

- `IWorldStore.GetEntityAsync` materializes bounded entity/component state.
- `WorldStoreTests` proves component data is valid JSON object data and one component exists per
  definition per entity.
- The existing ASP.NET application owns process composition and already initializes SQLite.

## Runtime artifacts

- New `DantesRoleplay.Web` project and `web-interface` component inventory record.
- `WebPage` identity with one active revision pointer.
- Append-only `WebPageRevision` rows containing complete HTML.
- A web-owned EF migration and migration-history table.
- No catalog IDs, component schemas, mechanics, procedures, MCP kinds, or game-state migrations.

## Authoritative state and closed input

- SQLite is authoritative for uploaded page revisions.
- `PUT /api/pages/{id}` accepts one route-safe ID and a non-empty `text/html` body. It never accepts
  a caller-supplied revision, timestamp, active pointer, database name, query, or file path.
- Dynamic data reads accept exactly a type and entity ID. The backend resolves all state through
  `IWorldStore`; callers cannot name a table, column, filter, SQL expression, or projection.

## Behavior, result, and typed effects

- A page upload appends the next integer revision and atomically updates the page's active pointer.
- Page reads return exactly the active HTML content and `404` when the page does not exist.
- `entity` reads return identity, containment, contained summaries, and a property map whose keys
  are component-definition IDs and whose values are parsed stored JSON.
- Component reads return the selected component's JSON object directly, not JSON encoded as a
  string and not translated to a compile-time DTO.
- The page transaction is owned by the web page store. Data reads create no transaction or effect.

## Failure, replay, and rollback contract

- Invalid page IDs, empty bodies, and non-HTML content types return stable `400` or `415` results
  without creating a revision.
- Missing pages, entities, or components return `404` without writes.
- A failed page save leaves the previous active revision unchanged.
- Repeating an upload intentionally creates another immutable revision; upload idempotency is not
  part of this slice.
- Stored component JSON that cannot be parsed produces a server error because it violates the
  existing state-owner invariant; the web layer does not reinterpret it.

## Implementation sequence

1. Add the web project, records, persistence, migration, and registration.
2. Add the dynamic data reader and HTTP mappings.
3. Compose the project into the existing host with the same SQLite path.
4. Add focused persistence/data tests, build, and record evidence.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Positive page | Two uploads produce revisions 1 and 2; retrieval returns revision 2 unchanged. |
| Page boundary | Invalid ID, empty HTML, and wrong content type do not write. |
| Dynamic entity | Unknown component keys and payload fields survive in returned JSON. |
| Dynamic component | Type plus entity ID returns the exact component JSON object. |
| Missing | Unknown page/entity/component returns not found. |
| No-change | All reads leave both page and game state unchanged. |
| Compatibility | Solution builds and existing MCP protocol surface remains unchanged. |

## Verification commands

- Focused web-interface tests.
- `dotnet build DantesRoleplay.slnx --no-restore`
- Existing protocol surface tests because host dependency registration changes.
- Full suite only at feature acceptance if the concurrent modularization worktree is green.

## Completion receipt and exit gate

Evidence is recorded in [the Slice 1 receipt](WEB-INTERFACE-SLICE-1-RECEIPT.md). Work stopped before
SSE, uploaded asset bundles, security work, or a game-state write endpoint.

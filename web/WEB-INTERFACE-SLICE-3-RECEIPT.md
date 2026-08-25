# Web Interface Feature 1 Slice 3 receipt — live SSE invalidation

Status: **Verified and accepted**

## Delivered boundary

- Added `GET /api/changes` as a standards-based `text/event-stream` response.
- Added optional `page={pageId}` observation for active page-revision notices.
- Added immediate `invalidate` on every connection and reconnect, committed-database-change
  invalidation, page-revision events, two-second retry guidance, and fifteen-second keepalives.
- Added a transient SQLite commit observer based on `PRAGMA data_version`. It reads no kernel table,
  changes no transaction owner, and persists no cursor or subscription.
- Kept invalidation deliberately coarse: browsers refetch authoritative current data rather than
  receiving guessed entity/component changes or applying server-authored patches.
- Added local `EventSource` usage documentation without modifying the user-owned example page.

## Evidence

- Focused web tests: **16 passed**, including committed page observation, initial/reconnect state,
  page revision, rollback silence, and valid single-line SSE frames.
- Solution build: **succeeded with 0 warnings and 0 errors**.
- Protocol and manifest-guard compatibility checks: **13 passed**.
- Full suite: local-AI **19 passed**; shared suite **530 passed**, with no failures.
- HTTP stream walk against a disposable fresh SQLite database:
  - the stream returned `200` and began with retry guidance plus `event: invalidate`;
  - a page upload returned revision 1 and advanced the observed database version;
  - the open stream emitted `database-commit` invalidation and page revision 1 with its URL;
  - an unsafe page query returned `400 INVALID_PAGE_ID` before streaming.
- `git diff --check`: **passed**; reported only existing line-ending conversion notices.

## Deliberate exclusions

No durable event replay, Last-Event-ID cursor, entity/component filter, exact state-delta payload,
client-side patch protocol, WebSocket, frontend framework, authentication, authorization, isolation,
CSP, connection quota, remote binding/deployment, game-state write, catalog record, MCP kind,
database migration, or D&D rule was added.

The live HTTP walk used the Production environment because the unrelated Development-only missing
local structured-completion registration recorded by the Slice 2 receipt remains outside this
web-owned boundary.

# Web Interface Feature 1 Slice 1 receipt — trusted HTML and dynamic JSON

Status: **Verified; repository-level test exception recorded**

## Delivered boundary

- Added the standalone ruleset-neutral `DantesRoleplay.Web` project and `web-interface` component.
- Added append-only `web_page_revision` persistence with an atomic active pointer in `web_page`.
- Added a web-owned EF migration history in the existing SQLite database.
- Added `PUT /api/pages/{id}` and `GET /ui/{id}` for complete trusted HTML documents.
- Added `GET /api/data/entity/{id}` for a generic dynamic entity envelope.
- Added `GET /api/data/{componentType}/{entityId}` for a component's raw stored JSON object.
- Composed the web project into the existing ASP.NET host without changing MCP kinds or game-state
  transaction ownership.

## Evidence

- Focused web tests: **8 passed**, covering immutable revisions, unchanged HTML, invalid/no-write
  behavior, unknown dynamic fields, raw component JSON, and missing reads.
- Solution build: **succeeded with 0 warnings and 0 errors**.
- Protocol-focused tests: **7 passed**; the MCP three-verb surface remains unchanged.
- HTTP walk against a disposable fresh SQLite database:
  - consecutive uploads returned revisions 1 and 2;
  - `GET /ui/dynamic-viewer` returned `200`, `text/html`, and byte-for-byte matching revision 2;
  - a missing dynamic entity returned `404`.
- Full suite: local-AI **19 passed**; shared suite **813 passed and 2 failed**. The two failures are
  the independently reproducible Feature 20 movement/Speed failures already owned by
  [`KNOWN_ISSUES.md`](../KNOWN_ISSUES.md#feature-20-movementspeed-acceptance-failures). The component
  manifest guard and all web-interface tests pass independently.

## Deliberate exclusions

No SSE, asset/ZIP upload, authentication, authorization, CSP, sandboxing, remote exposure, HTML
sanitization, page editor, game-state write endpoint, catalog record, or D&D rule was added.

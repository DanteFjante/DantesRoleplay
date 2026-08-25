# Web Interface Feature 1 Slice 2 receipt — versioned ZIP page bundles

Status: **Verified and accepted**

## Delivered boundary

- Added `PUT /api/pages/{id}/bundle` for bounded `application/zip` page bundles containing one
  root `index.html` and revision-owned files below `assets/`.
- Added `GET /ui/{id}/index.html` and `GET /ui/{id}/assets/{**path}` while retaining the accepted
  direct HTML upload and `GET /ui/{id}` behavior.
- Added append-only `web_page_asset` persistence with derived media type and SHA-256 content hash,
  keyed to an immutable page revision.
- Added a forward web-owned EF migration, unique revision/path index, and SQLite optimization step.
- Made HTML, asset insertion, and active-revision movement one transaction. A later bundle exposes
  only its own active assets while older revision rows remain available as recovery evidence.
- Added closed ZIP limits and rejection for traversal, rooted or URL-ambiguous paths, duplicates,
  misplaced assets, invalid UTF-8 HTML, malformed archives, and oversized input.

## Evidence

- Focused web tests: **13 passed**, covering exact bytes/content metadata, active-only asset reads,
  direct-upload compatibility, ZIP boundaries, transaction rollback, and fresh-database migration.
- Solution build: **succeeded with 0 warnings and 0 errors**.
- Protocol and manifest-guard compatibility checks: **13 passed**.
- Full suite: local-AI **19 passed**; shared suite **511 passed**, with no failures.
- HTTP walk against a disposable fresh SQLite database:
  - bundle upload returned `200`, revision 1, and one asset;
  - active `index.html` returned `200` with `text/html`;
  - the active CSS asset returned `200` with `text/css`;
  - all web migrations, including `WebPageAssets`, applied before the walk.
- `git diff --check`: **passed**; reported only existing line-ending conversion notices outside
  this slice.

## Deliberate exclusions

No individual or partial asset mutation, SSE, authentication, authorization, isolation, CSP,
quota administration, remote binding/deployment, HTML rewriting, frontend build system, game-state
write, catalog record, MCP kind, or D&D rule was added.

The live HTTP walk used the Production environment. Development-only service-provider validation
currently finds an unrelated pre-existing missing local structured-completion registration; that
host composition issue is outside this web-owned slice and did not affect build, tests, migration,
or the Production HTTP walk.

# Web Interface Feature 2 Slice 3 receipt — ECS and contract explorer

Status: **accepted**  
Accepted boundary: [Slice 3 implementation document](WEB-CONTROL-CENTER-SLICE-3-IMPLEMENTATION.md)  
Recorded: **2026-08-24**

## Delivered boundary

- Added stable, capped discovery reads to the application-registry and ECS owners for applications,
  application-scoped state spaces, latest component-type versions, live entities, and live-entity
  components. Owner cursors require an existing scoped last key and never broaden application or
  state-space scope.
- Added the web-only `ControlStructureExplorer` and thirteen GET-only
  `/api/control/structure/*` routes. They provide summaries first, exact component values and
  immutable schema versions on selection, opaque scope-bound web cursors, stable 400/404/409
  failures, and `Cache-Control: no-store`.
- Kept `IPublicApplicationCatalogProvider` as the sole publication boundary. An absent production
  provider reports `unavailable`; an explicitly supplied empty navigator reports `empty`; an
  explicitly public navigator supports its existing signed browse/search cursors and exact record
  reads. Files, SQLite rows, previews, and source registrations are never treated as proof of
  public access.
- Updated `<ecs-explorer>` to lazily browse applications, state spaces, live entities, component
  values, exact schemas, and public catalog collections/search/records. Dynamic values are rendered
  with DOM text nodes, and the panel retains independent loading, empty, forbidden, failure, and
  retry states.

No application, ECS, schema, catalog, page, event, or operation write was added. There is no new
table, migration, catalog activation/import, catalog record, game-rule interpretation, D&D
special case, settings/assistant/site-editor implementation, or MCP surface change.

## Verification evidence

- Focused application-registry, ECS, catalog-navigation, and web tests before the overlapping
  application-activation work changed migration state: **67 passed**, 0 failed.
- Final Slice 3 invariant selection on the settled tree: **11 passed**, 0 failed; the exact explorer
  test also verifies SQLite `total_changes()` is identical before and after success and failure
  reads.
- Solution build: **passed**, 0 warnings and 0 errors.
- Catalog validation: **passed**, 144 records validated; 17 existing near-duplicate warnings and no
  live-data change.
- Disposable local HTTP/browser walk:
  - uploaded the source control-center page into a disposable database only;
  - `ecs-explorer` reached `ready` and truthfully rendered the empty-application state;
  - status and effect history stayed functional while unfinished panels remained unavailable;
  - the structure list returned 200/no-store and an invalid limit returned the stable
    `INVALID_LIMIT` 400/no-store result; and
  - no browser-console errors occurred.
  The disposable host was stopped after the check.
- `git diff --check`: no whitespace errors; the working copy reports only existing line-ending
  warnings for tracked files.

The final full solution run completed **19/19** local-AI tests and **604/606** shared tests. Its two
failures are confined to concurrently added application-activation tables and columns that have not
yet been classified by the repository's catalog round-trip coverage test. Migration drift, catalog
validation, and the MCP walk pass after that overlapping work's migration landed. Slice 3 adds no
migration or activation model, and its focused invariants and clean solution build pass on that
same settled tree.

## Deliberate exclusions and next gate

The production public catalog remains deliberately unavailable until a separate activation owner
publishes an explicitly public navigator. Slice 4 is the next ordered control-center leaf, but its
inactive-draft, sandbox-preview, optimistic publish/rollback, and self-edit recovery semantics still
require confirmation before implementation. Settings, conversations/local AI, and Codex retain
their own Sol gates.

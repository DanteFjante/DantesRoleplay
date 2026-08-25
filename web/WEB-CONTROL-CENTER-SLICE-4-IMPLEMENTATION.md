# Web Interface Feature 2 Slice 4 implementation — in-browser site editing

Status: **accepted**  
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md)  
Dependency tree/leaf: [Control-center dependency plan](WEB-CONTROL-CENTER-DEPENDENCY-PLAN.md), in-browser site editing  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Let an authorized operator list existing database-authored pages, inspect immutable revisions, append an inactive HTML draft, preview/export it, and explicitly publish or roll back an exact revision.  
Exclusions: new-page creation through the editor, revision/asset deletion or mutation, automatic publish, arbitrary filesystem editing, hostile-content support, external preview connections, schema migration, catalog/game-state changes, settings, assistants, Codex, and MCP changes.  
Allowed files/areas: existing web page contracts/models/store/tests; control routes/security/projection; `<site-editor>` source bundle; web documentation and Feature 2 status/receipt.  
Stop point: `<site-editor>` supports confirmed editing of existing pages and the remaining settings/assistant panels stay unchanged.

## Confirmed decisions

- The user's **continue** on 2026-08-24, after being told Slice 4 required these semantics, confirms inactive drafts, isolated preview, optimistic publish/rollback, and self-edit recovery.
- Existing `web_page`, `web_page_revision`, and `web_page_asset` rows retain their meaning. No migration is authorized.
- Because `web_page.ActiveRevision` is non-null and positive, the editor drafts only existing pages. New page creation remains the existing explicit `/api/pages/{id}` or bundle-upload recovery path, which activates revision 1.
- A draft appends one immutable revision without changing `ActiveRevision` or `UpdatedAt`. It copies the exact base revision's assets and replaces only its HTML.
- Draft append requires `expectedLatestRevision` and `baseRevision`. Publish/rollback requires `expectedActiveRevision` and an exact target `revision`. The server derives the next revision, timestamps, hashes, active state, and asset metadata.
- Page order is ordinal page ID ascending. Revision order is revision number descending. List limits are 1–100, default 25. Web cursors are base64url `{kind,scope,pageSize,lastKey}`, at most 1024 characters, and return 400 malformed / 409 wrong-scope-or-size / 409 missing-key restart results.
- HTML keeps the existing non-empty, valid UTF-8, 1 MiB boundary. JSON write bodies are capped at `MaximumHtmlBytes + 4096`; assets remain subject to existing bundle limits.
- Preview HTML is served only for an exact immutable revision with `Cache-Control: no-store`, `connect-src 'none'`, `form-action 'none'`, `object-src 'none'`, no external resource origins, and framing limited to the same site. The UI iframe uses `sandbox="allow-scripts"` without `allow-same-origin`, so preview scripts cannot read or mutate same-origin control APIs.
- Editing `control-center` is allowed. The UI shows the unchanged CLI/direct upload path as recovery before activation; those existing routes remain tested and available.

## Prerequisite evidence

- [Slice 0 receipt](WEB-CONTROL-CENTER-SLICE-0-RECEIPT.md) verifies `control.read`, `control.pages.write`, same-origin JSON mutation checks, and closed control-route mapping.
- [Slice 1 receipt](WEB-CONTROL-CENTER-SLICE-1-RECEIPT.md) verifies the uploadable control-center shell and independent panel convention.
- Existing `IWebPageStore` / `WebPageStore` own immutable revisions, active pointer changes, revision-scoped assets, transactions, and existing direct upload recovery.
- Existing web migrations already contain every required field and constraint; this slice changes no schema meaning.

## Runtime artifacts

- Revised owner records: `WebPageSummary`, `WebPageDiscoveryPage`, `WebPageRevisionSummary`, `WebPageRevisionDiscoveryPage`, `WebPageRevisionDocument`, and `WebPageActivationResult`.
- Revised `IWebPageStore`: `ListPageAsync`, `GetSummaryAsync`, `ListRevisionsAsync`, `GetRevisionAsync`, `AppendDraftAsync`, and `ActivateRevisionAsync`; existing upload/active reads stay compatible.
- New `WebPageStoreException` carries stable owner failure codes.
- New web-only `ControlPageEditor` owns opaque cursors, bounded JSON-body parsing, response projection, ZIP materialization, and preview headers; it never queries tables directly.
- Confirmed routes:
  - `GET /api/control/pages`
  - `GET /api/control/pages/{pageId}`
  - `GET /api/control/pages/{pageId}/revisions`
  - `GET /api/control/pages/{pageId}/revisions/{revision:int}`
  - `GET /api/control/pages/{pageId}/revisions/{revision:int}/bundle`
  - `GET /api/control/pages/{pageId}/revisions/{revision:int}/preview/index.html`
  - `GET /api/control/pages/{pageId}/revisions/{revision:int}/preview/assets/{**path}`
  - `POST /api/control/pages/{pageId}/drafts` using `control.pages.write`
  - `PUT /api/control/pages/{pageId}/active` using `control.pages.write`
- Draft body: `{ "expectedLatestRevision": int, "baseRevision": int, "html": string }`.
- Activation body: `{ "expectedActiveRevision": int, "revision": int }`.

## Authoritative state and closed input

The store derives current active/latest revisions, next revision, timestamps, UTF-8 SHA-256 content hash, asset count/bytes/hashes, and activation result. The browser supplies only a route-safe existing page ID, exact immutable revision IDs, optimistic revision tokens, bounded HTML, cursor, and page size. It cannot supply storage IDs/paths, content hashes, timestamps, active flags, asset bytes, authorization, or filesystem locations.

## Behavior, result, and typed effects

- Page summaries expose `id`, `activeRevision`, `latestRevision`, and active-pointer `updatedAt`.
- Revision summaries expose `pageId`, `revision`, `isActive`, `createdAtUtc`, HTML `contentHash`, `assetCount`, and `assetBytes`. Exact detail additionally returns HTML and asset summaries; asset bytes remain exact-owner/export/preview data only.
- Draft append validates the existing page, exact base revision, current latest token, HTML, and copied bundle bounds before one web-database transaction appends revision `expectedLatestRevision + 1`. The active pointer is unchanged.
- Activation validates the target revision, compares the current active token, and changes only `ActiveRevision` plus `UpdatedAt` in one web-database transaction. Reactivating an older immutable revision is rollback; activating a draft is publish.
- Export creates an in-memory ZIP containing root `index.html` and the exact revision's assets. Preview reads the same exact revision and never activates it.
- These are web-content transitions only. No game typed effect, operation row, event, or catalog record is created.

## Failure, replay, and rollback contract

- Invalid ID/revision/limit/cursor/body returns stable 400; oversized body/HTML returns 413; unknown page/revision/asset returns 404; stale latest/active/cursor or an already-active target returns 409; unauthorized or wrong-origin writes fail before owner invocation.
- Replaying a draft body after success is stale because latest revision advanced and appends nothing. Replaying activation with the old expected active revision is stale and changes nothing.
- Validation or injected persistence failure rolls back the new revision/assets and pointer update. Draft failure never changes the active page. Activation failure leaves the prior active revision.
- Preview and export are read-only. Preview scripts have no network connection channel and the opaque iframe origin cannot access control APIs.
- Existing direct HTML/ZIP upload remains the recovery path if a published `control-center` revision is broken.

## Implementation sequence

1. Add owner records/methods and focused persistence tests without changing tables or existing upload semantics.
2. Add the web projection, bounded body/cursor handling, read/write routes, exact export, and preview policy.
3. Replace only `<site-editor>` with list/edit/draft/preview/publish/rollback/export behavior and explicit control-center recovery guidance.
4. Run focused web tests, solution build, full suite, browser walk, catalog validation only if component metadata changes, and `git diff --check`; write the receipt and stop.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Empty/multiple pages and descending revisions | owner/projection tests |
| Draft copies base assets and never activates | persistence tests |
| Stale draft/activation and replay | 409/no-change tests |
| Exact publish and older-revision rollback | active-pointer tests |
| Injected draft/activation failure | transaction rollback tests |
| Exact detail, asset metadata, ZIP export | store/web tests |
| Preview CSP, opaque iframe, no control/external connection | header/source/browser tests |
| Wrong identity/origin and GET-only read routes | existing control guard plus route metadata tests |
| `control-center` recovery | legacy upload route/store compatibility test and UI guidance |
| No migration/MCP/catalog/game-state change | diff/build/catalog evidence |

## Verification commands

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter "FullyQualifiedName~WebInterfaceTests"`
- `dotnet build DantesRoleplay.slnx --no-restore`
- `dotnet test DantesRoleplay.slnx --no-restore`
- `git diff --check`

## Completion receipt and exit gate

Record evidence in `web/WEB-CONTROL-CENTER-SLICE-4-RECEIPT.md`, update Feature 2 status once, and stop before settings, conversations/local AI, Codex, catalog activation, or any new-page editor semantics.

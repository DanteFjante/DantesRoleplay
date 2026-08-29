# Web Interface Feature 1 Slice 2 implementation — versioned ZIP page bundles

Status: **accepted — delivered by [Slice 2 receipt](WEB-INTERFACE-SLICE-2-RECEIPT.md)**  
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md)  
Dependency tree/leaf: [Versioned page bundles](WEB-INTERFACE-DEPENDENCY-TREE.md#ordered-leaves)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Upload one bounded ZIP bundle containing `index.html` and its static assets, activate the
result as one immutable page revision, and serve assets only from that active revision.  
Exclusions: Individual asset mutation, partial bundle updates, archive formats other than ZIP,
server-side HTML rewriting, content transformation, executable server code, SSE, authentication,
authorization, sandboxing, CSP design, remote binding, game-state writes, and UI polish.  
Allowed files/areas: `web/`, `src/system/web-interface/`, the web-owned EF migration/snapshot,
focused web tests, and status/link-only changes in the web roadmap and dependency tree.  
Stop point: Focused tests and solution build pass; the existing HTML/data routes and MCP surface
remain compatible; record the receipt and stop before SSE or security work.

## Confirmed decisions

- The user's 2026-08-24 request to implement the web dependency plan authorizes roadmap Slice 2's
  asset/ZIP capability and its required web-owned migration/public HTTP additions.
- The permanent upload route is `PUT /api/pages/{id}/bundle` with `application/zip`.
- A bundle contains one required root `index.html`; every other regular entry is a static asset.
- The permanent asset route is `GET /ui/{id}/assets/{**path}`. The same active HTML is also served
  at `GET /ui/{id}/index.html`, allowing ordinary `assets/...` relative references while retaining
  the accepted `GET /ui/{id}` route unchanged.
- Static asset bytes and metadata are append-only children of one page revision in the existing
  web-owned SQLite database. Activating HTML and all assets is one web transaction.
- Closed safety limits are 10 MiB compressed input, 256 regular entries, 5 MiB per entry, 25 MiB
  total uncompressed bytes, 1 MiB for `index.html`, and 240 characters per asset path.

## D&D 5e 2024 alignment

No D&D rule, term, formula, eligibility decision, state, or outcome is introduced.

## External implementation reference

No Foundry review is relevant to ruleset-neutral ZIP validation and static HTTP asset serving.

## Prerequisite evidence

- [Slice 1 receipt](WEB-INTERFACE-SLICE-1-RECEIPT.md) proves append-only page revisions, active HTML
  serving, dynamic state reads, host composition, and the web-owned migration boundary.
- `WebPageStore` owns page revision activation and its SQLite transaction.
- `WebInterfaceTests` proves unchanged HTML, revision ordering, invalid-input no-change behavior,
  and opaque dynamic JSON reads.

## Runtime artifacts

- `WebPageAsset` rows keyed to immutable `WebPageRevision` rows, with path, media type, SHA-256
  content hash, and bytes.
- One forward web-owned EF migration and updated web model snapshot.
- A bounded ZIP reader producing one closed `WebPageBundle` input.
- The bundle upload, active-index alias, and active-asset HTTP routes named above.
- No catalog ID, component schema, mechanic, procedure, application ID, MCP kind, or game-state
  migration.

## Authoritative state and closed input

- SQLite remains authoritative for page revisions and their assets.
- Callers supply only a route-safe page ID and ZIP bytes. They cannot supply revision numbers,
  timestamps, hashes, media types, active pointers, filesystem paths, database identifiers, or SQL.
- The backend derives the next revision, normalized archive paths, media types, hashes, and time.
- ZIP paths must use `/`, be relative, contain no empty, `.` or `..` segment, contain no control
  character, colon, query/fragment delimiter, or percent-escape marker, and be unique under ordinal
  comparison. All assets live below root
  `assets/`; directory entries are ignored.

## Behavior, result, and typed effects

- The reader validates the entire ZIP before opening a transaction. It decodes root `index.html`
  as strict UTF-8 and rejects an empty document.
- A successful save appends one page revision and all asset rows, then atomically changes the page's
  active pointer. Older revisions and assets remain immutable recovery evidence.
- Bundle upload returns the page ID, new revision, asset count, and `/ui/{id}/index.html` URL.
- Asset reads select only the active page revision and return the exact bytes with a backend-derived
  media type. Missing pages/assets return `404`; reads never mutate state.
- Existing direct `text/html` uploads create a revision with no assets and remain unchanged.

## Failure, replay, and rollback contract

- Wrong content type returns `415`; malformed ZIP, missing/duplicate `index.html`, unsafe/duplicate
  paths, invalid UTF-8 HTML, or any exceeded limit returns stable `400`/`413` without a revision.
- Empty, encrypted, split, or otherwise unreadable entries are rejected where they prevent a
  complete deterministic materialization.
- Repeating a valid upload intentionally creates another immutable revision.
- Any persistence failure rolls back the revision, assets, and active pointer together.
- An asset from an inactive revision is never returned through the active route.

## Implementation sequence

1. Add bundle/asset models, validation, store behavior, and focused tests.
2. Add the web-owned forward migration and inspect its generated SQL/model snapshot.
3. Add the three HTTP mappings and update the local usage documentation.
4. Run focused tests, solution build, compatibility checks, and write the receipt.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Positive | A ZIP with `index.html`, CSS, JavaScript, and nested assets creates one revision and exact active reads. |
| Replacement | A later bundle activates only its own asset set while retaining the older immutable rows. |
| Boundary | Traversal, rooted/backslash, duplicate, missing-index, malformed, and oversized inputs make no write. |
| Deterministic | The same valid ZIP yields identical normalized paths, media types, hashes, and bytes. |
| Rollback | Injected persistence failure leaves the prior active revision and assets unchanged. |
| Compatibility | Direct HTML upload, `/ui/{id}`, dynamic JSON routes, solution build, and MCP surface remain unchanged. |

## Verification commands

- Focused `WebInterfaceTests`.
- `dotnet build DantesRoleplay.slnx --no-restore`.
- Existing protocol-surface tests because the shared host gains routes, not MCP kinds.
- `git diff --check`.

## Completion receipt and exit gate

Delivered behavior and verification are recorded in
[`WEB-INTERFACE-SLICE-2-RECEIPT.md`](WEB-INTERFACE-SLICE-2-RECEIPT.md). Slice 2 is accepted; stop
before SSE, authentication, isolation, CSP, quotas, or remote deployment.

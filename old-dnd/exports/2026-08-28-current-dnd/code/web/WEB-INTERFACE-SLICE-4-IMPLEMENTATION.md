# Web Interface Feature 1 Slice 4 implementation — local single-user hardening

Status: **accepted — delivered by [Slice 4 receipt](WEB-INTERFACE-SLICE-4-RECEIPT.md)**  
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md)  
Dependency tree/leaf: [Local web hardening](WEB-INTERFACE-DEPENDENCY-TREE.md#ordered-leaves)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Finish the web interface for its selected local single-user deployment by rejecting
non-loopback clients, making the trusted-content boundary explicit, applying restrictive browser
headers, and bounding request/upload pressure.  
Exclusions: Remote deployment, account/password/OAuth authentication, reverse-proxy trust,
multi-user authorization, sanitizing or rewriting trusted HTML, separate-origin rendering,
containerization, replacing local AI with Codex, changing MCP access, and game-state writes.  
Allowed files/areas: `web/`, `src/system/web-interface/`, the shared host's web middleware call,
focused web tests, and the web component manifest. No migration or non-web semantic change.  
Stop point: Focused/full tests and a local HTTP hardening walk pass; record the receipt and mark the
local web roadmap complete while leaving remote identity/deployment as an optional later project.

## Confirmed decisions

- On 2026-08-24 the user selected the simplest realistic alternative: the web interface runs
  locally for one operator; ChatGPT continues to use its existing MCP access separately.
- Loopback reachability is the access boundary. Web routes reject missing or non-loopback remote
  addresses before reading or writing data. No account database or shared secret is introduced.
- Uploaded HTML remains operator-trusted executable content. It may use same-origin scripts,
  styles, assets, JSON reads, and SSE; it is not sanitized or treated as hostile third-party code.
- Restrictive response headers prevent framing, plugins, external network/resource loading, base
  URL changes, form submission, MIME sniffing, referrer leakage, and unnecessary browser features.
  Inline script/style remain allowed because stored pages are ordinary authored HTML.
- Request limits are fixed web-owned policies: 240 read requests/minute, 10 uploads/minute, and
  four concurrent SSE streams, with no queue. Direct HTML is capped at the existing 1 MiB HTML
  limit; bundle limits remain unchanged.
- Remote deployment and identity are removed from the local completion gate. They require a future
  host/identity decision and must not be implied by this receipt.

## D&D 5e 2024 alignment

No D&D rule, term, formula, eligibility decision, state, or outcome is introduced.

## External implementation reference

No Foundry review is relevant to ruleset-neutral local HTTP access and browser hardening.

## Prerequisite evidence

- [Slice 3 receipt](WEB-INTERFACE-SLICE-3-RECEIPT.md) proves the complete local page/data/bundle/SSE
  capability and its passing HTTP walk.
- `MapDantesRoleplayWeb` owns every web route, allowing one closed filter/header/rate-limit boundary
  without changing the MCP endpoint.
- `WebPageBundleLimits.MaximumHtmlBytes` already owns the page HTML ceiling for bundle uploads.

## Runtime artifacts

- Web-owned loopback access and response-security endpoint filters.
- Named ASP.NET rate-limit policies for reads, uploads, and SSE concurrency.
- One bounded strict-UTF-8 direct HTML reader reusing the 1 MiB limit.
- One host middleware composition call for rate limiting.
- A revised web component ownership statement and local usage documentation.
- No catalog ID, game schema, mechanic, procedure, MCP kind, database table, index, or migration.

## Authoritative state and closed input

- The TCP peer address supplied by ASP.NET is authoritative for local access. Forwarded headers,
  Host, Origin, and caller-supplied identity headers are ignored.
- Callers supply the same route/query/body inputs accepted by Slices 1–3. They cannot select a
  trust mode, access mode, CSP, quota, client identity, or remote proxy.
- Existing SQLite/page/world owners remain authoritative; filters and limits persist no state.

## Behavior, result, and typed effects

- Every route mapped by `MapDantesRoleplayWeb` first requires an IPv4 or IPv6 loopback peer.
- Every web response receives the closed CSP plus `nosniff`, no-referrer, deny-framing,
  same-origin opener/resource, and restrictive permissions headers.
- Read routes use the read policy, page upload routes use the upload policy, and SSE uses its
  concurrency policy. Rejected requests return stable JSON with `429 WEB_RATE_LIMITED`.
- Direct HTML is buffered only up to 1 MiB and decoded as strict UTF-8 before the page transaction.
- MCP mapping, transport, access behavior, and protocol kinds remain unchanged.

## Failure, replay, and rollback contract

- A non-loopback or missing peer returns `403 LOCAL_ACCESS_REQUIRED` without invoking the route.
- Limit exhaustion returns `429 WEB_RATE_LIMITED` without queueing or invoking the route.
- Oversized direct HTML returns `413 HTML_TOO_LARGE`; invalid UTF-8 returns
  `400 INVALID_HTML_ENCODING`; neither creates a revision.
- Security headers apply to success and ordinary web error responses. The loopback filter's own
  rejection also includes them.
- Retried accepted uploads retain the existing append-only revision semantics. Rejected inputs and
  requests make no persistent change.

## Implementation sequence

1. Add access/header policy and bounded HTML reader with focused tests.
2. Group the web routes under those filters and named rate-limit policies.
3. Compose rate-limit middleware in the host and update the component/readme boundaries.
4. Run focused tests, solution build/full suite, protocol compatibility, HTTP hardening walk, and
   write the receipt.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Positive | Loopback requests retain all accepted page/data/bundle/SSE behavior and security headers. |
| Access | IPv4/IPv6 loopback pass; remote and missing peer fail closed before route execution. |
| Trust | CSP permits same-origin/inline authored pages and denies framing, plugins, forms, base changes, and external connections. |
| Boundary | The eleventh upload in one minute returns 429; a fifth concurrent SSE stream returns 429. |
| Input | Direct HTML at the limit succeeds; oversized or invalid UTF-8 input creates no revision. |
| Rollback/replay | Rejected requests make no write; accepted repeated uploads remain append-only. |
| Compatibility | MCP surface and Slices 1–3 remain unchanged; build and full suite pass. |

## Verification commands

- Focused `WebInterfaceTests`.
- `dotnet build DantesRoleplay.slnx --no-restore`.
- Full solution tests.
- Existing protocol/manifest-guard tests because the shared host gains middleware composition.
- Local HTTP header, upload-limit, and SSE-concurrency walk against a disposable database.
- `git diff --check`.

## Completion receipt and exit gate

Delivered behavior and verification are recorded in
[`WEB-INTERFACE-SLICE-4-RECEIPT.md`](WEB-INTERFACE-SLICE-4-RECEIPT.md). The local interface is
complete; remote identity/deployment and Codex-as-local-AI remain separately confirmed future work.

# Application kernel Slice 10A implementation — public read-only catalog protocol

Status: **accepted**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Generic application kernel dependency plan](APPLICATION-KERNEL-DEPENDENCY-PLAN.md), H / read-only catalog queries  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Expose the accepted Slice 9 catalog navigator through four `system.*` query kinds while
retaining exactly the existing `orient`, `query`, and `commit` transport verbs.  
Exclusions: Administrative queries/commits, application/source registration or activation,
authorization implementation, private catalog access, database migration/persistence, legacy-kind
removal, game content/rules, vector search, local AI, and interaction orchestration.  
Allowed files/areas: `src/system/catalog-navigation/{domain,tests}/`,
`DantesRoleplay.MCPServer/{Tools,ServerConfiguration.cs}`, public-surface guard/protocol tests,
`catalog/procedures/system/procedure.system.use.md`, component manifests, this document, its
receipt, and link/status-only dependency-plan updates.  
Stop point: Stop when public read-only catalog discovery is advertised, dispatched, audited,
recoverable, and protocol-walked. Do not expose any `system.*` commit or non-catalog administrative
query before E9 authorization is accepted.

## Confirmed decisions

The user replied “Continue” on 2026-08-24 directly after being told Slice 10 required confirmation
for its public request/response and authorization boundary. This confirms the read-only catalog
surface below. Slice 0 S0.8 already reserves the exact permanent kind names.

| Kind | Closed request fields | Result |
| --- | --- | --- |
| `system.catalogs` | `applicationId` | bounded public collection summaries |
| `system.catalog.browse` | `applicationId`, `collection`, optional `branch`, `pageSize`, `cursor` | node, breadcrumbs, counts, direct entries, next cursor |
| `system.catalog.search` | `applicationId`, `query`, optional `collection`, `branch`, `kinds`, `statuses`, `pageSize`, `cursor` | ranked summaries and next cursor |
| `system.catalog.record` | `applicationId`, `collection`, `id` | exact public effective record and provenance |

The provider port is explicitly `public`: it may return only an already authorization-filtered
manifest whose fingerprint/cursors/counts describe that same visible set. The production default
is empty/deny. This slice cannot infer visibility from loopback, application ownership, source
trust, or the absence of an authorization service.

## Prerequisite evidence

- [Slice 0 S0.7–S0.10](APPLICATION-KERNEL-SLICE-0-IMPLEMENTATION.md) confirms catalog behavior,
  the four permanent query kind names, three-verb transport, authorization-before-counts, and the
  AI-independent path.
- [Slice 9 receipt](receipts/APPLICATION-KERNEL-SLICE-9-RECEIPT.md) proves immutable scoped
  manifests, deterministic navigation, exact inspect, and authenticated cursors.
- `VerbSurface`, `QueryTool`, `ToolRunner`, capability guards, bootstrap entry contract, and the
  protocol walk are the existing public-surface owners and remain authoritative.

## Runtime artifacts and behavior

- `IPublicApplicationCatalogProvider` retrieves one prefiltered public manifest; its empty default
  reveals no application existence or catalog content.
- The query dispatcher parses the opaque application ID, resolves its public manifest, constructs
  the Slice 9 navigator, maps results into the uniform `ToolEnvelope`, and records the public
  `query` audit identity with subject `query:<kind>`.
- Every success includes literal next calls for traversal or exact inspection. Every failure has a
  stable code and callable recovery: `INVALID_APPLICATION`, `PUBLIC_CATALOG_UNAVAILABLE`,
  `INVALID_PAYLOAD`, `CATALOG_COLLECTION_UNKNOWN`, `CATALOG_NODE_UNKNOWN`,
  `CATALOG_RECORD_UNKNOWN`, `CURSOR_INVALID`, or `CURSOR_STALE`.
- Missing provider/application, malformed fields, wrong scope, hidden content, cursor tampering,
  and stale cursors return no catalog data. Querying never mutates application/runtime state.

## Implementation sequence and acceptance

1. Add the public-manifest provider seam and empty/in-memory implementations.
2. Add four surface specs/parameters, tool descriptions, dispatch cases, and a thin adapter.
3. Update the existing system-use contract and guard parser for dotted kind names.
4. Add direct adapter tests plus an actual MCP protocol walk with a public fixture manifest.
5. Run focused tests, `roleplay validate catalog`, full shared/local-AI suites, warning-free build,
   protocol walk, and `git diff --check`; record the receipt.

Acceptance requires capability/dispatch/description agreement, exactly three MCP tools, all result
and failure shapes over the live transport, audit evidence, cursor continuation/tamper/stale proof,
empty-provider non-disclosure, and no new commit kind or game/AI dependency.

## Completion receipt and exit gate

Acceptance evidence is recorded in
[the Slice 10A receipt](receipts/APPLICATION-KERNEL-SLICE-10A-RECEIPT.md). Administrative system
queries and all `system.*` commits remain blocked on a separately confirmed authorization slice.

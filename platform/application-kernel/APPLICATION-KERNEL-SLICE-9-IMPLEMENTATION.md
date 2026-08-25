# Application kernel Slice 9 implementation — deterministic catalog navigation

Status: **accepted**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Generic application kernel dependency plan](APPLICATION-KERNEL-DEPENDENCY-PLAN.md), G  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Provide bounded, deterministic, application-scoped catalog navigation over an immutable
effective-manifest snapshot, without vector search or AI.  
Exclusions: Application activation, catalog file parsing/import, source scanning/overlay execution,
database persistence/migration, authorization, public protocol registration, legacy migration,
game content/rules, vector search, and AI work.  
Allowed files/areas: `src/system/catalog-navigation/{domain,tests}/`, its `component.json`, this
document, its receipt, and link/status-only dependency-plan updates.  
Stop point: Stop after an in-process navigator proves described traversal, lexical discovery,
inspection, and authenticated snapshot-bound cursors. Slice 10 owns public transport; activation
and import own creation of effective manifests.

## Confirmed decisions

Slice 0 S0.7 supplies the active semantics: application-authored logical nodes; normalized `/`
paths; explicit missing-description status for legacy nodes; children then records in stable order;
default page size 25 and maximum 100; invariant Unicode lexical ranking; and authenticated cursors
bound to a manifest, scope, filter, sort version, page size, and last stable key. S0.10 requires
that this component work with vectors and local AI disabled.

This slice introduces only internal C# contracts. It creates no permanent catalog record, database
row/table, migration, public kind, or endpoint.

## Prerequisite evidence

- [Slice 0](APPLICATION-KERNEL-SLICE-0-IMPLEMENTATION.md), S0.1 and S0.7–S0.10, confirms
  namespace, catalog, cursor, and AI-boundary semantics.
- [Slice 4 receipt](receipts/APPLICATION-KERNEL-SLICE-4-RECEIPT.md) proves deterministic immutable
  effective-source manifests, but intentionally does not parse catalog declarations or activate an
  application revision.
- [Catalog cursor contracts](../../src/system/catalog-navigation/domain/CatalogCursorContracts.cs)
  already provide authenticated payloads and are extended only to support a last-key cursor whose
  static scope can be validated independently.

## Runtime artifacts

- Immutable catalog-manifest, collection, node, and effective-record contracts whose data has
  already been selected by an activation/import owner.
- `ICatalogNavigator` plus an in-memory deterministic implementation for collection listing,
  direct-node browsing, lexical search, and exact record inspection.
- Cursor scope decoding that preserves the existing authenticated envelope while allowing a
  subsequent-page key to differ from the request's static scope.

## Authoritative state and closed input

The supplied immutable effective manifest is authoritative for one application revision. It carries
the application ID, manifest fingerprint, described logical collections/nodes, effective records,
content hash, version, and redacted source provenance. The caller may choose a collection, logical
branch, bounded page size, optional opaque cursor, and bounded lexical filters. It cannot provide
a manifest fingerprint, result score, cursor key, count, source path, hidden record, or another
application's record.

## Behavior, result, and typed failures

- Every collection has a root node; every record path and node ancestor exists in the same
  collection. A node either has authored title/description or is explicitly `missing` description
  status. The kernel never invents node prose.
- Browse returns selected-node metadata, breadcrumbs, direct/subtree counts by record kind, then a
  combined page of direct child nodes (path order) followed by direct records (kind/qualified-ID
  order). Empty and root branches are supported.
- Search normalizes Unicode invariantly and ranks exact qualified ID, exact alias/match phrase,
  exact name, prefix, then all-token textual matches. Ties use `(record kind, qualified ID)`.
- Inspect returns one exact effective record from the manifest with its hash, version, and redacted
  provenance; summaries never substitute for an inspect result.
- Cursor results carry a stable opaque successor. Tampering is `CURSOR_INVALID`; changed manifest
  or query scope is `CURSOR_STALE`; unavailable retained manifests/keys remain future-provider
  work and are not fabricated here.
- Invalid scopes, paths, filters, duplicate IDs, unbounded input, or inconsistent manifests fail
  before any result. This component is read-only and has no transaction or mutation path.

## Implementation sequence

1. Define/copy/validate immutable manifest and result contracts, including logical identifiers,
   description status, exact provenance, bounded request filters, and ranking version.
2. Extend cursor decoding with a static scope type; retain the existing exact-binding overload.
3. Implement only deterministic in-memory navigation and focused generic fixtures.
4. Verify focused tests, existing application/source/schema/ECS regressions, full suite, local-AI
   suite, solution build, and whitespace validation; record a receipt.

## Acceptance matrix

| Area | Required evidence |
| --- | --- |
| Manifest | Rejects cross-app IDs, missing roots/ancestors, duplicate nodes/records, unbounded fields, and fabricated descriptions. |
| Browse | Root/intermediate/empty branches show authored metadata, correct direct/subtree kind counts, stable combined pages, and no gaps/duplicates. |
| Search | Exercises every ranking tier, Unicode normalization, filters, deterministic ties, and vector/AI-free operation. |
| Inspect | Returns exact content/version/hash/provenance; unknown or wrong-collection IDs fail. |
| Cursor | Authenticated, scope/page/key bound; continuation works; tampered and changed-scope cursors fail typed without mixed pages. |
| Isolation | Another application cannot be included in or queried through this manifest. |
| Repository | Focused navigation tests, existing kernel regressions, solution build, full shared/local-AI suites, and `git diff --check` pass. |

## Completion receipt and exit gate

Record evidence in `receipts/APPLICATION-KERNEL-SLICE-9-RECEIPT.md`. Do not begin source/catalog
import, activation, database persistence, authorization, protocol registration, legacy adoption,
or AI orchestration.

## Result

Accepted 2026-08-24. The navigator is an immutable-manifest seam only: it proves generic
described traversal, vector-free lexical discovery, exact inspect, and authenticated continuation
without claiming application activation, persisted historical manifests, authorization, or public
transport.

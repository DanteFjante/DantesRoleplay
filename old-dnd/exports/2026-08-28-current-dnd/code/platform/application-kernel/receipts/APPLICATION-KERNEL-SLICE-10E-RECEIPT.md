# Application kernel Slice 10E receipt — authenticated dependency impact

Status: **accepted**  
Completed: 2026-08-24  
Accepted implementation: [Slice 10E](../APPLICATION-KERNEL-SLICE-10E-IMPLEMENTATION.md)

## Delivered

- Added authenticated `query(kind: "system.dependencies")` while preserving exactly the three MCP
  tools `orient`, `query`, and `commit`.
- Extended the projection-materialization owner with a read-only impact snapshot and traversal
  service over immutable persisted declarations. It does not materialize values, execute code, or
  change projection registration behavior.
- Indexed exact component type/version/schema-hash plus RFC 6901 field reads and exact projection
  version/content-hash dependencies. Edges distinguish `reads-component-field` from
  `depends-on-projection`.
- Added canonical component-field and projection node IDs, deterministic inventory, direct or
  transitive dependent traversal, shortest depths, reasons, complete graph counts, and a stable
  full-graph SHA-256 fingerprint.
- Made a whole-component query conservative: it seeds every declared field read for that exact
  component type/version. Unknown extra fields remain irrelevant until declared by a consumer.
- Returned bounded details without changing complete counts or fingerprints. Coverage explicitly
  identifies `component-field` and `projection` as indexed and mechanic, procedure, event,
  subscription, and catalog consumers as deferred; the system never infers dependencies from
  JavaScript or filenames.
- Required private-operator `Read` authorization before application/node parsing or registry access.
  Closed errors distinguish invalid/unknown nodes, unknown applications, unavailable services, and
  unexpected failures without leaking internal details.
- Kept the query read-only apart from normal query audit. It adds no table, migration, candidate,
  active application, state-space change, cache, vector index, or model dependency.

## Evidence

- Focused projection-impact and authorization tests: 20 passed, 0 failed.
- Combined projection-impact, authorization, guard, bootstrap-contract, and live MCP acceptance:
  45 passed, 0 failed.
- Live JSON-RPC walk proved real component-field/projection inventory and traversal, bounded
  details with full counts, explicit incomplete coverage, invalid-limit handling, remote denial
  before invalid node parsing, and the unchanged three-tool surface.
- Full shared suite: 596 passed, 0 failed.
- Standalone local-AI suite: 19 passed, 0 failed.
- Catalog validation: 144 records valid; 17 existing near-duplicate warnings; no live data touched.
- Solution build: passed with 0 warnings and 0 errors.
- `git diff --check`: passed; Git emitted line-ending notices only.
- During final acceptance, concurrent web-control work introduced a missing ASP.NET Core namespace
  import. The one-line compile-only repair was applied in `ControlStructureExplorer.cs`; it changes
  no web behavior and is not part of the dependency-impact feature boundary.

## Deliberate exclusions and next gate

This slice does not parse candidate application documents, index mechanic/procedure/event/
subscription/catalog consumers, decide schema compatibility, persist or activate a candidate,
create or upgrade state spaces, enable remote MCP, migrate `dnd2024`, or implement AI orchestration.
Those deferred consumer declarations must be added by their owning components before the impact
report can claim complete application coverage. Slice 10F may now own exact-preview activation as a
separate transaction, but it must reject candidates whose required dependency coverage or
compatibility evidence is incomplete.

# World Feature 7 — Slice 1 implementation handoff

**Assignment ID:** world-feature-07-slice-1-generic-graph-query  
**Status:** Complete and awaiting review  
**Owning plan:** [World Feature 7 dependency plan](WORLD-FEATURE-07-DEPENDENCY-PLAN.md)  
**Exact slice:** Generic bounded graph reader and public `query(kind: "graph")`  
**Requested outcome:** A discoverable, deterministic, generic, read-only graph projection obeys
the confirmed closed request contract and hard caps.  
**Excluded work:** World recipe procedure and recipe fixtures, player filtering, maps, routes,
storage, migrations, and persistent catalog import.  
**Stop point:** Evidence is recorded in the [Slice 1 receipt](WORLD-FEATURE-07-SLICE-1-RECEIPT.md); stop before Feature 7 Slice 2.

## Confirmed public contract

- `query(kind: "graph")` has one root `id`, 1–12 selected `componentIds`, containment and
  relationship depths from 0–2, 0–12 selected `relationshipKinds`, optional `maxNodes` from
  1–100 (default 50), and optional `maxEdges` from 0–200 (default 100).
- The result is generic: ordered entity nodes, selected components, direct containment context,
  selected relationship edges, and an explicit truncation marker. It names no world component,
  relationship, fixture, visibility, or map concept.
- Invalid requests return a stable recoverable error before a projection. Missing/deleted
  relationship endpoints are errors; successful reads write no world state or audit operation.

## Allowed files

- Generic world graph models/reader and its DataAccess registration.
- Query dispatcher/capability description and generic graph query adapter.
- Focused graph, query-dispatch, and protocol-walk tests.
- This handoff and the Slice 1 receipt.

## Required verification

1. Focused graph, query, guard, and protocol-walk tests pass.
2. Public capability listing contains `graph` and its exact parameters.
3. Protocol call can materialise a graph through the registered host.
4. Full suite and `git diff --check` pass at Slice 1 acceptance.

## Escalation

Stop if a world-specific branch, a new stored projection, a migration, an authorization decision,
or a request-shape expansion beyond the confirmed contract becomes necessary.

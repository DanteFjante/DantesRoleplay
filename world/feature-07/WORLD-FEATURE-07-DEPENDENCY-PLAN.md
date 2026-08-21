# World Feature 7 dependency plan — bounded world read projections

Status: **Feature 7 verified**  
Last updated: 2026-08-20

## Target capability

A trusted GM or a future website consumer can request a small, deterministic read-only graph projection
and apply four catalog-defined world recipes: world overview, location detail, faction detail, and
knowledge detail. Each result carries only selected component data, containment context, and
selected relationships, in stable order and within hard size limits.

The runtime stays generic. It does not know what a faction, clue, rumour, secret, region, or map
is. `procedure.game.core.world.read` owns the world-specific recipes; a generic
`query(kind: "graph")` materialiser merely follows declared entity IDs, component-definition IDs,
containment, and relationship records.

This is a trusted-GM read surface, not player-safe filtering. Descriptive visibility values are
returned as authored data; authentication, audience policy, fog-of-war, and per-player discovery
remain later work.

### Included

- One generic, bounded, read-only graph query added to the existing `query` surface.
- One world read procedure that defines the four fixed Feature 7 recipes and their component and
  relationship dependencies.
- Deterministic node/edge ordering, cycle protection, caps, omission/truncation reporting, and
  exact unknown/malformed input behavior.
- Focused query, capability, protocol-walk, catalog-validation, and recipe-shape coverage.
- Map preparation only as a consumer-ready topology projection: names, containment, and canonical
  adjacency. No map geometry is authored or inferred.

### Excluded

- A world-specific C# query, a game-specific database table, stored/cached projections, migrations,
  writes, effects, events, subscriptions, notifications, or mechanics.
- Coordinates, paths, route cost, terrain, distance, line of sight, travel modes, rendered maps,
  interactive UI, or map assets. W8 owns routes/travel; W9 owns spatial/map anchors and rendering.
- Player-safe filtering, authorization, users, roles, campaigns, quests, character beliefs,
  automatic discovery, prose generation, or search/vector retrieval.
- Arbitrary recursive graph dumps, endpoint component expansion beyond declared bounds, and a
  second world topology or knowledge model.

## Source and contract basis

| Authority | Evidence | Decision supplied |
| --- | --- | --- |
| Repository feature workflow | `AGENTS.md`; `procedure.system.create-feature`; `procedure.system.modify`; `procedure.system.verify` | Confirmation boundary, generic-kernel rule, tests, catalog validation, guard tests, and protocol walk for the new public query kind. |
| Existing read surface | `QueryTool`, `WorldTools`, `VerbSurface`, and `ProtocolWalkTests` | Current `world` and `entities` reads are generic inspection calls; neither offers bounded multi-entity graph materialisation. |
| Generic world structures | `procedure.world.model`; `procedure.world.change` | A projection reads entities/components/containment/relationships; it never adds a world table or changes authoritative state. |
| World topology | `procedure.game.core.world.location` | Root/location data stays closed; containment and canonical `game.core.world.location.connected-to` remain authoritative. |
| Factions and motives | `procedure.game.core.world.faction` | Faction detail consumes explicit member/control/alliance/opposition edges and optional motive data without copying them. |
| Knowledge | `procedure.game.core.world.knowledge` | Knowledge detail consumes in-world/about/support edges and descriptive visibility; it must not claim access control. |
| Visibility boundary | `GAME_SYSTEM_MASTER_PLAN.md`, Visibility | Trusted MCP sessions are GM scope today; the website cannot claim player-safe results before authenticated audience policy exists. |

The present `query(kind: "world")` returns component-definition inventory and samples, while
`query(kind: "entities")` returns either summaries or one entity's full immediate context. Those
calls remain supported. Feature 7 adds a composable, capped read model rather than changing their
meaning or duplicating their data.

## Ownership and confirmation boundary

The following requires one explicit semantic/public-surface confirmation before implementation:

| Artifact | Proposed meaning |
| --- | --- |
| `procedure.game.core.world.read` | Defines the authoritative trusted-GM recipes below, their source components/relationships, visibility disclaimer, ordering, and no-map-data boundary. |
| `query(kind: "graph")` | A generic public read kind that materialises a caller-specified bounded graph. It knows no `game.core.*` identifiers or world semantics. |
| `GraphQuery` / `GraphProjection` runtime types | Generic MCP/DataAccess request and response models for selected components, containment, relationship edges, references, limits, and truncation. Names are implementation-local, not catalog IDs. |

Confirmation must also choose the maximum result bounds in the next section. These limits are part
of the public behavior, not tuning values callers may silently bypass.

## Generic graph-query contract

### Closed request

`query(kind: "graph")` accepts exactly these fields in addition to the established `query` envelope:

~~~text
id: required, one nonblank existing root entity ID
componentIds: required, unique array of 1–12 declared component-definition IDs
containmentDepth: required integer, 0–2
relationshipKinds: required, unique array of 0–12 nonblank relationship kinds
relationshipDepth: required integer, 0–2
maxNodes: optional integer, 1–100; defaults to 50
maxEdges: optional integer, 0–200; defaults to 100
~~~

Missing, `null`, non-object, unknown, duplicate, blank, undeclared, out-of-range, or wrong-type
values fail before reading a partial result. Empty `relationshipKinds` means containment-only; empty
`componentIds` is invalid because an unselected all-component dump is not a bounded projection.
`containmentDepth` follows descendants only. `relationshipDepth` follows selected incoming and
outgoing edges from already selected nodes. Each accepted relationship endpoint becomes a selected
node at its discovered depth and exposes only the requested components; it is not expanded beyond
the requested relationship depth.

The graph begins at `id`. Each selected node exposes identity, direct containment identity/slot
when present, and only its components whose IDs appear in `componentIds`. Every selected
relationship exposes `fromEntityId`, `toEntityId`, `kind`, and raw object data. An endpoint reached
at the terminal depth is returned but never becomes a further traversal root.

### Determinism and bounds

- Traverse containment before relationships; within each category order by entity ID, then by
  containment slot or relationship kind/from/to tuple.
- De-duplicate nodes and relationships by their permanent identity/triple. A cycle cannot re-add
  or re-expand a node.
- Never read deleted endpoints; report a dangling/corrupt relationship as a stable failure rather
  than returning a misleading partial topology.
- Stop before exceeding `maxNodes` or `maxEdges`. Return the included projection plus a
  `truncated` object naming the first exhausted cap and the count omitted when determinable.
- A query that fits returns `truncated: null`. No successful response silently drops an eligible
  node or edge.
- The result never changes authoritative world state and makes no promise that it is a transaction
  snapshot beyond the repository's normal read consistency. Like every public query, it records
  the normal read audit entry; that entry is not a world-state change.

The implementation may share existing world-store reads and add a generic projection-reader
abstraction. It must not special-case component IDs, relationship kinds, visibility values, or
fixture entity IDs in C#.

## World recipe contract

`procedure.game.core.world.read` publishes the following trusted-GM recipes. The procedure records
the exact requests; callers may use the generic graph reader for other bounded administrative
views, but the website's initial world pages consume only these recipes.

| Recipe | Root and traversal | Selected components | Relationship kinds | Result boundary |
| --- | --- | --- | --- | --- |
| World overview | Active world-root ID; containment depth 2; relationship depth 1; 100 nodes/100 edges | `game.core.world.root`, `game.core.world.location` | `game.core.world.location.connected-to` | Root, regions/places under it, and adjacency among returned locations. It has no coordinates, paths, or map claims. |
| Location detail | One location ID; containment depth 1; relationship depth 1; 50 nodes/50 edges | `game.core.world.location` | `game.core.world.location.connected-to` | The location, direct contents, parent identity, and incident canonical adjacency. It does not recursively expose the region or the entire world. |
| Faction detail | One faction ID; containment depth 0; relationship depth 1; 40 nodes/50 edges | `game.core.world.faction`, `game.core.world.motive` | `game.core.world.faction.member`, `game.core.world.faction.controls`, `game.core.world.faction.allied-with`, `game.core.world.faction.opposed-to` | The faction's closed data, explicit links, and bounded identity/motive context. Membership/control remain non-exclusive. |
| Knowledge detail | World-root ID; containment depth 0; relationship depth 2; 100 nodes/150 edges | `game.core.world.fact`, `game.core.world.rumour`, `game.core.world.secret`, `game.core.world.clue` | `game.core.world.knowledge.in-world`, `game.core.world.knowledge.about`, `game.core.world.clue.supports` | World-scoped records, target/support references, provenance and descriptive visibility. It is GM-only by caller contract, not filtered output. |

Before implementation, validate that the generic traversal semantics can produce each exact recipe
without hidden special cases. If the knowledge recipe would pull unrelated records through an
over-broad two-hop traversal, stop and refine the generic request language with an explicit
direction/kind step model; do not add a `game.core.world` branch in C#.

## Dependency order and slices

~~~text
World Feature 7: bounded trusted-GM read projections
├─ generic entity/component/containment/relationship stores          [implemented]
├─ existing query dispatcher/capability catalog                      [implemented]
├─ W1 topology and F3 faction/motive contracts                       [verified]
├─ W4 knowledge contract and F6 reactive state                       [must be verified/read back]
├─ F5 clock                                                          [sequence gate; not a recipe input]
├─ confirmed graph query public contract and caps                    [semantic/public boundary]
│  └─ Slice 1: generic graph materialiser and public query kind
└─ confirmed world read recipes                                      [parent: Slice 1]
   └─ Slice 2: world procedure, recipe coverage, consumer handoff   [implemented]

Player filtering, map geometry, routes, rendering, and storage [excluded]
~~~

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Generic bounded graph reader | Public request/response grammar, caps, error codes, and generic/no-game-vocabulary boundary are confirmed. | **Verified** — [Slice 1 receipt](WORLD-FEATURE-07-SLICE-1-RECEIPT.md): the graph query is advertised, callable, deterministic, bounded, and protocol-walk proven without any `game.core` C# branch. |
| 2 | World recipe publication | Slice 1 and the Feature 4 knowledge fixture/readback are verified; four recipe semantics are confirmed. | **Verified** — [Feature 7 receipt](WORLD-FEATURE-07-IMPLEMENTATION-RECEIPT.md): the governing procedure and recipe tests return bounded GM context and map-preparation topology without altering authoritative world state. |

Feature 5 remains a roadmap sequencing gate but does not appear in the initial recipes. Add clock
data only through a later revision if a consumer has a proven need; do not silently expand every
world read.

## Slice 1 — generic bounded graph reader

### Runtime artifacts

| Area | Change |
| --- | --- |
| Read abstraction | Add/extend a generic read-only projection service over existing entity, component, containment, and relationship persistence APIs. |
| MCP query dispatch | Add `graph` to `QueryTool` and `VerbSurface` with exactly the closed request shape above. Existing `world` and `entities` behavior stays unchanged. |
| Response serialization | Emit canonical node, component, containment, relationship, reference, and truncation records; no direct persistence entity leakage. |
| Tests | Add focused generic graph-reader tests plus query capability/dispatcher coverage, guard tests, and the MCP protocol walk. |

### Slice 1 acceptance matrix

| Case | Expected result |
| --- | --- |
| Valid small graph | Exact selected components and canonical nodes/edges are returned in stable order. |
| Containment and relationship cycles | Each node/edge appears once; traversal terminates within the requested depth. |
| Component filtering | Unselected components never appear; unknown/duplicate/empty selection fails before results. |
| Boundary limits | Exactly-at-cap response is complete; over-cap response explicitly reports truncation without non-deterministic ordering. |
| Bad request | Missing/extra/`null`/wrong-type/depth/limit/kind inputs fail with a stable recoverable error and no partial result. |
| Missing/deleted/corrupt reference | The response does not invent a node; stable failure distinguishes the invalid root or dangling stored edge. |
| Compatibility | Existing `world` and `entities` query responses and capability entries retain their current meaning. |
| Public surface | Capability catalog lists `graph` and its parameters; guard tests and protocol walk prove the dispatcher, serialization, and callable recovery path. |

## Slice 2 — world recipe publication

### Catalog and consumer artifacts

| Artifact | Change |
| --- | --- |
| Governing procedure | Add `procedure.game.core.world.read`. It owns the four exact recipe requests, trusted-GM audience disclaimer, stable ordering expectations, and map-preparation boundary. |
| World/lore documentation | Link this plan and correct any statement that the current location component already stores optional map metadata. It does not; map anchors are W9 work. |
| Focused tests | Add `CatalogWorldFeature7Tests` or the nearest world catalog/read test owner. Seed/import the fixture, run each recipe through the public graph path, and compare stable DTO shapes. |
| Website handoff | Provide read-only recipe examples and response schemas to the future website. No website code, asset, map widget, or player audience claim is included. |

### Slice 2 acceptance matrix

| Recipe / case | Exact expected result |
| --- | --- |
| World overview | The fixture root, its contained topology, and canonical adjacency are returned with no copied parent/child/coordinate fields. |
| Location detail | A fixture location has only direct child context, parent identity, and incident adjacency; unrelated locations are absent. |
| Faction detail | The fixture faction returns its closed agenda data and explicit membership/control/alliance/opposition context; it does not infer exclusivity or rewrite motives. |
| Knowledge detail | The fact, rumour, secret, and clue graph has in-world/about/support provenance and visibility labels; no output claims party-safe filtering. |
| Reactive readback | After the verified Feature 6 transition, the designated clue reads as `revealed/party` while the supported secret remains unchanged. |
| Map boundary | No response contains coordinates, path geometry, distance, terrain, route cost, or line-of-sight fields. |
| No-write guarantee | Every recipe leaves entities, components, containment, relationships, events, and notifications unchanged. Its ordinary `query` audit entry is the only durable record it creates. |
| Repository acceptance | Focused tests, `roleplay validate catalog`, guard tests, MCP protocol walk, full suite, and `git diff --check` pass. No persistent import occurs unless integration play or release needs it. |

## Completion boundary

Feature 7 is complete when a caller can discover and call the generic bounded graph query, the
trusted-GM recipes are recorded in the governing procedure, the Feature 7 fixture graph returns
the asserted stable shapes, and the public-surface verification passes.

Stop before adding player audiences, browser pages, map geometry, caching, routes, generic
unbounded graph search, or any new authoritative world state. Those would change either the
authorization, spatial, travel, or consumer ownership boundary and need their own plan.

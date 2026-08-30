# DND2024 scoped map views — future implementation plan

Status: **Slices 1, 2, 5 and 7 accepted 2026-08-28; Slices 3 and 6 blocked**

Slices 1, 2, 5 and 7 are implemented and accepted under their durable
[Slice 1](evidence/dnd2024/DND2024-SCOPED-MAP-VIEWS-SLICE-1-RECEIPT.md),
[Slice 2](evidence/dnd2024/DND2024-SCOPED-MAP-VIEWS-SLICE-2-RECEIPT.md),
[Slice 5](evidence/dnd2024/DND2024-SCOPED-MAP-VIEWS-SLICE-5-RECEIPT.md), and
[Slice 7](evidence/dnd2024/DND2024-SCOPED-MAP-VIEWS-SLICE-7-RECEIPT.md) receipts. Slice 4 has no separate prototype
deliverable: its city maps were authored in Slice 1, and its canonical hierarchy remainder is out of
scope for prototype work. Slices 3 and 6 are planned in the
[scoped map views dependency tree](DND2024-SCOPED-MAP-VIEWS-DEPENDENCY-TREE.md), which names the
owner, state, and gate blocking each one. No runtime artifact is authorized for Slices 3 and 6.

Owner/roadmap: `web/WEB-INTERFACE-ROADMAP.md`, Feature 5

Dependency tree: `DND2024-WEB-INFORMATION-HUB-DEPENDENCY-TREE.md`, World map branch C2 and future
Leaf 4A

Ruleset alignment: **dnd2024-compatible**

Source: **not applicable**. These maps present authored world information and do not calculate D&D
movement, travel, visibility, range, or encounter rules.

## Desired experience

Provide one map workspace that can move through an authored hierarchy without coupling maps to one
campaign:

~~~text
World map
└─ Region map
   └─ City or settlement map
      └─ Location reference view
~~~

Campaigns may reveal, annotate, or link to these maps through audience-safe knowledge, but the map
documents and durable geographic facts belong to the World. Starting a later campaign in the same
World reuses the same maps and observes committed World changes.

## Scope contracts

| Scope | Purpose | Typical features | Explicit non-goals |
| --- | --- | --- | --- |
| World | Orientation across the entire setting | Regions, major terrain, seas, capitals, major routes, known world landmarks | Tactical movement, exact travel time, labels for undiscovered places |
| Region | Understand one geographic area | Settlements, local terrain, roads, rivers, borders, ruins, regional landmarks | Inventing route connectivity from visual proximity |
| City | Navigate a settlement | Districts, gates, streets, public buildings, known points of interest | Building interiors, NPC simulation, live traffic |
| Location | Understand one scene-scale place | Rooms/areas, entrances, notable objects, authored points of interest, optional atmosphere art | Tactical grid, exact combat cover/range, automatically authoritative generated geometry |

Every map exposes breadcrumbs, current scope, parent/back navigation, known child scopes, a legend,
and one shared selection/detail pattern. Opening a marker changes information view only; it never
moves a character or records a visit.

## Proposed data boundary

No permanent IDs or schemas are confirmed by this document. A later implementation plan should
confirm generic equivalents of:

- **Map document:** world-owned identity, scope kind, parent entity, audience policy, revision,
  coordinate system, base asset, alt text, and provenance.
- **Map layer:** ordered background/terrain/labels/roads/markers/fog-or-knowledge layers, each with
  its own audience policy and safe Player variant.
- **Map feature:** point/line/polygon geometry linked to an existing World entity, never identified
  by filename or free-text matching.
- **Scope transition:** explicit parent/child or portal link connecting World → Region → City →
  Location. Visual nesting alone cannot create the relationship.
- **Map annotation:** campaign- or actor-scoped knowledge overlay that references, but never mutates,
  the World map document.

Each scope owns its own coordinate space. A city marker's coordinates on a regional map do not
become street coordinates, and a room position does not derive from world coordinates. Scale,
distance, adjacency, routes, and travel availability remain separate authored owners.

## Audience and secrecy

- The server selects the effective DM/Player audience before projecting maps.
- Player responses include only known map documents, safe base layers, authorized labels/features,
  and permitted child-scope links.
- A base image containing secret labels is unsafe even when secret markers are hidden. Player-safe
  raster/vector variants or independently projected label layers are required.
- Hidden feature names, counts, geometry, image URLs, alt text, metadata, and child-map existence
  must be absent from Player bytes.
- DM Player-preview must equal a real Player projection.

## Generated location views

Generation is optional and never required to browse the site. A generated location view should be
treated as a proposed visual asset until reviewed:

1. Build a closed generation brief from already authorized structured location facts.
2. Generate an unlabelled or safely labelled reference image; do not send DM secrets for a Player
   asset.
3. Store provenance: source location revision, audience variant, prompt/brief revision, generator
   and version, seed when available, generated timestamp, and content/licensing review.
4. Require DM approval before linking the asset to the World entity.
5. Mark it as illustrative unless authored geometry has been separately confirmed.
6. Preserve the prior approved asset for rollback and regenerate only through an explicit action.

Generated images must not invent exits, doors, treasure, creatures, hazards, distances, or tactical
properties that become game truth. Any such fact requires a separate authoritative World change.

## Ordered future slices

| Order | Slice | Prerequisites | Acceptance boundary |
| ---: | --- | --- | --- |
| 1 | Map scope contract and fixture hierarchy | Confirm map document/layer/feature/scope-link meanings | **Accepted 2026-08-28.** Componentized breadcrumbs and World → Region → City fixture navigation; no generation or live state. |
| 2 | Audience-safe map projection | Authenticated seat, knowledge owner, safe base-layer policy | **Accepted 2026-08-28.** Player bytes exclude hidden scopes/features/assets; DM preview equality proven. |
| 3 | Live World and Region maps | HTTPS bridge, World map owners, accepted anchors | World and Region documents read from authoritative state without copied campaign geography. |
| 4 | City maps | Confirmed city/district/place hierarchy and authored feature links | **Authored in Slice 1**; the canonical hierarchy remainder is out of scope for prototype work. City navigation resolves explicit districts and places; no proximity inference. |
| 5 | Authored Location reference views | Confirmed scene-scale document/feature contract | **Accepted 2026-08-28.** Location references show approved areas and points without tactical claims. |
| 6 | Optional generated Location assets | Approved media storage, provenance, review, and audience variants | Generation is explicit, reviewable, replaceable, and never authoritative by itself. |
| 7 | Campaign knowledge overlays | Campaign/actor knowledge projection and revision semantics | **Accepted 2026-08-28.** Campaign notes/reveals overlay World maps without changing World geography. |

## Failure and empty states

- A scope with no map shows its existing text/location detail and a clear “Map not available” state.
- A known child with no approved asset remains navigable as information, not a broken image.
- Missing/denied/stale layers fail closed and never fall back to a DM asset.
- Generation failure preserves the last approved asset and does not change World state.
- Unknown scope links, mismatched revisions, and geometry outside the declared coordinate space are
  rejected.

## Future acceptance evidence

Tests must cover scope normalization, breadcrumb order, parent/child resolution, audience-safe
projection, safe base variants, hidden child exclusion, generated-asset approval/provenance,
stale/denied/empty states, keyboard/touch navigation, responsive layouts, rollback, and proof that
map browsing performs no movement, visit, world, or campaign write.

## Stop point

Slices 3 and 6 record product and dependency intent only. They create no component, permanent ID,
schema, migration, map, image, generation request, model dependency, API, or deployment. The delivered boundaries of Slices 1, 2, 5 and 7 are recorded in
their receipts; none created a permanent ID, schema, migration, API, or generated asset.

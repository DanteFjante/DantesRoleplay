# DND2024 scoped map views Slice 1 receipt

Status: **accepted 2026-08-28**

Implementation document: `DND2024-SCOPED-MAP-VIEWS-SLICE-1-IMPLEMENTATION.md`

Plan: `DND2024-SCOPED-MAP-VIEWS-FUTURE-PLAN.md`, Slice 1

## Delivered boundary

- One authored map hierarchy in `hubSource.world.maps`: the Eldervale world map, three region maps,
  and three city maps, related **only** by declared `parentMapId` values and `scopeLinks`.
- Runtime meanings confirmed against fixtures for map document, map layer, map feature, and scope
  transition, plus `world.rootMapId` as the single declared entry point.
- Componentized workspace: `ScopedMapWorkspace`, `MapBreadcrumbs`, `MapCanvas`, `MapScopeLinks`, and
  `MapFeatureDetail`. `WorldMap` was superseded and removed.
- Pure helpers in `src/state.js`: `normalizeMapId`, `resolveMapDocument`, `resolveRootMapId`,
  `buildMapBreadcrumbs`, `resolveMapChildScopes`, `resolveSelectedMapFeature`,
  `isGeometryInCoordinateSpace`, and `isValidMapHierarchy`, which the closed envelope validation now
  requires.
- Every scope owns its own coordinate space (world `100x100` percent; regions `1000x700`, `900x900`,
  `800x600`; cities `800x800`). No code path converts a value between two spaces. Feature placement
  is expressed as a percentage of the declaring map's own space at render time only.
- World-scope placement is derived from the already projected locations and their accepted display
  anchors. No second copy of world geography was created.
- Authored label-free SVG bases for the region and city scopes under `public/`.

## Evidence

| Command | Result |
| --- | --- |
| `node --test test/web-prototype-state.test.js test/web-audience-envelope.test.js` | 36 passed, 0 failed |
| `node --test` (all 40 files, run in batches) | 1 pre-existing failure, unrelated (below) |
| `npx tsc --noEmit` | 1 pre-existing error, unrelated (below) |
| `git diff --check` on `prototype/dnd2024` | clean |

Eleven new tests cover: root/unknown map normalization, cleared selection on an unknown feature,
breadcrumb ordering root to current, refusal of an orphaned or cyclic ancestry, child scopes coming
only from declared links, dangling-link rejection, boundary and out-of-space geometry (rejected,
never clamped), hierarchy rejection for a non-world root, duplicate ids, dangling links and unknown
`viaFeatureId`, declared-parent reachability for every projected map, distinct coordinate spaces,
world placement derived from projected locations, an approved-base-absent scope staying navigable,
Player exclusion of the hidden city scope and its link and features, canary absence in Player bytes,
determinism, and non-mutation of the authored source.

### Privacy evidence

The hidden Blackglass Cove city map, its scope link, its base URL, its alt text, and its features are
absent from the Player projection: `player.world.maps.length` is 6 against the DM's 7, the Crown
Coast scope-link count is 0 against the DM's 1, and no `HIDDEN_MAP_CANARIES` or
`HIDDEN_MAP_FEATURE_CANARIES` string appears in the serialized Player envelope. The existing DM
Player-preview equality assertion now also covers maps, since it deep-equals the whole projected
world.

### Unrelated exceptions

- `test/record-inventory.test.js` fails on `vocabulary.ability.strength cites missing
  catalog/applications/dnd2024/components/abilities/dnd2024.abilities.schema.json`. That schema is
  deleted in the working tree by unrelated in-flight catalog work; the failure predates this slice
  and no map artifact touches `catalog/`.
- `npx tsc --noEmit` reports `src/server/runtime-environment.ts(13,27) TS2339: Property 'env' does
  not exist on type 'ImportMeta'`. Pre-existing; that file was not touched.
- Concurrent Campaign work (quests, open threads, clues) was editing `src/state.js`,
  `src/server/hub-source.js`, `src/server/hub-envelope.js`, and `DndInformationHub.tsx` during this
  pass. All edits here were read-modify-write and nothing was clobbered in either direction.

## Deliberate exclusions

Live World/Region state reads, layer-level audience policy and Player-safe base variants, confirmed
city/district/place ownership, scene-scale Location reference views, generated imagery and media
provenance, campaign knowledge overlays, fog, routes, adjacency, reachability, distance, travel,
tactical grids, movement, and visit recording. No catalog, SQLite, schema, migration, permanent ID,
public API, MCP surface, or model dependency was added, and `npm run build` and deployment remain
owned by the slice that first serves this workspace publicly.

## Status effect

`DND2024-SCOPED-MAP-VIEWS-FUTURE-PLAN.md` Slice 1 is accepted. Dependency-tree Leaf 4A stays
`planned`: its live-data prerequisites are untouched, and this slice is fixture presentation evidence
only.

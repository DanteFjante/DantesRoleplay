# DND2024 scoped map views Slice 5 receipt

Status: **accepted 2026-08-28**

Implementation document: `DND2024-SCOPED-MAP-VIEWS-SLICE-5-IMPLEMENTATION.md`

Dependency tree: `DND2024-SCOPED-MAP-VIEWS-DEPENDENCY-TREE.md`, Slice 5

## Delivered boundary

- **The fourth scope now has a runtime user.** `location` was a declared scope kind with nothing
  behind it; two authored reference views now use it, completing World → Region → City → Location.
- `map.fixture.location.sunken-archive` hangs off the **region** map through the existing
  `feature.fixture.ash-march.sunken-archive`, giving a three-level trail. Its subject is the real
  `sunken-archive` World location, so the same place is both a navigable scope and an openable
  location detail.
- `map.fixture.location.tollhouse` hangs off the **city** map through the existing
  `feature.fixture.greyfen.tollhouse-quay`, giving the four-level trail
  Eldervale → The Ash March → Greyfen Crossing → The Tollhouse. Its subject is a presentation-local
  fixture place with no World location behind it, exactly as Slice 1 recorded for city features.
- Scene features are **named points only**. No feature carries width, height, radius, extent, or grid
  data, and a test pins the exact key set so extent geometry cannot arrive unnoticed.
- Slice 2's audience machinery is exercised at the new depth: a DM-only "Ward notes" layer, a DM-only
  sealed-brass-door feature, and a DM base variant that marks the ward, beside a Player base that
  does not.
- `MapScopeLinks` and `MapCanvas` now pick an icon per scope kind instead of showing a city icon for
  every child.

The projector, the pure helpers, and the workspace components needed **no** change: adding a scope
required authoring documents, which is the outcome Slice 1's scope-agnostic design was aiming at.

## Evidence

| Command | Result |
| --- | --- |
| `node --test test/web-prototype-state.test.js test/web-audience-envelope.test.js` | 45 passed, 0 failed |
| `npx tsc --noEmit` | 1 pre-existing error, unrelated (below) |
| `git diff --check` on `prototype/dnd2024` | clean |

Four new tests cover: both parent paths with their exact breadcrumb trails and the declared links
that carry them; scope-and-place independence in all three combinations (both, place only, scope
only); point-only geometry with a pinned key set; and audience exclusion of the DM layer, DM feature,
and DM base four levels deep. Two Slice 1 and Slice 2 assertions that pinned exact map counts and the
scope list were updated for the two new documents.

### Privacy evidence

The Player projection contains 8 maps against the DM's 9, and none of `/location-map-sunken-archive-dm.svg`,
`The sealed brass door`, or `Ward notes` appears in its serialized bytes. The Player archive base is
the label-free `/location-map-sunken-archive.svg`.

### Unrelated exceptions

- `npx tsc --noEmit` still reports `src/server/runtime-environment.ts(13,27) TS2339`. Pre-existing;
  that file was not touched.
- `test/record-inventory.test.js` remains failing on a catalog schema deleted by unrelated in-flight
  work, as recorded in the Slice 1 and Slice 2 receipts. No map artifact touches `catalog/`.

## Scope note on Slice 4

Slice 4 (city maps) has no separate prototype deliverable: city maps were authored in Slice 1, and
what Slice 4 additionally wanted was a **confirmed canonical** city/district/place hierarchy. Under
the owner's decision that implementation happens in the prototype rather than by changing the
canonical model, that remainder is out of scope here. The prototype's authored fixture hierarchy is
the hierarchy this workspace uses.

## Deliberate exclusions

Area, polygon, or extent geometry; tactical grids, cover, range, distance, and line of sight;
generated imagery and media provenance (Slice 6); campaign knowledge overlays (Slice 7); and live
state (Slice 3). No catalog, SQLite, schema, migration, permanent ID, public API, MCP surface, or
model dependency was added.

## Status effect

Slice 5 is accepted. Slice 6 (optional generated Location assets) now has its prerequisite scope and
remains blocked only on approved media storage, provenance, and review. Slice 7 (campaign knowledge
overlays) has both its prerequisites met in the prototype and is the next implementable leaf. Slice 3
stays blocked on live state.

# DND2024 scoped map views Slice 7 receipt

Status: **accepted 2026-08-28**

Implementation document: `DND2024-SCOPED-MAP-VIEWS-SLICE-7-IMPLEMENTATION.md`

Dependency tree: `DND2024-SCOPED-MAP-VIEWS-DEPENDENCY-TREE.md`, Slice 7

## Delivered boundary

- **Campaign annotations that own no geography.** `campaign.mapOverlays` carries `id`, `mapId`,
  `featureId` (nullable), `kind` (`note` or `reveal`), `label`, `detail`, and `recordedOn` — and
  nothing else. An overlay points at a World feature; its screen position *is* that feature's
  position, so a campaign cannot move a marker or invent one.
- **Ownership is visible in the envelope shape.** Overlays sit under `campaign`, never under `world`.
  Deleting every overlay from the source leaves `world.maps` byte-identical, which is asserted
  directly rather than argued.
- **Exclusion is derived from the target, not only from the overlay.** `projectCampaignOverlays` runs
  *after* map projection and keeps an overlay only when the audience may see the overlay itself, its
  map is projected, and its named feature is projected. All three paths are exercised by fixtures.
- **Dropping is silent.** No count, placeholder, or "annotation hidden" affordance survives for a
  removed overlay — that would leak exactly what the target's audience policy protects.
- `MapOverlayNotes` lists the active map's overlays under a campaign-labelled heading with an
  explicit boundary statement; `MapFeatureDetail` shows the selected feature's overlays; annotated
  markers carry `data-annotated` and say so in their accessible label.
- Closed client validation now requires every projected overlay to resolve to a projected map and,
  when named, a projected feature — and rejects any overlay carrying `geometry`, `coordinateSpaceId`,
  `layerId`, or `base`. A campaign that tries to place something is invalid, not merely ignored.

## Evidence

| Command | Result |
| --- | --- |
| `node --test test/web-prototype-state.test.js test/web-audience-envelope.test.js` | 50 passed, 0 failed |
| `npx tsc --noEmit` | 1 pre-existing error, unrelated (below) |
| `git diff --check` on `prototype/dnd2024` | clean |

Five new tests cover: byte-identical World maps with and without overlays plus a pinned overlay key
set; all three exclusion paths with canary absence; every surviving overlay still resolving to
something the audience can see; deterministic ordering and DM Player-preview equality; helper
selection by map and by feature including the map-level `null` case; and validation rejection of an
unresolvable target, an unknown kind, and each of the four placement fields.

### Privacy evidence

DM receives 7 overlays, Player 4. Dropped for Player: `vault-approach` (DM-only overlay),
`cove-invitation` (its map is hidden), and `archive-brass-door` — which is itself Player-visible and
is removed solely because its target feature sits on a DM-only layer. No `HIDDEN_OVERLAY_CANARIES` or
`UNREACHABLE_OVERLAY_CANARIES` string appears in the serialized Player envelope.

### Unrelated exceptions

- `npx tsc --noEmit` still reports `src/server/runtime-environment.ts(13,27) TS2339`. Pre-existing.
- `test/record-inventory.test.js` remains failing on a catalog schema deleted by unrelated in-flight
  work, as recorded in earlier receipts. No map artifact touches `catalog/`.
- Concurrent Campaign work (quests, threads, clues) had extended the campaign projection and its
  closed validation since Slice 2; this slice merged with it rather than around it.

## Deliberate exclusions

Campaign-authored geometry, placement, scope links, layers, or bases; World writes of any kind; live
campaign or knowledge authority; visit inference; generated imagery. No catalog, SQLite, schema,
migration, permanent ID, public API, MCP surface, or model dependency was added.

## Status effect

Slice 7 is accepted, and with it the plan's central claim is demonstrated: a campaign annotates World
maps without owning, moving, or changing their geography. The remaining slices are **3** (live World
and Region maps) and **6** (optional generated Location assets), both blocked — 3 on live state and
the placement gap recorded on hold in `world/feature-18/`, and 6 on approved media storage,
provenance, and review. Neither is startable as prototype work today.

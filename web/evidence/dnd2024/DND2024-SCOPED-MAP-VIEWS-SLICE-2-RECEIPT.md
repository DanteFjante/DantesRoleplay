# DND2024 scoped map views Slice 2 receipt

Status: **accepted 2026-08-28**

Implementation document: `DND2024-SCOPED-MAP-VIEWS-SLICE-2-IMPLEMENTATION.md`

Dependency tree: `DND2024-SCOPED-MAP-VIEWS-DEPENDENCY-TREE.md`, Slice 2

## Delivered boundary

- **Per-layer audience policy.** Every authored `MapLayer` declares `audience: "player" | "dm"`.
  `projectMapLayers` emits only the layers the effective audience may read; a layer whose audience is
  missing or unrecognized is omitted rather than defaulted to either side.
- **Features cannot outlive their layer.** A feature is dropped when its `layerId` names a layer the
  audience did not receive, so removing a layer can never leave an orphan marker behind. The closed
  client validation now enforces the same rule.
- **Per-audience base variants.** `MapDocument.base` is authored as
  `variants: [{ audience, imageUrl, alt }]`. `projectMapBase` resolves the variant for the effective
  audience and emits the existing flat `{ imageUrl, alt }` or `null`, so the projected client
  contract, the Slice 1 components, and the Slice 1 helpers are unchanged.
- **Fail closed, quietly.** A scope with no variant for the effective audience emits `base: null` and
  reuses Slice 1's "Map not available" state. No fallback to the other audience's asset, no error,
  and no hint that another audience has one.
- Fixtures now exercise all three paths: the Ash March region carries a label-free Player base beside
  a DM base that labels the sealed Cinder Vault; the Emberwatch city map carries a DM-only base and
  no Player variant; and the Greyfen Crossing city map carries a DM-only "Night-watch notes" layer
  whose feature is otherwise Player-visible, isolating layer policy from `playerKnown`.
- New canaries `DM_ONLY_BASE_CANARIES` and `DM_ONLY_LAYER_CANARIES`; `HIDDEN_MAP_CANARIES` now reads
  every variant.

## Evidence

| Command | Result |
| --- | --- |
| `node --test test/web-prototype-state.test.js test/web-audience-envelope.test.js` | 41 passed, 0 failed |
| `node --test test/record-inventory.test.js` | 3 passed, 1 failed — unchanged pre-existing failure (below) |
| `npx tsc --noEmit` | 1 pre-existing error, unrelated (below) |
| `git diff --check` on `prototype/dnd2024` | clean |

Six new tests cover: layer-policy exclusion of an otherwise Player-visible feature, absence of
DM layer ids and labels from Player bytes, per-audience base variant selection, absence of DM base
URLs and alt text from Player bytes, the fail-closed missing-variant path with the scope still
navigable, omission of layers with unknown or missing audience policy together with their features,
and byte-equality of a DM Player-preview with a real Player projection across every map.

The remaining 38 test files are untouched by this slice; they were run in full during the Slice 1
pass. The only tests exercising the changed modules are the two focused files above.

### Privacy evidence

No `DM_ONLY_BASE_CANARIES` or `DM_ONLY_LAYER_CANARIES` string appears in the serialized Player
envelope, and neither does the DM-only feature's name. Player receives `/region-map-ash-march.svg`
where DM receives `/region-map-ash-march-dm.svg`; Player receives `base: null` for Emberwatch where
DM receives `/city-map-emberwatch-dm.svg`. `JSON.stringify(preview.world.maps)` equals
`JSON.stringify(player.world.maps)`.

### Unrelated exceptions

- `test/record-inventory.test.js` still fails on `vocabulary.ability.strength cites missing
  catalog/applications/dnd2024/components/abilities/dnd2024.abilities.schema.json`, from unrelated
  in-flight catalog work. Unchanged from the Slice 1 receipt; no map artifact touches `catalog/`.
- `npx tsc --noEmit` still reports `src/server/runtime-environment.ts(13,27) TS2339`. Pre-existing;
  that file was not touched.

## Deliberate exclusions

Live World/Region state reads, permanent IDs and schemas, Region as an addressable entity, city and
district ownership, scene-scale Location views, generated imagery and media provenance, and campaign
knowledge overlays. No catalog, SQLite, migration, public API, MCP surface, or model dependency was
added, and no new client capability or caller input was introduced.

## Status effect

Slice 2 is accepted. Slice 3 remains `blocked`: it needs parent tree Leaves 3 and 4, Region as an
addressable entity, and confirmed permanent map IDs and schemas — none of which this slice touched.

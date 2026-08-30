# DND2024 atlas Slice 13 receipt — accessible illustrated and list modes

Status: **accepted 2026-08-30**

## Delivered boundary

Every scoped atlas view now offers an accessible Illustrated/List switch. The semantic list consumes
the exact same layer-filtered active `MapDocument` as the illustrated canvas, groups nonempty
features in declared layer order, and preserves selection, current-place, campaign-note,
faction-presence, and explicit closer-map affordances. Turning off a layer removes its features from
both modes. An all-hidden scope shows a clear empty state without leaking hidden names or counts.
The mode is ephemeral UI state and performs no request or state write.

## Evidence

- Focused grouping/map state tests: 24 passed, 0 failed.
- Full prototype suite: 160 passed, 0 failed.
- Production prototype build: passed.
- Prototype root responded 200; after the host's expected request-limit cooldown,
  `http://localhost:6217/ui/dnd2024-play` responded 200 and still targets the live prototype.

## Deliberate exclusions

No map/entity data, geography, coordinate, route, discovery/reveal, faction relationship,
World/Campaign write, schema, migration, or D&D mechanic changed. Canonical live map documents and
reviewed generated imagery remain separately blocked work.

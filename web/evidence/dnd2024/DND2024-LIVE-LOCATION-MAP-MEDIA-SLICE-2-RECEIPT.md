# DND2024 live location-owned map media — Slice 2 receipt

Status: **accepted 2026-08-30**
Ruleset alignment: ruleset-neutral presentation state

## Delivered boundary

- Created live `location.thalorien.thalos` below `world.thalorien`.
- Moved the nine existing regional locations beneath Thalos without changing their IDs.
- Attached active exact Player/DM map visuals to Thalos, nine regional locations, Crownmere, and
  Merrowgate.
- Migrated the existing illustrated positions to normalized live anchors for the nine Thalos
  regions and fifteen direct regional children.
- Used only dry-run-first `system.world-state.sync` transactions; no direct SQL or catalog-to-live
  import was used.

## Evidence

- Hierarchy/media manifest dry run replayed successfully and commit applied 33 effects under
  operation `9f9d25b0653637d1647b2676d0fdd6fb`.
- Regional-anchor dry run applied 15 staged effects under
  `dd03fa4f52e08b823906f15c26966a45`; the identical commit applied 15 effects under
  `680bd45ff8f8c41476f515427c72122a`.
- Public application-state readback found 12 active visual components, 24 anchor components, and
  nine region children beneath Thalos. Representative readback matched Thalos, Aldros, Crownmere,
  and Brackenford values in the reviewed manifests.

## Deliberate exclusions

No web projection, new asset bytes, authoring UI, map geometry, travel/discovery behavior,
campaign state, or D&D mechanic was added in this slice.


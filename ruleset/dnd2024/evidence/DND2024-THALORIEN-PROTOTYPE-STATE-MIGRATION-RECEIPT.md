# D&D 2024 Thalorien prototype-state migration receipt

Status: complete
Date: 2026-08-29

## Delivered boundary

All active Thalorien records were copied into the new application-owned ECS state space
`dnd2024-thalorien-migrated`. The existing campaign ID `campaign.thalorien.brackenford` and world ID
`world.thalorien` were retained; no Embervale or replacement campaign was created.

Inventory from the exact dry-run and commit:

- 199 entities
- 366 components
- 26 containments
- 357 relationships

Legacy generic campaign/world records were mapped directly to D&D campaign/world component types
(`dnd2024.campaign.*` and `dnd2024.world.*`). Relationship kinds were mapped to the corresponding
`dnd2024.*` relationship namespace. No compatibility namespace was added.

Orban's original `dnd2024.playtest-character-record` narrative ledger was preserved. The separate
provisional character review ledger remains the source for later replacement of invented mechanical
facts; the ocarina remains narrative-only.

## Evidence and verification

- Pre-migration export: `old-dnd/prototype-cutover-archive/2026-08-29-thalorien-live-pre-migration-export`
- Application activation fingerprint: `62FD793E8E61E8ED4C2D80C11EDB8ADADC65AFE776A61D10C10D27205CBBCB90`
- Adoption source fingerprint: `2907D2FBECB1BC0D0AD1E5D54ACC67F07C3B275497F2B8A0393E672951D3B839`
- Adoption evidence fingerprint: `2B65190FC99CE707FE24277B883F85DA736C1F005204F67D245D6E679CAB10F2`
- Catalog validation: passed; 21 existing warnings, no errors.
- Scoped adoption regression tests: 7 passed.
- System catalog protocol tests: 2 passed.
- Post-commit read-back: 199 entities and Orban's migrated narrative component present in the new state space.

Deliberate exclusions: no deletion or overwrite of the classic graph, no secret disclosure, and no
new D&D rule behavior.

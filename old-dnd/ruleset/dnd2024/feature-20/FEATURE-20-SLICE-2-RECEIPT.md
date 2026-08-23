# Feature 20 Slice 2 receipt — tactical placement admission

Status: **Verified**
Date: 2026-08-21

## Delivered boundary

Feature 20 now provides a bounded five-foot encounter grid, closed sparse terrain, Size-derived
2.5-foot placement anchors, and effect-free base melee-reach evidence. It does not move a creature,
spend movement, authorize an attack, resolve cover/sight, or create an event.

The placement repair uses the existing declared child fan-out: one
`mechanic.dnd2024.encounter-participant-tactical-state.read` result is collected for every
encounter-contained participant. The parent requires a complete, one-to-one snapshot set and
rejects malformed, missing, foreign, or duplicate reports before it can propose an effect. It then
checks the Size-derived footprint against map bounds, blocked squares, and other valid
same-encounter occupied footprints.

## Catalog artifacts

- `dnd2024.encounter-space`, `dnd2024.encounter-position`, and `dnd2024.melee-reach`
  components.
- Encounter map record/correct and diagnostic-read mechanics.
- Placement record/correct mechanics with child roster snapshots.
- Base reach record/correct and effect-free check mechanics.
- Governing procedure:
  `procedure.mechanic.dnd2024.encounter-space`.

## Evidence

`CatalogFeature20Tests` passed 5/5 focused tests. They cover:

- medium-creature adjacency, collision rejection with byte-identical stored placement, and
  out-of-bounds rejection;
- all Tiny through Gargantuan footprint widths (1/2/2/4/6/8 half-square units) at the exact map
  edge and one-unit-over rejection;
- blocked terrain rejection, difficult-terrain admission without a movement cost, and invalid
  roster Size rejection without mutating a placement;
- map diagnostics, exact five-foot in-reach and ten-foot out-of-reach evidence, zero effects, and
  deterministic replay.

`roleplay validate catalog` passed in a disposable database: **365 records**, including
**83 mechanics**, **101 procedures**, **75 components**, **12 event types**, **2 subscriptions**,
and **92 entities**. It reported 60 existing catalog warnings and did not touch live data.
`git diff --check` passed.

## Next boundary

Slice 3 may add only the tactical melee precondition parent around Feature 8. Voluntary path
movement remains blocked until the platform provides a reviewed derived-cost-to-budget-spender
composition path.

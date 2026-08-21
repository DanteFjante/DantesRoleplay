# World Feature 15 Slice 1 receipt — fixed portal foundation

**Status:** Slice 1 verified; Feature 15 awaits Slice 2 and feature acceptance  
**Date:** 2026-08-20  
**Roadmap:** [World and lore](../../WORLD_AND_LORE_PLAN.md)

## Delivered

- Closed fixed-portal component state.
- One active gate/`presence` portal with explicit world and observatory destination links.
- No route, adjacency, clock, item, spell, or traveller-state change.

## Verification

- Focused Feature 15 coverage: **3 passed, 0 failed**.
- `roleplay validate catalog`: **156 records valid** with 24 advisory near-duplicate warnings and
  no catalog errors. The validator touched no live data.

## Remaining

Slice 2 adds the single-effect teleport action, moving only a co-located eligible traveller while
leaving the root clock byte-identical.

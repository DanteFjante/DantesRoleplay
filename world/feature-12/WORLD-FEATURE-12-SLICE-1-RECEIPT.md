# World Feature 12 Slice 1 receipt — generic ground conveyance foundation

**Status:** Slice 1 verified; Feature 12 awaits Slice 2 and feature acceptance  
**Date:** 2026-08-20  
**Roadmap:** [World and lore](../../WORLD_AND_LORE_PLAN.md)

## Delivered

- Closed generic ground-conveyance and ground-route components.
- A horse-cart fixture at gate/`presence`, speed 15 units/minute.
- A separate 300-unit gate→market ground route. It does not alter Feature 8's on-foot route or
  availability.

## Verification

- Focused Feature 12 coverage: **3 passed, 0 failed**.
- `roleplay validate catalog`: **148 records valid** with 22 advisory near-duplicate warnings and
  no catalog errors. The validator touched no live data.

## Remaining

Slice 2 adds the exact integer ceiling-division journey action, moving cart, driver, and root clock
atomically; no other vehicle modes or simulation is in scope.

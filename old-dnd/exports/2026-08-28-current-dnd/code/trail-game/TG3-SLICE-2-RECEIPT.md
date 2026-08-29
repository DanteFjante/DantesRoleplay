# Trail Game TG3 Slice 2 receipt — trade, policy, rest, and forage

Status: **accepted through scoped equivalent automated invariant evidence**
Completed: **2026-08-25**
Implementation: [TG3 Slice 2](TG3-SLICE-2-IMPLEMENTATION.md)

## Delivered boundary

- Added exact trade, policy, rest, and forage JavaScript mechanics over one declared run-root and
  bounded party projection.
- Derived prices, affordability, stock, cargo weight, policy eligibility, food, healing, elapsed
  time, forage yield, turn, and seed cursor entirely from pinned scenario plus canonical state.
- Proved the activated runner sequence and wrong-seed no-change behavior; exact earlier operation
  replay remains idempotent after later commands.

## Evidence and exclusions

- Focused TG3 activated headless suite: **3 passed, 0 failed**.
- Current isolated test build: **0 warnings, 0 errors**.
- Travel/events/terminal acceptance, full suite, authored fixture, public transport, migration, and
  generic C# game rules remain excluded.

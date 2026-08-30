# D&D 2024 complete-campaign G6 clock bridge receipt

Status: **accepted**
Date: 2026-08-30

## Delivered boundary

The D&D application can now resolve an installed identity such as
`dnd2024.game.core.world.clock` to the exact registered `game.core.world.clock` component when—and
only when—`game` is a declared base application of the exact state-space revision. The
application-local installed key remains visible to mechanics while persistence retains the base
owner, exact component version, schema hash, and persisted revision.

`dnd2024.mechanic.world.clock.advance` and `dnd2024.procedure.world.clock` provide the D&D action
surface. The action accepts only 1–1,440 minutes, preserves calendar identity, advances the minute
monotonically, increments the embedded clock revision once, uses the projected persisted component
revision, and commits through the existing replay-safe typed-effect transaction.

No campaign clock, wall-clock scheduler, automatic passage of time, migration, activation, or live
database write was added.

## Evidence

- [Implementation contract](../../DND2024-G6-AUTHORITATIVE-CLOCK-BRIDGE-IMPLEMENTATION.md)
  records ownership, installed-base mapping, clock behavior, failure behavior, and exclusions.
- `ApplicationClockBridgeTests.Installed_base_clock_advances_once_and_rejects_caller_derived_state`
  proves exact base-owner resolution, one committed advance, replay without a second advance,
  persisted/embedded revision movement, and caller-state rejection.
- `Dnd2024ClockBridgeTests` proves D&D mechanic behavior for an accepted advance and rejects zero,
  caller-derived target state, minute overflow, and revision overflow without effects.
- Application component-store acceptance continues to prove exact persisted-revision rejection;
  the clock uses that same typed component-set transaction.
- The combined bridge, D&D mechanic, execution, and sandbox filter passed 39 of 39 tests.
- The combined namespace-containment, application-scoped ECS, and clock filters passed 16 of 16
  tests.
- `roleplay validate catalog` passed 154 generic catalog records with 26 existing/expected
  near-duplicate warnings and no live-data access. D&D namespace containment also passed for the
  newly authored application mechanic and procedure.

## Deliberate exclusions and next gate

This slice does not implement reaction windows, expiry, rest completion, downtime, session closure,
or campaign-record capture. Those consumers can now share one authoritative clock coordinate and
revision. G8/G9 remain the next prerequisites before trusted campaign authoring is accepted.

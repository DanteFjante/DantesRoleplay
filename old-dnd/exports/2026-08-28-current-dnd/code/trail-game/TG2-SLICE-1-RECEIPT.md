# Trail Game TG2 Slice 1 receipt — run spine schemas

Status: **accepted through equivalent automated invariant evidence**
Completed: **2026-08-25**
Implementation: [TG2 Slice 1](TG2-SLICE-1-IMPLEMENTATION.md)
Parent: [TG2 run domain](TG2-RUN-DOMAIN-DEPENDENCY-PLAN.md)

## Delivered boundary

- Added the governing `procedure.trail-survival.run-state` procedure.
- Added metadata/schema pairs for `trail-survival.scenario-pin`, `trail-survival.run`,
  `trail-survival.clock`, and `trail-survival.route-progress`.
- Proved each pair parses, compiles under the bounded JSON Schema profile, accepts a representative
  valid value, and rejects missing, extra, wrong-type, invalid-enum, and boundary violations as
  applicable.
- Proved exact Trail ownership, derived version/hash, identical version-1 replay, and empty
  `dnd2024` component discovery in a disposable database.
- Made TG1 source/catalog assertions additive so new component files and the governing procedure do
  not invalidate the accepted application seam.

## Evidence

- Focused TG1/TG2 suite: **4 passed, 0 failed** using isolated build output because the running
  private host held its normal output assemblies open.
- Disposable catalog validation: **144 records valid**, 21 existing advisory warnings, no errors,
  and no live data touched. Application-owned schema parsing is separately asserted by the focused
  test because the legacy validator does not scan application package components.

## Deliberate exclusions

No party/inventory/decision/outcome schema, fixture, mechanic, action, calculation, transition,
migration, public surface, startup registration, or live state was added. TG2 Slice 2 owns the four
party/inventory contracts.


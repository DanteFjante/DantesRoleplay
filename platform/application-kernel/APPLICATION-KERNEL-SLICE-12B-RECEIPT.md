# Application Kernel Slice 12B receipt — bounded exact ancestry checks

Status: **accepted** · 2026-08-30

## Delivered boundary

- Replaced World author's complete ancestor-roster snapshots with bounded exact
  child-parent-slot-revision expectations for only the containment paths used to prove scope.
- Added host-only exact containment-edge validation and in-transaction verification while retaining
  complete-roster expectations for existing callers.
- Preserved the public `system.world-state.sync` request, root-membership rules, effect limits,
  replay behavior, and atomic rollback.

## Evidence

- Focused application-ECS applier and World synchronizer suite: 26/26 passed.
- Added regression proof that a root with 101 unrelated direct children accepts a small reviewed
  manifest, plus malformed-expectation and stale-edge rollback tests.
- Disposable catalog validation: 355 records valid, zero warnings; no live data touched.
- Full repository suite was attempted. Existing unrelated D&D fixture gaps, weapon-damage contract
  drift, and a catalog fixture assertion still fail; the focused owner tests compile and pass.
- `git diff --check` reported no whitespace errors (only the checkout's line-ending notices).

## Live acceptance

- The exact Ganji manifest passed dry run: 8 entities, 41 effects, effect operation
  `2a606ff22f60748bf92fb8d638fe4a67`.
- The byte-identical manifest committed atomically: effect operation
  `6f4cb51f8c6cc527a720070d0fb3c2fb`, outer operation
  `c8c050f65a8f4aa2bae123e2b10a0b0e`.
- Live application readback verified 8/8 entity revisions, 8/8 component counts, 8/8
  containments, and 12/12 relationships.

## Deliberate exclusions

No playable actor, campaign participation, D&D character choices, database migration, public MCP
shape, ruleset logic, deletion/removal support, or unrelated live World record changed.

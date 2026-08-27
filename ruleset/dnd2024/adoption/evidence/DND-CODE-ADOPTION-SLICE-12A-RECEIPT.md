# D&D code-adoption Slice 12A receipt — fresh-host play, replay, and rollback

Date: 2026-08-27  
Status: **accepted**

## Accepted boundary

- A single fresh SQLite host previews and activates the core-only D&D application, with the
  optional legacy-equipment extension demonstrably absent.
- The activated action surface orders and starts an encounter, grants Temporary HP, applies weapon
  damage through Temporary HP and Hit Points, heals, and reads back authoritative state.
- Reusing the committed damage operation returns `Replayed` and preserves the Hit Point revision
  and value.
- Corrupt authoritative Temporary HP state rejects weapon damage before any Hit Point mutation.
- The existing Slice 6C injected-failure proof remains the generic all-or-nothing rollback owner.
- No runtime/catalog artifact, public operation, schema, migration, or live database changed.

## Verification

- Focused fresh-host acceptance: **1 passed, 0 failed**.
- Existing impact/replay/rollback proof: schema and pinned evidence checks passed; generic focused
  transaction tests passed; the proof reports no writes.
- Complete `Dnd2024AbilityCheckTests`: **92 passed, 0 failed**.

## Deliberate exclusions

This leaf does not run donor software, contact upstream repositories, update attribution, duplicate
the generic transaction tests, or claim full-suite/protocol acceptance. Those are Slice 12B–12C.

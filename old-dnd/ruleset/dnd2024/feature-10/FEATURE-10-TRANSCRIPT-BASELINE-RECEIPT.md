# Feature 10 transcript-baseline receipt

Status: **verified**  
Date: 2026-08-21

## Delivered boundary

`CatalogFeature10Tests` now treats the accepted
`dnd2024.encounter-sides` component as immutable training-fixture state. The deterministic
vertical-session assertion proves that the component is present before the session, remains
byte-for-byte unchanged afterward, and is not confused with the one runtime initiative-order
delta.

No game rule, fixture data, catalog record, MCP surface, persistence schema, or production C#
behavior changed.

## Evidence

- Focused `CatalogFeature10Tests`: **2 passed, 0 failed**.
- `roleplay validate catalog`: **406 records valid**; 73 existing advisory overlap warnings; no
  live database touched.
- Isolated solution build: **0 warnings, 0 errors**.
- Settled-catalog full suite: **790 passed, 0 failed, 0 skipped**.

The first full-suite attempt overlapped a concurrent addition of the armor-training catalog
directory while individual tests were copying the catalog into disposable folders. Five copies
therefore failed before execution. A rerun after the catalog settled passed in full; no test helper
or game behavior was changed for that transient race.

## Next boundary

The repository acceptance baseline is green. The next playable-game evidence is the existing
fresh-host story-session proof described in `STORY_FIRST_ROADMAP.md`; it must be promoted only
through its own bounded integration receipt and without reopening this completed D&D acceptance
feature.

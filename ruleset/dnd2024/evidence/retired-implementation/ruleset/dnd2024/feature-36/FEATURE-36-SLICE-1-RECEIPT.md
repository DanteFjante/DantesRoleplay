# Feature 36 — Slice 1 receipt

Date: 2026-08-21

## Delivered boundary

- Added the closed `dnd2024.character-experience` component: a nonnegative JavaScript-safe XP
  total plus fixed SRD 5.2.1 `Character Creation > Level Advancement` provenance.
- Added the governing procedure and administrative `record`/`correct` mechanic. It accepts only a
  complete XP total and writes exactly that component.
- Added a zero-effect reader that combines valid XP with `dnd2024.character-level` and derives
  only the exact next-level threshold, `below-next-threshold`,
  `eligible-for-next-level`, `at-level-cap`, or `unknown` diagnostics.
- Added focused execution tests for closed writes, all specified level 1/4/5/19 threshold
  boundaries, level-20 cap, invalid/missing diagnostics, and state preservation.

## Explicitly not delivered

No campaign award, campaign policy, advancement authorization, class level, Hit Points, feature
grant, or total-character-level change. Those remain owned by Campaign C14, Feature 27, and
Character CH9.

## Evidence

- `dotnet test DantesRoleplay.Tests\\DantesRoleplay.Tests.csproj --filter
  "FullyQualifiedName~CatalogFeature36Tests"` — passed, 3/3.
- `roleplay validate catalog` — valid disposable import, 251 records. It reports three advisory
  near-duplicate warnings for the new generic read/write/procedure vocabulary; no validation error
  or live-data change occurred.
- `dotnet test DantesRoleplay.Tests\\DantesRoleplay.Tests.csproj --no-build --no-restore --filter
  "FullyQualifiedName~CatalogValidationTests.Embedded_startup_content_is_the_canonical_catalog_content"`
  — passed, 1/1, after a non-incremental rebuild.
- An attempted complete-suite run reached an unrelated intermittent failure in
  `SessionFeature1Tests.Starts_one_session_atomically_and_a_fresh_host_derives_the_active_record`:
  its exact exception-subclass assertion observed `TaskCanceledException` where it expects the
  base `OperationCanceledException`. The isolated rerun passed, 1/1. A clean shared-suite baseline
  is still required before accepting a dependent Feature 36 slice.

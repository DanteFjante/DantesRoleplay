# Feature 24 Slice 2 receipt — armor-training state and diagnostics

Status: **accepted**
Owner: Feature 24, Armor, shields, armor training, and derived Armor Class
Source: `source.dnd2024.srd-5.2.1`, `Rules Glossary > Armor Class and Armor Training`, PDF p. 176

## Delivered boundary

- Added `dnd2024.armor-training`: a closed, source-attributed ordered subset of `light`, `medium`,
  `heavy`, and `shield`. Absence remains unknown; `[]` is explicitly known-no-training.
- Added `procedure.mechanic.dnd2024.armor-training` and closed writer/diagnostic reader mechanics.
- The writer records or corrects exactly one component. The reader is effect-free and reports
  valid, absent, malformed, or invalid state without inference.
- Added fresh-import coverage for canonical values, explicit empty state, malformed input, corrupt
  state, replay, fixed provenance, and unchanged unrelated Armor Class state.

## Evidence

- `dotnet test --filter FullyQualifiedName~CatalogFeature24Slice2Tests` — passed (1 test).
- `dotnet run --project DantesRoleplay.Tools -- validate catalog` — valid: 410 records and 75
  non-blocking near-duplicate warnings; no live data touched.
- `dotnet test --no-restore` — passed: 791 tests, 0 failures.
- `git diff --check` — passed (line-ending warnings only).

## Deliberate exclusions

No class/species/monster/feat grant; equipped-item aggregation; AC calculation or migration; D20
drawback; Speed adjustment; spellcasting restriction; equipment transition; action; timing; schema
migration; public-surface change; generic C# rule; or persistent catalog import was added.

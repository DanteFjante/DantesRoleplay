# DND2024 Thalorien world expansion Slice 1 receipt — History and Lore

Status: **accepted 2026-08-30**

## Delivered boundary

The authorized notebook now admits a complete world-sized page of at most 200 records instead of
silently truncating the current Thalorien corpus at 100. The connected projection parses only text
already admitted for its audience and classifies 35 reviewed, time-bound turning points as History.
Enduring places, customs, institutions, peoples, interpretations, and current conditions remain
Lore. Unstructured safe text falls back to a generic Lore card.

Generic C# contains no Thalorien vocabulary. No canonical fact, secret, clue, epistemic state,
location, map, route, schema, migration, or public caller parameter changed.

## Evidence

- Focused modular knowledge tests in Release: 17 passed, 0 failed.
- Full prototype suite: 157 passed, 0 failed.
- Production prototype build: passed.
- An isolated copy of the normal `dnd2024-main` database returned all 158 current authorized DM
  entries through the rebuilt private notebook route. Classification produced 35 History and 123
  Lore records: 158 projected, 158 unique titles, and no omitted or duplicated record.
- The same isolated state under Orban's actor seat returned only 11 reviewed entries and 1 known
  location. All 11 remained Lore; no DM-only record entered the actor notebook.
- The normal database itself was read but not mutated, and the running development server was not
  restarted.

The repository-wide .NET suite was also started in Release. It reached unrelated existing failures
in the dirty checkout: a web-surface `innerHTML` guard, an expected prototype schema count of 154
versus the current 166, and missing catalog component paths used by D&D ability-check/package tests.
The run was stopped after those repeated missing-path failures. None of the focused knowledge or
prototype tests failed.

## Deliberate exclusions

City maps, Elaris and Kharad Veyr materialization, new settlements, new secrets, and clues remain
outside this receipt. Canonical additions remain blocked until an accepted application-ECS world
authoring transaction exists.

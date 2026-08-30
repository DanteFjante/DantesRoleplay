# DND2024 Rules reference dynamic catalog Slice 2 receipt

Status: **accepted 2026-08-30**

Implementation document: `web/DND2024-RULES-REFERENCE-DYNAMIC-CATALOG-SLICE-2-IMPLEMENTATION.md`

Dependency tree/leaf: `web/DND2024-WEB-INFORMATION-HUB-DEPENDENCY-TREE.md`, Leaf 13 / G

Ruleset alignment: **dnd2024-compatible presentation of dnd2024-owned records**

Published page: `dnd2024-play`, active revision **14**

## Delivered boundary

- Removed the fourteen-ID Rules allowlist. The tab now traverses the bounded, paginated active
  application catalog `entities` branch and accepts every active entity summary regardless of ID.
- Added the secured, read-rate-limited generic application route
  `GET /api/applications/{applicationId}/catalog/browse`; it delegates to the existing ruleset-neutral
  catalog explorer and is admitted by the exact remote web path gate.
- Added dynamic family filters, search across names/families/IDs/summaries/sources, deterministic
  ordering, a complete result count, 80-entry render windows with “Show more,” automatic refresh on
  opening Rules, and an explicit refresh action.
- Added lazy exact-record detail for an available activated catalog. Identity, active positive
  revision, current `dnd2024.source.srd-5.2.1` citation, bounded locator, and optional authored
  presentation summary are verified before detail is shown.
- Added a build-generated minimal fallback projection from the canonical authored D&D entity files.
  This keeps the page readable when the runtime correctly refuses its drifted activation snapshot,
  without activating a new mechanic manifest, changing a state-space binding, exposing raw JSON, or
  maintaining a second rule ID list.
- The accepted bundle contains **2,380** current source-cited active entities in ten derived families:
  Character Options, Creatures, Equipment, Final Dictionaries, Gameplay Toolbox, Magic Items,
  Shared Rules, Spells, Structural Support, and Vocabulary. The count is evidence for this revision,
  not a pinned contract.

No D&D record, source locator, mechanic, procedure, schema, live game state, application activation,
state-space binding, database migration, typed effect, or transaction changed.

## Evidence

| Check | Result |
| --- | --- |
| Focused Rules tests | Passed: 8 tests covering recursive/paginated discovery, new IDs without code changes, revised details, source fidelity, malformed browse, filtering, and bundle fallback. |
| Complete React suite | Passed: 127 tests, 0 failures. |
| Production server bundle | Passed; emitted React JS/CSS, two reviewed city maps, and a 1,094,003-byte `rules-catalog.json`. |
| Bundle publication | Passed: revision 14, 6 ZIP entries, 5 assets, 7,833,329 compressed bytes. |
| Focused `WebInterfaceTests` | Passed: 89 tests, 0 failures, including the public route and remote-path boundary. |
| Catalog validation | Passed: 154 developer catalog records, 0 errors, 26 existing near-duplicate warnings; disposable database only. |
| Live desktop browser | Passed: Rules loaded 2,380 references; ten family choices were visible; `Fireball` search returned four references; exact Fireball summary, source locator, and catalog revision rendered; refresh retained the result. |
| Slice whitespace review | Passed. |

The repository-wide .NET suite was started after focused acceptance. It encountered unrelated
existing checkout failures, primarily the missing
`catalog/applications/dnd2024/components/dnd2024.weapon-profile.json` expected by D&D harness tests,
plus an existing weapon-damage contract failure. The repeated run was stopped. This slice changes no
catalog component or gameplay mechanic; the focused web/server suites are green.

## Deliberate exclusions

- application activation or state-space upgrade/migration;
- rule editing or catalog authoring in the browser;
- executable rule text, calculations, outcomes, or LLM-generated summaries;
- mechanics, procedures, queries, raw entity JSON, and developer documentation;
- making the evidence count a permanent coverage target.

## Rollback

The prior page remains an immutable page revision and can be reactivated through the existing page
editor. Source rollback removes the browse route, dynamic reader/view, generated minimal projection,
styles, and tests. No catalog or game-state rollback is required.

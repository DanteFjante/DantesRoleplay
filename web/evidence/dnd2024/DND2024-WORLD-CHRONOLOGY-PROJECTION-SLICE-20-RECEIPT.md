# D&D 2024 World chronology projection slice 20 receipt

Status: **accepted in code; live application activation pending**
Date: 2026-08-30
Owner: `WORLD_AND_LORE_PLAN.md`, World tab presentation

## Delivered boundary

- Added the confirmed read-only chronology route at `GET /api/applications/{applicationId}/campaigns/{campaignId}/chronology?perspective=player|dm`.
- Added a closed, separately activated application chronology binding. The generic host contains no dnd2024 component or relationship IDs.
- Player projection admits active `public` and `party` records, emits opaque response-local IDs, and omits canonical IDs, visibility, and subjects.
- DM projection also admits `gm` records and exposes only validated same-World subjects. An Actor requesting `dm` is rejected before World-state reads.
- Connected History now consumes dedicated chronology only. History-like authorized knowledge remains lore and is never promoted into dated history.
- The History UI supports chronology records without a consequence field and distinguishes no dated history from a filter with no matches.

## Verification evidence

- Focused .NET chronology and local-audience boundary: **11 passed, 0 failed**. The six chronology tests cover the separate activated document, Player filtering/non-disclosure, DM subjects, Actor denial, and cross-World fail-closed behavior.
- Web suite: **133 passed, 0 failed**.
- Server web build: **passed**, 1,622 modules transformed.
- Catalog validation: **154 records valid**, 26 advisory near-duplicate warnings, and no live data touched.
- Server project build: **passed** with no warnings or errors during the slice.
- Repository-wide acceptance began successfully with **21 LocalAI tests passed**, then encountered unrelated shared-worktree failures: an existing weapon-damage contract test, owner-ledger count drift (69 expected versus 73 present), and repeated missing `catalog/applications/dnd2024/components/dnd2024.weapon-profile.json` fixture failures. The broad run was stopped after those failures were reproducible.

## Publication and rollback

- The chronology-aware web bundle is active as local page revision **16** at `/ui/dnd2024-play`.
- Published bundle: `C:\Users\dante\AppData\Local\Temp\dnd2024-play-world-chronology-slice-20.zip`, SHA-256 `CA69A88A8EE61511959501C47FD137C281A3DA732D066D0C4C8C80E26E13A57B`.
- Pre-publication revision **15** export: `C:\Users\dante\AppData\Local\Temp\dnd2024-play-revision-15-pre-chronology.zip`, SHA-256 `66D1C7D68F56FEF9CC8346C89ECC5AB56E88243AF3BDF06FD6BA12BA8B862B8B`.
- Final local readback: audience context **200**, page **200**, and chronology **503 `CHRONOLOGY_UNAVAILABLE`** because activation 10 predates the new `world-chronology.json` binding.

## Deliberate exclusions and next boundary

- No broad dnd2024 application activation was performed. The application source contains other in-progress changes, so activating it here would cross the confirmed synchronization boundary and absorb unrelated work.
- No live Thalorien chronology entities were authored or changed.
- Until a reviewed activation includes `catalog/applications/dnd2024/metadata/world-chronology.json`, the History consumer truthfully shows no dated World history rather than falling back to knowledge.
- The next chronology boundary is an explicit activation preview/review, followed separately by reviewed live chronology authoring.

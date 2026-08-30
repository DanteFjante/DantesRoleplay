# Caldris live content slice 2 receipt — populated World reference

Status: **accepted 2026-08-30**

Implementation document: `CALDRIS-LIVE-CONTENT-SLICE-2-IMPLEMENTATION.md`

## Delivered boundary

- Authored and atomically imported 32 reviewed Caldris records through 125 typed effects beneath
  the existing `world.caldris` root.
- Added five dated history entries, seven public lore facts, eight opening NPCs with distinct
  motives, and three active factions with exact member and territory relationships.
- Added a World atlas, an Eredane regional map, a Bramblebridge local map, and stable anchors for
  the existing places. The two reviewed PNG files are served by the shared map component host.
- Aligned local-loopback GameMaster knowledge and chronology reads with the existing campaign
  selector. Actor seats remain bound to their configured campaign, remote callers remain denied,
  and exact application/campaign binding validation remains mandatory.
- Corrected chronology projection so records belonging to another World and calendar are ignored
  instead of making the selected World's chronology unavailable.
- Published the reviewed React bundle as active `dnd2024-play` revision **19**.

## Live transaction and rollback evidence

- Consistent pre-import SQLite backup:
  `runtime/backups/dantesroleplay-pre-caldris-content-v2-20260830T1315.db`; SQLite
  `quick_check=ok`.
- Exact governed preview: 32 reviewed entities, 125 effects, operation
  `8db0c1cba15092b6727d34d71f4c9abd`; no state written.
- Exact governed commit: 32 reviewed entities, 125 effects, operation
  `7f2e13699e6053f2446a973b579e9241`; not a replay.
- Pre-publication page revision 18 export:
  `runtime/backups/dnd2024-play-revision-18-pre-caldris-content-v2.zip`, SHA-256
  `059A97A1531B4E1F7C0137A1625C3A906AD34A7DFBC53975C9CEF5CAEE473643`.
- Published revision 19 bundle:
  `runtime/backups/dnd2024-play-caldris-content-v2.zip`, SHA-256
  `1348076CE7D95C2256F1ED1E3249F16C93F702514C9C69B55DBA9B943E9C2DAB`.

## Verification evidence

- Focused .NET audience/chronology tests: **21 passed, 0 failed**.
- D&D website suite: **138 passed, 0 failed**.
- D&D server bundle and MCP server build: **passed**, with no build warnings or errors.
- Catalog validation: **156 valid records**, 27 existing near-duplicate advisories, no errors.
- Live API readback: knowledge `ready` with **7** entries; chronology `ready` with **5** entries;
  Caldris World and Eredane map images both returned HTTP 200 as `image/png`.
- In-app browser acceptance on the actual local page confirmed:
  - Map: Caldris atlas with Eredane and Solasca, closer Eredane map, and faction influence.
  - History: **5 of 5** events.
  - People: **8 of 8** visible.
  - Factions: **3 of 3** visible.
  - Lore: **7 of 7** visible.
- The page was returned to DM, World Overview after acceptance.

The repository-wide .NET run was also attempted and stopped after unrelated dirty-worktree
failures were reproducible: pre-existing expected-count drift (37/38 and 22/23), the existing
weapon-damage contract failure, and missing `character-creation` fixture paths. The focused tests,
catalog validator, live reads, and browser acceptance for this slice all passed.

## Deliberate exclusions

- No quests, campaign chapters/arcs, current scene, character sheets, encounters, inventory/items,
  wider 95-person cast, or 48-quest backlog was imported.
- People and faction directories remain DM projections under the established web authorization
  model.
- The local DM's Player-preview continues to omit notebook lore because the knowledge route does
  not yet issue a perspective-bound projection. It fails closed rather than reusing GM-authorized
  bytes. Public Player map and chronology projection are unchanged.

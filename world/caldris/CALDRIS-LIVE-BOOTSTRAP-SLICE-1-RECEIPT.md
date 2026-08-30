# Caldris live bootstrap slice 1 receipt

Status: **accepted**
Date: 2026-08-30

## Delivered boundary

- Created `world.caldris` as the live, public Caldris World root.
- Created `campaign.caldris.measure-of-mercy` as the live campaign root.
- Added the Eredane and Solasca continents; Bramblebridge, Candlefen, and Wren's Hollow; and the
  Gilded Kettle and North Bell Tower.
- Linked the campaign to Caldris and Bramblebridge without changing the existing Thalorien graph.
- Kept the server's public MCP capability catalog unchanged.

## Transaction evidence

- First dry run rejected campaign effect 10 because the active runtime schema fixes
  `creationMethod` to `manual`. No Caldris entity was written.
- Corrected the reviewed manifest to the active schema.
- Successful dry run: 27 effects, operation `e38d8924233398849aebe828b92929ae`.
- Successful atomic commit: 27 effects, operation `29d719e67dbe99efcfab04dbaaa7a03b`.
- Exact rerun returned replay receipts for both operation IDs and wrote no duplicate graph.
- A pre-write SQLite backup was created at
  `runtime/backups/dantesroleplay-pre-caldris-20260830T1245.db`.
- A post-write backup at `runtime/backups/dantesroleplay-post-caldris-verified-20260830T1250.db`
  passed SQLite `quick_check`.

## Readback evidence

- All nine entities returned through the application ECS web API at revision 1.
- World and campaign components returned their installed type version and schema hash.
- Containment readback returned two continents under Caldris, three settlements under Eredane, and
  two opening sites under Bramblebridge.
- Relationship readback returned the Caldris World link and Bramblebridge start reference.
- The same selector adapter used by the React website returned, in order:
  - Caldris → The Measure of Mercy
  - Thalorien → The Waystone at Brackenford
- Selecting the Caldris campaign returns connected state. It deliberately has no current scene yet.

## Verification

- Bootstrap helper build: succeeded with 0 warnings and 0 errors.
- D&D 2024 website suite: 137 passed, 0 failed.
- Live selector-model readback: succeeded.
- Existing Thalorien campaign entity remained readable at revision 1.
- Public surface inspection found the existing `system.world-state.sync` and no new bootstrap or
  compose public kind.

## Deliberate exclusions

This slice does not claim full live import of the authored cast, quests, lore, chronology, maps,
factions, secrets, items, or encounters. It does not create a current scene or player character.
Those remain later reviewed live-authoring slices.

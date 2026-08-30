# DND2024 live World/Campaign selector Slice 1 receipt — authorized context switching

Status: **accepted 2026-08-30**

Implementation document: `DND2024-LIVE-WORLD-CAMPAIGN-SELECTOR-SLICE-1-IMPLEMENTATION.md`

Dependency tree: `ruleset/dnd2024/DND2024-PROTOTYPE-SERVER-INTEGRATION-DEPENDENCY-TREE.md`, Leaf 5B

## Delivered

- The World/Campaign text in the existing React TopBar is now one clickable, keyboard-accessible
  control that opens a componentized popup.
- The popup groups exact readable campaign roots beneath their existing World identity, shows the
  active choice, and replaces the complete live hub envelope when another campaign is selected.
- The current selection and DM/Player perspective are preserved together as harmless device-local
  view preferences. They do not write game state.
- A browser campaign token is never trusted. The server-side adapter rediscovers exact campaign-root
  components inside the already authorized application/state space before accepting a switch.
- An actor seat remains limited to its server-issued campaign. Invalid, injected, malformed, or
  undiscovered campaign IDs return a closed denial before selected-campaign detail reads.
- The selector remains visible in the responsive TopBar and supports outside-click and Escape close.

## Evidence

- Full prototype suite: **148 passing, 0 failing**.
- Production Site build completed successfully.
- Focused tests prove multiple campaigns in one World, campaigns in separate Worlds, exact
  campaign-root verification, deterministic grouping, and actor cross-campaign denial.
- Live local `GET /api/hub` returned `200` with World **Thalorien**, Campaign
  **The Waystone at Brackenford**, selected IDs `world.thalorien` and
  `campaign.thalorien.brackenford`, and one World/one Campaign choice from current SQLite state.
- A live injected campaign request returned `403` with no replacement envelope.
- `http://localhost:6217/ui/dnd2024-play` returned `200` and continues to show the React page.
- `git diff --check` reported no new whitespace errors; only pre-existing line-ending notices.

## Deliberate exclusions

No World or Campaign creation, game-state mutation, schema, migration, catalog record, C# route,
D&D rule, remote gateway, player participation selector, or hosted deployment was added. A World
without a readable campaign is not selectable because the current hub remains campaign-backed.
The current database has only one readable campaign root, so the popup truthfully shows one choice
until another campaign is created.

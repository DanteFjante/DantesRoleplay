# Caldris live content slice 2 — populated World reference

Status: **accepted 2026-08-30**
Owner/roadmap: application World state, authorized knowledge, chronology, and D&D web presentation
Dependency tree/leaf: existing `system.world-state.sync` transaction beneath `world.caldris`
Ruleset alignment: `dnd2024-compatible`; authored setting/presentation state only
Source ID and locator: not applicable; no D&D rule is implemented
Outcome: Caldris visibly contains history, lore, maps, people, and factions in the website
Exclusions: character sheets, encounters, quests, current scene, full 95-person cast, and all 48 quests
Allowed areas: Caldris reviewed runtime manifest; local GM campaign-read policy and tests; reviewed map asset registry/bytes
Stop point: all five formerly empty Caldris sections render verified live content for the local DM;
public map and chronology projections remain available to Player perspective while the existing DM
Player-preview knowledge boundary continues to fail closed

## Confirmed decisions

The user's request to make the missing World sections available confirms this bounded import and
the required presentation asset keys. The existing world switcher already permits a host-configured
local GM to select any exact readable campaign root in the authorized state space. This slice aligns
knowledge and chronology reads with that selector: an Actor remains restricted to the configured
campaign, while a loopback GameMaster may request another campaign only after the existing binding
resolver validates it in the same application state space.

## D&D 5e 2024 alignment

| Concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Setting content | Not an SRD rule | Caldris world bible/cast/gazetteer | Store descriptive state only |
| Audience | Host role and application binding | authorized knowledge policy | Player isolation is unchanged; GM selection follows validated campaign roots |
| Maps | Display-only image and anchor state | World spatial components | No topology, travel, or rules are inferred |

## External implementation reference

No Foundry rule implementation is relevant because the slice performs no D&D calculation or
character/gameplay operation.

## Prerequisite evidence

- Browser inspection on Caldris reports 7 known places and 0 knowledge entries; Map, History,
  People, Factions, and Lore are empty.
- `readWorldDirectory` derives people from contained `actor.*`/`creature.*` entities and factions
  from `faction.caldris.*` plus the installed World faction component.
- Chronology requires a World clock, chronology components, and exact in-World/about edges.
- Lore requires fact/classification components plus exact in-World/about edges.
- Maps require reviewed asset keys, map visuals, and direct-child anchors.
- `system.world-state.sync` is now applicable because `world.caldris` exists.

## Runtime artifacts

- Five chronology entries, seven public lore facts, eight opening NPCs, and three factions.
- A Caldris world map and the reviewed Alderwick/Bramblebridge regional map, with three map scopes
  and anchors for all seven existing places.
- `caldris.world.*`, `caldris.region.eredane.*`, and `caldris.town.bramblebridge.*` reviewed asset keys.
- One additive/update-only manifest below `world.caldris`, bounded below 128 effects.

## Authoritative state and closed input

The manifest fixes IDs, names, exact existing revisions, component values, containment, and
relationships. Installed component references and schema hashes are backend-derived. Map registry
keys resolve only to repository-reviewed image bytes. The browser cannot supply identity, role,
state-space, component versions, or relationship scope.

## Behavior, result, and typed effects

Create new content beneath Caldris and add map/clock components to existing entities in one
`system.world-state.sync` root transaction. Preview the exact payload first, then commit the same
payload. The GM authorization change changes only the effective campaign token in a local-loopback
GameMaster grant; Actors and remote callers remain denied for any campaign other than their bound
one. Binding resolution and campaign-to-World validation remain mandatory.

## Failure, replay, and rollback contract

Malformed, unknown-schema, stale-revision, wrong-root, cross-application, remote, or actor
cross-campaign requests fail closed. The state synchronizer rolls back all effects on failure and
replays by request token. Unknown map keys still render no image.

## Implementation sequence

1. Add focused GM/Actor authorization tests, then the narrow policy correction.
2. Register and copy the reviewed Caldris map assets.
3. Author the closed live content manifest and create a consistent SQLite backup.
4. Run exact dry run, then atomic commit.
5. Rebuild/restart the local host, run focused/full web tests, and inspect all five tabs in-browser.
6. Record the receipt and stop.

## Acceptance matrix

| Case | Expected |
| --- | --- |
| GM selected campaign | validated Caldris knowledge/history are readable |
| Actor cross-campaign | denied before content access |
| Remote caller | denied |
| History | five ordered entries |
| Lore | seven public entries in DM; DM Player-preview remains empty until knowledge has a perspective-bound server read |
| People | eight contained NPCs with distinct motives in the DM World directory |
| Factions | three active factions and exact member/territory links in the DM World directory |
| Map | world, Eredane, and Bramblebridge images resolve; direct children are anchored |
| Atomicity/replay | exact preview then commit; replay-safe request token |
| Compatibility | Thalorien remains selectable and its tests pass |

## Verification commands

- Focused knowledge policy and chronology tests.
- D&D website test suite and server build.
- `roleplay validate catalog` because the live state uses only already-activated schemas.
- Live API readback plus in-app browser inspection of Map, History, People, Factions, and Lore.

## Completion receipt and exit gate

`CALDRIS-LIVE-CONTENT-SLICE-2-RECEIPT.md` records live/browser readback. Stop before quests,
campaign chapters/arcs, a current scene, inventory/items, encounters, and the wider cast.

# D&D 2024 web UI Slices 7D2–7D3 completion receipt

Accepted: **2026-08-27**
Implementation boundary: [combined Slice 7D implementation](DND2024-WEB-UI-SLICE-7D-IMPLEMENTATION.md)
Roadmap owner: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 5

## Delivered boundary

This combined product batch accepts the reviewed live knowledge-state synchronization boundary and
the player-facing Knowledge viewport together. The running D&D 2024 page now resolves the fixed
loopback Orban seat through ambient policy, verifies campaign participation, reads only effective
authorized knowledge, and presents safe text without canonical knowledge IDs, sensitivity labels,
hidden counts, or caller-selected identity/role inputs.

The page has a fifth game-styled **Knowledge** tab with remembered-lore cards, stance/category
filters, search, empty/error states, and accessible keyboard tab navigation. Knowledge reloads with
the selected campaign and does not depend on the currently selected entity.

The private boundaries accepted by this batch are:

- commit kind `system.knowledge-state.sync`, restricted to the private operator and an exact
  reviewed campaign/list payload;
- `GET /api/applications/{applicationId}/campaigns/{campaignId}/knowledge`, returning only
  `{status, entries:[{text, stance, presentationKind}]}` with `Cache-Control: no-store`.

The synchronization validates campaign participation, canonical world membership, activated
state vocabulary, and optimistic relationship revisions before one atomic application-ECS effect
batch. Dry-run, commit, replay identity, audit, and failure-without-partial-write behavior remain
owned by the generic transaction/effect boundary.

## Reviewed live manifest

Exactly these eleven existing Orban relationships were committed as `known` in
`campaign.thalorien.brackenford`:

1. `fact.thalorien.brackenford`
2. `fact.thalorien.greenmantle`
3. `fact.thalorien.frontier-watch`
4. `fact.thalorien.settlement-hospitality`
5. `fact.thalorien.present-dangers`
6. `fact.thalorien.wilderness-danger`
7. `fact.thalorien.continent.thalos`
8. `fact.thalorien.seven-kingdoms`
9. `fact.thalorien.seven-kingdom-names`
10. `fact.thalorien.peace-as-value`
11. `fact.thalorien.peace-generations`

No baseline, rumour, clue, or secret was granted. In particular,
`secret.thalorien.brackenford-goblin-migration` and
`secret.thalorien.brackenford-waystone-cellar` remain unknown and were absent from the endpoint,
DOM, search results, counts, and browser text.

## Live activation and readback

- Pre-write backup:
  `DantesRoleplay.MCPServer/data/backups/dantesroleplay-before-7d23-20260827-knowledge.db`
  (9,035,776 bytes).
- Catalog preview: source `dnd2024-core`, 373 winners, zero problems.
- Preview fingerprint:
  `B2BEB1EDCD9DBFB6ECBC2E7B7A4B5B8CCBA361110BB9AF33BA01D2712A71EF23`.
- Activation operation: `7d230000000000000000000000000001`.
- Active catalog revision: **3**; activation fingerprint:
  `87E911B7976667AC0029E89CB11C9CF03DDC88DB73F7D35285284B7352D15744`.
- Activated knowledge binding content fingerprint:
  `A4255BCDF55435D514B400FC3899788D32CC10DF361A71C0DA6C7552062A9255`.
- Synchronization dry-run: 11 reviewed, 11 changed; effect operation
  `f645e84951b47a34db95d275da8bc1f5`.
- Synchronization commit: 11 reviewed, 11 changed; effect operation
  `2fdfaf506495ed13ca6e0d28840094b0`; wrapper operation
  `05cf75a8e71243099f0fdb28b1fccd30`.
- Direct database readback found exactly eleven Orban knowledge-state relationships, all
  `{"state":"known"}` at revision 1.
- Live endpoint returned HTTP 200, `status=ready`, eleven entries, and `no-store`; canonical fact
  IDs, secret IDs, sensitivity metadata, and excluded secret phrases were absent.

The development host also received the missing `repository` allowed-root mapping required to
resolve its already-registered application catalog sources. This removed the reproducible
`SOURCE_ROOT_UNKNOWN` activation failure without changing source ownership.

## Verification evidence

- Focused knowledge and web tests: **106 passed**, zero failed.
- Disposable catalog validation: **144 valid records** (14 mechanics, 50 procedures,
  33 components, 10 event types, 2 subscriptions, 35 entities); 21 existing advisory
  near-duplicate warnings and no live database write.
- Full shared suite after updating the closed commit-kind contract: **1,404 passed**, zero failed,
  zero skipped.
- Protocol walk for the changed MCP dispatcher: **6 passed**, zero failed, 2 intentional skips.
- Build: zero warnings and zero errors.
- In-app browser acceptance at `/ui/dnd2024-play`: campaign, state space, and Orban seat loaded;
  the Knowledge tab rendered eleven cards; searching `hospitality` reduced the result to the one
  matching lore card; clearing restored eleven; ArrowLeft/ArrowRight moved between tabs; DOM safety
  checks found no canonical fact/secret IDs or excluded secret phrases.

## Deliberate exclusions

This receipt does not accept a caller-selectable actor/role, a baseline grant, secret/rumour/clue
authoring, generic entity/fact browsing, map coordinates, a known-place map, visual attachments,
image generation, tactical-grid interaction, encounter authoring, or weapon attack/damage. The
next player-information leaves remain 7E (display-only known-place map), 7F (reviewed location and
person imagery), and Order 8 viewport polish before combined acceptance.

# DND2024 web information hub Slice 4 receipt — location people and DM holdings

Status: **accepted 2026-08-28**

Implementation document: `DND2024-WEB-INFORMATION-HUB-SLICE-4-IMPLEMENTATION.md`

Dependency tree/leaf: `DND2024-WEB-INFORMATION-HUB-DEPENDENCY-TREE.md`, supplemental Leaf 2B

Published source revision: `379847e6798cf0b166297c39b23506945a6c9b96`

## Delivered boundary

- Added a reusable location workspace with separate Details, People & Creatures, and Holdings
  components. Player sees Details and People & Creatures; Holdings appears only in DM perspective.
- Added explicit server-only fixture occupants for every location. Known NPCs and creatures expose
  readable identity, role, summary, disposition, and background. DM additionally receives hidden
  occupants, private motives, and secret context.
- Added server-only fixture containers and bounded item lines for every location. Holdings remain
  wholly DM-only and include no transfer, discovery, lock, trap, weight, value, or rules actions.
- Extended the closed projector to filter people before transport and attach holdings only after DM
  perspective is resolved. Awareness flags are never returned.
- Added a safe subsection normalizer. Unknown values and Player attempts to select Holdings resolve
  to Details; an accepted DM-to-Player perspective change also closes Holdings immediately.
- Added responsive occupant and holding cards, keyboard-operable subsection navigation, explicit DM
  labels, and friendly empty states.

This remains a fixture-backed information surface. The component and exclusion behavior are
accepted, but dependency-tree Leaves 3 and 5 remain planned for authoritative live World,
presence/containment, NPC profile, creature detail, and inventory data.

## Evidence

| Check | Result |
| --- | --- |
| Focused web checks | Passed: 15 tests, 0 failures across audience policy, occupant filtering, holdings exclusion, subsection normalization, perspective fallback, map/location state, and client envelope validation. |
| Exact committed-source full suite | Passed in an isolated checkout: 57 tests, 0 failures. |
| Exact committed-source production build | Passed with dynamic `/` and private `GET /api/hub` routes. |
| Local DM route walk | HTTP 200; the current Archive exposed 3 occupants and 2 holdings in DM, while Player preview exposed 2 occupants and no holdings key. |
| Local Player route walk | A Player requesting DM remained Player with one allowed perspective and 9 known occupants across 5 locations. Player root markup and JSON had zero private-person or holding matches. |
| Player exclusion | Player serialization omitted every private motive/secret, hidden-person ID/name, holding ID, item name, DM note, `holdings`, `motive`, and `playerKnown` key. |
| Client asset boundary | Scanned 10 emitted text assets with zero private-person, hidden-person, holding, server-module, or DM environment-key matches. |
| Private deployment | Sites version 6 succeeded at `https://dantes-roleplay-dnd2024-table.dantecavallin.chatgpt.site` using environment revision 2. |
| Access policy | Owner role, custom access, exactly one allowed account, zero external visitors, and zero workspace or tenant groups. |

No catalog validation, parent .NET suite, or MCP protocol walk was required because no catalog,
C#, MCP surface, dependency registration, or live database changed.

## Deliberate exclusions

- live SQLite/catalog/containment/world/campaign/character/encounter/rules transport;
- runtime editing, persistence, creation, transfer, discovery, or inventory mutation;
- a canonical NPC presentation-profile or visual-reference schema;
- NPC or creature portraits, creature statistics, actions, AC, HP, CR, checks, DCs, or outcomes;
- item weight, value, attunement, equipment, lock, trap, or capacity mechanics;
- World History, campaign expansion, Party dossiers, Current View expansion, or Rules expansion;
- public sharing, app-owned sign-in, seat management, and LLM calls.

## Rollback

Sites version 5 remains the source/deployment rollback boundary. No database, catalog, rule, schema,
campaign, world, character, inventory, or container state requires reversal.

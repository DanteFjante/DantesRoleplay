# DND2024 web information hub Slice 6 receipt — World reference directories

Status: **accepted 2026-08-28**

Implementation document: `DND2024-WEB-INFORMATION-HUB-SLICE-6-IMPLEMENTATION.md`

Dependency tree/leaf: `DND2024-WEB-INFORMATION-HUB-DEPENDENCY-TREE.md`, supplemental Leaf 2D

Published source revision: `0756c17113bd7a1542e64d3bf1656d86145c12e0`

## Delivered boundary

- Expanded World navigation to Overview, Map, History, Locations, People, Factions, and Lore.
- Added a world-wide People & Creatures directory derived exactly from the authorized location
  occupant projection. It supports name/content search, kind and region filters, readable background
  cards, DM context when authorized, and links back to the location People subsection.
- Added five fixture factions with public summaries, goals, methods, influence, members, territory,
  and relationships. Player receives four known factions; DM receives the hidden fifth faction plus
  private agenda and secret context.
- Added ten fixture lore entries covering customs, relics, places, factions, omens, and history.
  Player receives eight known entries; DM receives two hidden entries and private truth/table notes.
- Added safe cross-navigation from lore and factions to existing Location, History, and Faction
  surfaces without duplicating those detail owners.
- Extended the closed projector so private memberships, territories, relationships, and lore links
  are resolved only after the target entity is present in that audience's envelope.
- Added pure, deterministic People, Faction, and Lore filters plus closed client validation for every
  projected record/link shape.
- Added responsive directory controls, cards, relationship chips, DM panels, keyboard-visible
  controls, and friendly empty states.

This remains fixture-backed presentation evidence. Live faction, authorized knowledge, actor
profile/presence, and World transport owners remain separate future acceptance boundaries.

## Model requirement

No AI model is required for this implementation. The three directories render structured projected
data directly, make no OpenAI request, and require no `OPENAI_API_KEY`. A model may later be added as
an optional authoring or summarization aid, but it must never be required to browse known data.

## Evidence

| Check | Result |
| --- | --- |
| Focused web checks | Passed: 22 tests, 0 failures across perspective policy, directory derivation, record/link filtering, search/filter helpers, section normalization, DM preview equality, and client validation. |
| Working-copy full suite | Passed: 87 tests, 0 failures, including unrelated record work already present in the checkout. |
| Exact committed-source full suite | Passed in an isolated checkout: 64 tests, 0 failures. |
| Exact committed-source production build | Passed with dynamic `/` and private `GET /api/hub` routes. |
| Local DM route walk | HTTP 200; DM returned 14 people/creatures, 5 factions, and 10 lore entries with private context. |
| Local Player route walk | HTTP 200; a Player requesting DM remained Player with one allowed perspective and received 9 people/creatures, 4 factions, and 8 lore entries. |
| Player exclusion | Player serialization and initial markup contained zero new/prior hidden or private canary matches, no private directory keys, and no hidden-location link. |
| Client asset/model boundary | Scanned 11 emitted text assets with zero hidden/private, server-module, DM environment-key, `OPENAI_API_KEY`, or OpenAI API endpoint matches. |
| Private deployment | Sites version 8 succeeded at `https://dantes-roleplay-dnd2024-table.dantecavallin.chatgpt.site` using environment revision 2. |
| Access policy | Owner role, custom access, exactly one allowed account, zero external visitors, and zero workspace or tenant groups. |

No catalog validation, parent .NET suite, or MCP protocol walk was required because no catalog,
C#, MCP surface, dependency registration, live database, or model integration changed.

## Deliberate exclusions

- live SQLite/catalog graph transport or permanent faction/lore/profile schemas and IDs;
- runtime editing, persistence, creation, deletion, faction agenda/front mutation, or simulation;
- knowledge acquisition, inference, semantic search, embeddings, or generated lore;
- portraits, creature statistics, actions, AC, HP, CR, checks, DCs, or outcomes;
- Campaign, Party, Current View, or Rules expansion;
- OpenAI/model calls, API keys, background prompts, and per-tab generation;
- public sharing, app-owned sign-in, and seat management.

## Rollback

Sites version 7 remains the source/deployment rollback boundary. No database, catalog, rule, schema,
world, faction, lore, person, campaign, character, inventory, or container state requires reversal.

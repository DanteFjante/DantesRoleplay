# DND2024 web information hub Slice 5 receipt — World History

Status: **accepted 2026-08-28**

Implementation document: `DND2024-WEB-INFORMATION-HUB-SLICE-5-IMPLEMENTATION.md`

Dependency tree/leaf: `DND2024-WEB-INFORMATION-HUB-DEPENDENCY-TREE.md`, supplemental Leaf 2C

Published source revision: `d1af159192333fc733a293562be9274ca2b0a140`

## Delivered boundary

- Added World History as the third World section, ordered Overview, Map, History, Locations.
- Added nine fixture setting events with date/era, category, region, status, summary, and an explicit
  persistent world consequence. These are World events rather than campaign session recaps.
- Added reusable `WorldHistory`, `HistoryFilters`, and `HistoryTimeline` components with safe text
  search, region/category filters, deterministic newest/oldest ordering, and a friendly empty state.
- Added authorized location links that select and open the existing location workspace instead of
  duplicating location detail. People and creature references are presented as compact context.
- Extended the closed server projector so Player receives seven known events while DM receives all
  nine plus hidden truth and follow-on context. Links are resolved only against entities already
  present in that perspective, preventing a safe event from disclosing a hidden place or person.
- Extended client validation to require the complete projected event and resolved-link shapes.

This remains a fixture-backed presentation surface. The chronology UX and exclusion behavior are
accepted, but dependency-tree Leaf 7 remains missing until an authoritative, audience-aware live
World chronology owner is confirmed and implemented.

## Evidence

| Check | Result |
| --- | --- |
| Focused web checks | Passed: 17 tests, 0 failures across audience policy, history filtering, nested-link redaction, DM preview equality, section normalization, stable ordering, and client envelope validation. |
| Working-copy full suite | Passed: 82 tests, 0 failures, including unrelated record work already present in the checkout. |
| Exact committed-source full suite | Passed in an isolated checkout: 59 tests, 0 failures. |
| Exact committed-source production build | Passed with dynamic `/` and private `GET /api/hub` routes. |
| Local DM route walk | HTTP 200; DM seat and perspective returned 9 events with private history context. |
| Local Player route walk | HTTP 200; a Player requesting DM remained Player with one allowed perspective and 7 events. |
| Player exclusion | Player serialization and initial markup contained zero history-secret, hidden-event, hidden-location, hidden-person, holding, or prior secret canary matches; no `dmTruth` field or hidden location link was returned. |
| Client asset boundary | Scanned 11 emitted text assets with zero history/private/hidden, prior secret, server-module, or DM environment-key canary matches. |
| Private deployment | Sites version 7 succeeded at `https://dantes-roleplay-dnd2024-table.dantecavallin.chatgpt.site` using environment revision 2. |
| Access policy | Owner role, custom access, exactly one allowed account, zero external visitors, and zero workspace or tenant groups. |

No catalog validation, parent .NET suite, or MCP protocol walk was required because no catalog,
C#, MCP surface, dependency registration, or live database changed.

## Deliberate exclusions

- live SQLite/catalog/event-ledger history transport or a permanent chronology component/schema;
- runtime editing, persistence, creation, deletion, world mutation, or campaign-to-world promotion;
- Campaign Adventure Log, outcomes, visited-place ownership, or campaign selection;
- factions, lore encyclopedia, standalone people directory, portraits, or entity-to-image links;
- Party dossiers, adaptive Current View, or Rules expansion;
- public sharing, app-owned sign-in, seat management, and LLM calls.

## Rollback

Sites version 6 remains the source/deployment rollback boundary. No database, catalog, rule, schema,
campaign, world, history, character, inventory, or container state requires reversal.

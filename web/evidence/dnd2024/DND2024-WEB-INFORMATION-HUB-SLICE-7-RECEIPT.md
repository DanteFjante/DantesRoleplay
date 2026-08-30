# DND2024 web information hub Slice 7 receipt — Campaign workspace

Status: **accepted 2026-08-28**

Implementation document: `DND2024-WEB-INFORMATION-HUB-SLICE-7-IMPLEMENTATION.md`

Dependency tree/leaf: `DND2024-WEB-INFORMATION-HUB-DEPENDENCY-TREE.md`, supplemental Leaf 2E

Published source revision: `594ad73e09918e99a376a40a723462f2806fa828`

## Delivered boundary

- Replaced the Campaign preview with a componentized workspace containing Overview, Adventure Log,
  Places Visited, and Outcomes.
- Added a campaign overview with premise, chapter, active objective, stakes, progress, next milestone,
  audience-derived summary counts, the latest remembered event, and DM context when authorized.
- Added six fixture campaign-log entries. Player receives five witnessed/shared entries; DM receives
  the hidden continuity entry plus behind-the-scene notes and open threads.
- Added five explicit fixture visit records with first/last visit, visit count, status, memory, and
  safe links into the existing World location view. No visit is inferred from current position, map
  selection, or recap prose.
- Added five structured situation outcomes. Player receives four known outcomes; DM receives the
  hidden fifth outcome and private ramifications.
- Resolved Campaign links only against locations, people, and factions already present in the same
  audience envelope. Hidden targets, names, counts, and associations remain absent from Player
  transport.
- Added deterministic search, order, region, and status filters; friendly empty states; native
  section/filter controls; focus handoff; and responsive Campaign layouts.
- Added `DND2024-SCOPED-MAP-VIEWS-FUTURE-PLAN.md` documenting World, Region, City, and Location
  scopes, explicit parent/child links, separate coordinate spaces, audience-safe layers, optional
  reviewed generated location imagery, provenance, failure behavior, and seven future slices.

This remains fixture-backed presentation evidence. It does not restore live campaign authority,
create a campaign-owned visit schema, or duplicate World state.

## Evidence

| Check | Result |
| --- | --- |
| Focused Campaign/audience checks | Passed: 25 tests, 0 failures. |
| Working-copy full suite | Passed: 90 tests, 0 failures, including unrelated record work already present in the checkout. |
| Exact committed-source full suite | Passed in an isolated checkout: 67 tests, 0 failures. |
| Exact committed-source production build | Passed with dynamic `/` and private `GET /api/hub` routes. |
| Local DM route | HTTP 200; DM received 6 log entries, 5 visited places, and 5 outcomes with private context. |
| Local Player preview route | HTTP 200; effective audience remained DM seat/Player perspective and received 5 log entries, 5 visited places, and 4 outcomes. |
| Player exclusion | Player serialization contained no hidden log/outcome entries, DM context, DM thread, DM ramification, or campaign-secret canaries. |
| Safe campaign links | Tests prove campaign place/log/outcome links resolve only to entities present in the projected World. |
| Client asset/model boundary | Nine emitted client text assets contained no campaign-secret canaries, DM environment keys, `OPENAI_API_KEY`, or OpenAI API endpoint. |
| Private deployment | Sites version 9 succeeded at `https://dantes-roleplay-dnd2024-table.dantecavallin.chatgpt.site` using environment revision 2. |
| Access policy | Owner role, custom access, exactly one allowed account, zero external visitors, and zero workspace or tenant groups. |

No catalog validation, parent .NET suite, or MCP protocol walk was required because no catalog,
C#, MCP surface, dependency registration, live database, or D&D mechanic changed.

## Model requirement

No AI model is required. Campaign browsing, filtering, cross-navigation, and perspective switching
render structured projected data directly and make no OpenAI request.

## Deliberate exclusions

- live campaign creation, selection, runtime authority restoration, SQLite/catalog transport, or
  persistence;
- session/outcome/visit creation, editing, deletion, inference, or world mutation;
- campaign-aware knowledge acquisition, encounter state, party changes, or automatic summaries;
- scoped-map runtime components, map schemas, generated assets, generation requests, or tactical
  maps;
- D&D rules, checks, DCs, outcomes, travel calculations, or model calls; and
- public sharing, app-owned sign-in, or access-policy changes.

## Rollback

Sites version 8 remains the deployment rollback boundary. No database, catalog, rule, schema,
campaign, world, visit, outcome, map, character, or inventory state requires reversal.

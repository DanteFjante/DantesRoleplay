# DND2024 web information hub Slice 3 receipt — authorized World map and DM seat repair

Status: **accepted 2026-08-28**

Implementation document: `DND2024-WEB-INFORMATION-HUB-SLICE-3-IMPLEMENTATION.md`

Dependency tree/leaf: `DND2024-WEB-INFORMATION-HUB-DEPENDENCY-TREE.md`, supplemental Leaf 2A

Published source revision: `0eee113719a949d08a053c81ff42e4eca96167eb`

## Delivered boundary

- Repaired the private Site's owner DM seat. The server now accepts an exact trusted
  `oai-authenticated-user-email` through a secret deployment allowlist in addition to supporting
  genuine Site-scoped authenticated-user IDs. The former account-level ID environment value was
  removed because it cannot match a Site-scoped dispatcher identity.
- Kept the audience policy non-escalating. DM can select DM or Player preview; a Player requesting
  DM still receives Player. Missing or malformed identity/configuration fails closed.
- Added a componentized World Map section between Overview and Locations, with accessible marker
  buttons, a selected-place summary, and a handoff to the existing reusable location detail view.
- Added an original 1672-by-941, label-free Eldervale map base. It contains terrain only; all place
  names, markers, and selection state are HTML projected from the server envelope.
- Added two unrevealed fixture locations for DM. Player responses omit their IDs, names, positions,
  summaries, secrets, and counts before serialization.
- Derived region and known-place counts from the already projected location subset, preventing
  aggregate counts from disclosing hidden places.
- Preserved map anchors as display percentages only. No travel distance, topology, reachability,
  movement, route-line, fog, or tactical meaning was added.

This remains a fixture-backed information surface. The World Map presentation and audience
contract are accepted, but dependency-tree Leaves 3 and 4 remain planned for authoritative live
World data and accepted game-state-owned visual associations.

## Evidence

| Check | Result |
| --- | --- |
| Focused web checks | Passed: 13 tests, 0 failures across DM identity, non-escalation, secret/hidden-marker exclusion, projected counts, anchors, map navigation state, and envelope validation. |
| Exact committed-source full suite | Passed in an isolated checkout: 55 tests, 0 failures. The shared dirty checkout also passed 73 tests before unrelated in-progress record tests changed; a later unrelated failure was excluded from this Site revision rather than modified. |
| Production build | Passed from the exact published source revision with dynamic `/` and `GET /api/hub` routes. |
| Local DM route walk | Root, Player preview, and DM returned HTTP 200; DM saw 7 locations and Player preview saw 5. |
| Local Player route walk | A Player requesting DM remained Player with one allowed perspective and 5 locations. Root markup and response had zero secret or hidden-location matches. |
| Response headers | The hub read remained `private, no-store` and now varies on both trusted authenticated-user ID and email. |
| Client asset boundary | Scanned 10 emitted text assets with zero secret, hidden-location, server-module, or DM environment-key matches. |
| Generated map asset | Built-in image generation, `stylized-concept` use case; original label-free fantasy terrain saved as `public/world-map-eldervale.png` and inspected at 1672 by 941 pixels. |
| Private environment | Revision 2 contains only the redacted secret `DND2024_DM_EMAILS` for this seat policy; the incorrect account-ID value was removed. |
| Private deployment | Sites version 5 succeeded at `https://dantes-roleplay-dnd2024-table.dantecavallin.chatgpt.site` using environment revision 2. |
| Access policy | Owner role, custom access, exactly one allowed account, zero external visitors, and zero workspace or tenant groups. |

No catalog validation, parent .NET suite, or MCP protocol walk was required because no catalog,
C#, MCP surface, dependency registration, or live database changed.

## Deliberate exclusions

- live SQLite/catalog/world/campaign/character/encounter/rules transport;
- runtime editing, persistence, world/campaign selection, or a second state store;
- map travel/reachability logic, route lines, fog simulation, tactical play, or distance claims;
- map/location asset association schemas or authoritative world visual records;
- World History, People & Creatures, Holdings, character dossiers, or detailed rules browsing;
- public sharing, app-owned sign-in, browser-selected identity, and seat-management UI; and
- game-state mutation, LLM calls, and D&D calculations.

## Rollback

Sites version 4 remains the source/deployment rollback boundary. Removing the secret email
allowlist makes all authenticated visitors Player; no database, catalog, rule, schema, campaign,
world, or character state requires reversal.

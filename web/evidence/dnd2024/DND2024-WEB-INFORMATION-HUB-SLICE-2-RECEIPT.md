# DND2024 web information hub Slice 2 receipt — server-issued audience envelope

Status: **accepted 2026-08-28**

Implementation document: `DND2024-WEB-INFORMATION-HUB-SLICE-2-IMPLEMENTATION.md`

Dependency tree/leaf: `DND2024-WEB-INFORMATION-HUB-DEPENDENCY-TREE.md`, Leaf 2

Published source revision: `3db1daad5e310f315f7eeac403dbfd059ca127c9`

## Delivered boundary

- Added one private no-store `GET /api/hub` read adapter whose only optional browser input is the
  requested `dm` or `player` perspective.
- Bound ambient identity to the Sites dispatcher-provided authenticated-user ID. The exact current
  owner principal is stored as a secret production allowlist value; it is not present in source,
  client assets, responses, or this receipt.
- Added a pure non-escalating audience policy. The allowlisted DM can read DM or Player preview;
  every other authenticated principal receives Player even when requesting DM.
- Moved the full fixture source and all DM canary text behind server-only modules. A closed projector
  constructs Player locations from allowed keys and adds the DM field only to DM responses.
- Added shared safe response types and updated the existing component tree to consume one envelope.
  The client imports no full fixture source, audience policy, or server projector.
- Perspective refreshes preserve the active tab, World subsection, query, and valid selection. A
  failed refresh retains the last authorized envelope and reports a friendly retryable notice.
- Added a private unavailable state for missing production identity. Local DM/Player behavior is
  enabled only by an explicit development-host setting ignored by source control and absent from
  the production deployment.

This remains a fixture-backed information surface. The audience boundary is real; the fixture data
is not authoritative game state.

## Evidence

| Check | Result |
| --- | --- |
| Focused web checks | Passed: 11 tests, 0 failures across component state, seat policy, non-escalation, DM preview, closed input, and secret exclusion. |
| Full prototype suite | Passed: 67 tests, 0 failures. This also closes Slice 1's earlier unrelated acceptance exception. |
| Production build | Passed with dynamic `/` and private `GET /api/hub` routes. |
| Local DM/Player route walk | Root, Player, and DM returned HTTP 200; the read route returned `private, no-store`; Player contained no DM field while DM contained the authorized fixture field. |
| Player initial markup | HTTP 200 with zero secret-canary matches and no DM field. |
| Client asset boundary | Zero secret-canary matches and no server-module imports in emitted client assets. |
| Private deployment | Sites version 4 succeeded at `https://dantes-roleplay-dnd2024-table.dantecavallin.chatgpt.site` with environment revision 1. |
| Access policy | Owner role, custom access, exactly one allowed account, zero external visitors, and zero groups. |

No catalog validation, parent .NET suite, or MCP protocol walk was required because no catalog, C#,
MCP surface, dependency registration, or live database changed.

## Deliberate exclusions

- live SQLite/catalog/world/campaign/character/encounter/rules transport;
- a public/share access change, app-owned sign-in, seat editor, or browser-selected identity;
- map, history, people/creatures, holdings, visual-reference, detailed character, or rules-reference
  leaves;
- game-state persistence, mutation, LLM calls, and D&D calculations; and
- Leaf 3's authoritative World bridge and audience-safe location projection.

## Rollback

The prior owner-only Sites version remains the rollback boundary. Removing the production DM
allowlist value makes every authenticated visitor Player without changing source or game state. No
database, catalog, rule, schema, or campaign/world record requires reversal.

## Identity configuration correction — 2026-08-28

The original deployment configured the Site access-policy account ID as
`DND2024_DM_USER_IDS`. Sites authenticated-user IDs are scoped to an individual Site, so that
account-level ID did not match the dispatcher header and the owner was incorrectly limited to
Player. Slice 3 replaces that deployment value with the owner's exact trusted authenticated email
in a secret server allowlist while retaining support for genuine Site-scoped IDs. The
non-escalation and closed-envelope guarantees above are unchanged; the sentence claiming the
original value was the exact current owner principal is superseded by this correction.

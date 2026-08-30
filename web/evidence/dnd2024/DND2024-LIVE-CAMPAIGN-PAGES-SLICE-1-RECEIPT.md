# DND2024 live Campaign pages Slice 1 receipt — chapter and arc projection

Status: **accepted 2026-08-30**

Implementation document: `DND2024-LIVE-CAMPAIGN-PAGES-SLICE-1-IMPLEMENTATION.md`

Dependency tree: `ruleset/dnd2024/DND2024-PROTOTYPE-SERVER-INTEGRATION-DEPENDENCY-TREE.md`

## Delivered

- The server-side companion adapter now reads bounded chapter and arc records only from the exact
  host-authorized live campaign.
- The Campaign overview shows the live campaign status, active chapter **Brackenford Arrivals**,
  its party question, active arc **The Waking Depths**, and its party stake.
- Open Threads now contains the authored active chapter question and arc stake. Existing campaign
  root party goals continue to populate Quests.
- Closed chapters with authored closing summaries project into Adventure Log; resolved or
  abandoned arcs with authored closing summaries project into Outcomes.
- Player and DM-as-Player payloads omit `gmContext`. The current live records contain no GM context,
  so DM and Player correctly see the same two party-facing threads.
- Added a reusable Campaign empty-state component. Adventure Log, Places Visited, Outcomes, Quests,
  Open Threads, and Clues now distinguish an actually empty live owner from a filter with no
  matches and explain what authoritative record would populate the page.

## Evidence

- Focused Campaign/server/UI checks: 41 passing, 0 failing.
- Full prototype suite: 146 passing, 0 failing.
- Production Site build completed successfully.
- Live local DM and Player envelopes both returned Thalorien, The Waystone at Brackenford,
  Brackenford Arrivals, The Waking Depths, two quests, and two open threads. Player JSON contained
  no DM-only Campaign fields.
- `http://localhost:6217/ui/dnd2024-play` returned 200 and continues to mount the React prototype at
  the expected local runtime.

## Deliberate exclusions

No campaign state, catalog record, schema, migration, public route, or permanent ID changed. The
current live campaign has no sessions/recaps, explicit visit records, terminal arcs, or
campaign-owned clues, so Adventure Log, Places Visited, Outcomes, and Clues remain truthful empty
states. Nothing is inferred from location, map use, prose, or World knowledge.

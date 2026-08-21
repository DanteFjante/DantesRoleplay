# World and lore roadmap

Status: **Features W1–W15 and W17 verified; W16 has implemented slices awaiting acceptance**
Last reviewed: 2026-08-21

## Outcome and ownership

World owns setting truth: roots, locations, containment, adjacency, time, travel, factions, NPC
motives, conditions, facts, rumours, secrets, clues, and their bounded projections. Campaigns,
quests, sessions, and storytelling reference these owners; they do not copy World state.

Rules remain:

- authoritative topology is containment/relationships, never map coordinates;
- knowledge visibility is descriptive until a real audience-policy owner enforces it;
- travel mechanics validate current state and update location/time atomically;
- schedulers may propose work but only governed mechanics change state;
- game-specific world behavior stays in catalog JavaScript, not C# branches;
- one fixed small world is sufficient to prove the system before generated content.

## Verified feature index

Completed dependency-plan prose has been removed where receipts preserve the implementation proof.

| Feature | Capability | Durable evidence |
| --- | --- | --- |
| W1 | World root, region, three locations, containment, adjacency | [receipt](world/feature-01/WORLD-FEATURE-01-RECEIPT.md) |
| W2 | Governed one-hop movement over authoritative adjacency | [Slice 2 receipt](world/feature-02/WORLD-FEATURE-02-SLICE-2-RECEIPT.md) |
| W3 | Factions, recurring NPC motives, agenda transition | [Slice 2 receipt](world/feature-03/WORLD-FEATURE-03-SLICE-2-RECEIPT.md) |
| W4 | Facts, rumours, secrets, clues, provenance, reveal/confirm | [Slice 2 receipt](world/feature-04/WORLD-FEATURE-04-SLICE-2-RECEIPT.md) |
| W5 | Root-owned monotonic world clock | [Slice 2 receipt](world/feature-05/WORLD-FEATURE-05-SLICE-2-RECEIPT.md) |
| W6 | Agenda-triggered clue reveal through event reaction | [implementation receipt](world/feature-06/WORLD-FEATURE-06-IMPLEMENTATION-RECEIPT.md) |
| W7 | Bounded trusted-host world/location/faction/knowledge reads | [implementation receipt](world/feature-07/WORLD-FEATURE-07-IMPLEMENTATION-RECEIPT.md) |
| W8 | Named directed on-foot routes with atomic time/location change | [implementation receipt](world/feature-08/WORLD-FEATURE-08-IMPLEMENTATION-RECEIPT.md) |
| W9 | Trusted-host map layout without coordinate authority | [implementation receipt](world/feature-09/WORLD-FEATURE-09-IMPLEMENTATION-RECEIPT.md) |
| W10 | Clock-driven route condition/closure reconciliation | [implementation receipt](world/feature-10/WORLD-FEATURE-10-IMPLEMENTATION-RECEIPT.md) |
| W11 | Manual faction fronts and explicit territorial control | [implementation receipt](world/feature-11/WORLD-FEATURE-11-IMPLEMENTATION-RECEIPT.md) |
| W12 | Generic ground conveyance and vehicle-derived travel time | [implementation receipt](world/feature-12/WORLD-FEATURE-12-IMPLEMENTATION-RECEIPT.md) |
| W13 | Generic aerial route and rider/conveyance co-travel | [implementation receipt](world/feature-13/WORLD-FEATURE-13-IMPLEMENTATION-RECEIPT.md) |
| W14 | Read-only distant on-foot itinerary with per-leg revalidation | [implementation receipt](world/feature-14/WORLD-FEATURE-14-IMPLEMENTATION-RECEIPT.md) |
| W15 | Fixed portal relocation without route/time side effects | [Slice 1 receipt](world/feature-15/WORLD-FEATURE-15-SLICE-1-RECEIPT.md) |
| W17 | Bounded small-world composition for campaign creation | [Slice 1 receipt](world/feature-17/WORLD-FEATURE-17-SLICE-1-RECEIPT.md) |

Catalog records, tests, and these receipts own accepted semantics. Do not reconstruct deleted plans
from memory or treat older cross-plan references as missing authority.

## Active World work

### W16 — mode-aware distant itinerary

[The active plan](world/feature-16/WORLD-FEATURE-16-MODE-AWARE-ITINERARY-PLAN.md) combines only
explicitly available on-foot, ground, aerial, and fixed-portal legs. It must re-plan after every
individually validated/audited leg; it never batches an itinerary into one unchecked mutation.

Slices 1–2 have receipts. Run its focused checks, disposable catalog validation, and full-suite
acceptance against one stable worktree before changing this roadmap to verified.

## Next feature rule

Add a new World feature only for a player-visible capability not already owned above. Before
implementation:

1. search catalog records, code, tests, and receipts for the existing owner;
2. define one outcome and explicit non-goals;
3. expand dependencies to one lowest implementable slice;
4. confirm permanent IDs, schema meaning, public surface, and cross-owner semantics;
5. implement through catalog contracts/JavaScript and generic engine paths;
6. validate the catalog, run acceptance tests, write a short receipt, and stop.

## Deferred

- Procedural or model-generated worlds before reviewed fixed composition has played evidence.
- Player-safe knowledge views before authenticated audience policy.
- Rendering, pathfinding, terrain geometry, weather simulation, economies, and autonomous calendars
  without separately approved owners.
- Copying World truth into campaign/session/story prose for convenience.

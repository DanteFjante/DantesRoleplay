# World Feature 14 Slice 1 receipt — deterministic on-foot itinerary query

**Status:** Slice 1 verified; Feature 14 awaits Slice 2 and feature acceptance  
**Date:** 2026-08-20  
**Roadmap:** [World and lore](../../WORLD_AND_LORE_PLAN.md)

## Delivered

- Public trusted-GM `query(kind: "journey-plan")` with only `worldId`, `travellerId`, and
  `destinationId` inputs.
- A read-only planner that derives origin from containment and returns shortest active/open
  Feature 8 on-foot route legs with a clock revision label.
- Exact empty statuses for already-there, unreachable, blocked, and too-long results. It never
  moves a traveller, reserves time, or treats a plan as authorization.

## Verification

- Focused itinerary and public-query coverage: **6 passed, 0 failed**.
- MCP protocol walk: **4 passed, 0 failed**.
- `roleplay validate catalog`: **154 records valid** with 24 advisory near-duplicate warnings and
  no catalog errors. The validator touched no live data.

## Remaining

Slice 2 proves the handoff: execute only the first on-foot leg, re-plan from actual containment,
and stop without a second movement when the fresh plan becomes blocked.

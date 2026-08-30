# D&D 2024 local table DM seat Slice 1 receipt — server-authorized companion view

Status: **implementation complete; feature acceptance pending 2026-08-30**

## Delivered boundary

- The loopback-only local table configuration can select the existing generic Game Master audience
  role without an actor ID. The configured application and campaign remain server-owned inputs.
- `/api/audience-context` returns the bound D&D 2024 application, state space, campaign, and
  `game-master` role for the configured local Game Master seat. It accepts no browser-selected
  identity, role, application, campaign, or actor.
- The canonical server-hosted React companion maps that verified role to its existing DM
  presentation, uses the UI-only `Dungeon Master` label, and does not read fabricated character
  state for the seat.
- The World tab continues to use the live authorized campaign, World directory, map, knowledge,
  people, faction, and holding projections. Player preview continues to fail closed instead of
  reusing Game Master-only records.
- Actor-seat participation checks and the existing unavailable/denied responses remain unchanged.

## Verification evidence

- Focused audience and context tests passed: **11 passed, 0 failed**.
- The D&D 2024 React interface suite passed: **98 passed, 0 failed**.
- The canonical server bundle built successfully with Vite 8.0.13.
- `http://localhost:6217/api/audience-context` returned HTTP 200 with application `dnd2024`, state
  space `dnd2024-main`, campaign `campaign.thalorien.brackenford`, role `game-master`, and no actor
  identifier.
- `http://localhost:6217/ui/dnd2024-play` returned HTTP 200 from the canonical local server-hosted
  page.

## Deliberate exclusions

No remote authorization, browser-selected identity or role, public sharing, database mutation or
migration, D&D rule change, fixture fallback, player-visible reuse of a Game Master response, or
new permanent ID was introduced. Visual browser inspection was not part of this continuation.

## Acceptance gate

The implementation boundary is complete. Updating the feature plan and roadmap to `accepted`
requires the repository's separate feature-acceptance confirmation.

# DND2024 adaptive Current View — Slice 5 receipt

Date: 2026-08-30
Status: **source implementation complete; feature acceptance pending**
Ruleset alignment: **dnd2024-compatible authored campaign presentation**

## Delivered boundary

The catalog now owns the generic `game.core.campaign.scene-affordances` component and
`procedure.campaign.scene-affordances`. The DND2024 web adapter reads the application-qualified
`dnd2024.game.core.campaign.scene-affordances` record only for an exact authoritative current scene.
It independently validates the full location/conversation/encounter selector, bounded unique items,
and audience visibility. Player output includes `party` items only; authorized DM output may include
both `party` and `gm` items. Missing, malformed, stale, oversized, duplicate, or unauthorized input
emits no affordance details or hidden counts.

Current View renders the result as a read-only **Available now** section in Exploration,
Conversation, and Combat. An absent or empty projection uses a friendly empty state. The section has
no button, mechanic identifier, eligibility claim, target/input form, prepare/execute request, or
write behavior.

## Evidence

- Fresh disposable catalog validation: **passed**, 156 records validated (14 mechanics,
  53 procedures, 38 components, 10 event types, 2 subscriptions, and 39 entities). The 27 reported
  near-duplicate findings were advisory; no live database was touched.
- Focused Current View, adapter, and envelope checks: **53/53 passed**.
- Full DND2024 web suite: **137/137 passed**.
- Production server build: **passed**, 1,623 modules transformed.
- Secret/failure checks cover Player omission of GM-only items, exact full-selector comparison,
  duplicate-key rejection, and closed browser-envelope validation.

## Deliberate exclusions

Mechanic discovery and eligibility, D&D action-economy meaning, action or travel execution,
conversation generation, live campaign authoring, database mutation, catalog activation, deployment,
and final feature acceptance remain outside this slice.

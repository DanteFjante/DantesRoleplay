# DND2024 adaptive Current View — Slice 4 implementation

Status: **source implementation complete; acceptance pending**
Ruleset alignment: **dnd2024-compatible presentation composition**
Owner/roadmap: `web/WEB-INTERFACE-ROADMAP.md`, Feature 5
Parent: `web/DND2024-WEB-INFORMATION-HUB-DEPENDENCY-TREE.md`, Current View Leaves 11–12

## Boundary

Keep the already projected current-location context visible when the authoritative scene kind is
Conversation or Combat. Those modes currently replace the Exploration surface and hide the safe
location description and observations even though the same exact current-scene record supplies the
location.

Compose a reusable location-context panel from the existing `WorldLocation` projection. Conversation
shows it beside exact visible participants. Combat shows it beside Initiative and the active turn.
Existing DM-only location context remains DM-only because no new data is requested or transported.

## Owners and exclusions

- `dnd2024.game.core.campaign.current-scene` remains the exact scene selector.
- Existing audience-projected `WorldLocation` data remains the only location presentation input.
- Existing interaction and encounter projections remain authoritative for their respective modes.

This slice adds no game-state meaning, permanent ID, component, schema, relationship, endpoint,
mechanic selection, action availability, write, activation, or deployment. It does not show travel
choices outside Exploration and does not infer scene affordances from prose, turn resources, or the
generic application action control.

## Allowed files

- `src/system/web-interface/dnd2024/src/components/PreviewViews.tsx`
- `src/system/web-interface/dnd2024/src/styles.css`
- one focused source-presentation test
- this implementation document, receipt, roadmap, and parent dependency tree

## Verification and stop

Run the focused Current View presentation check and the production server build. Run the full web
suite and record any independent in-progress failures without changing their owners.

Stop after location context is composed into Conversation and Combat. Declared available actions
remain blocked on an authored owner; no generic action contract is created by presentation code.

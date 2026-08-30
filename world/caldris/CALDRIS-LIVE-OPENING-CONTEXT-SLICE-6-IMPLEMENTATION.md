# Caldris Slice 6 implementation — live opening context

Status: **blocked**
Owner/roadmap: Caldris implementation map
Dependency tree/leaf: Caldris playable opening; authoritative starting scene
Ruleset alignment: **dnd2024-compatible**
Source ID and locator: not applicable; no D&D rule is implemented
Outcome: make the stored Caldris campaign open on its reviewed Bramblebridge starting scene
Exclusions: player-character choices, session lifecycle, visit history, quest transitions, rolls,
rewards, damage, inventory, and played outcomes
Allowed areas: this document/receipt, one additive Caldris runtime manifest, and reviewed live sync
Stop point: both exact components commit, read back, and appear in Current View

## Confirmed decisions

The user's request confirms the reviewed current-scene and scene-affordance records on the existing
campaign. Existing permanent campaign and location IDs are reused. No schema, mechanic, protocol
kind, or D&D rule is introduced.

## Authoritative state and behavior

`campaign.caldris.measure-of-mercy` remains the active campaign. Its current scene points only to
`location.caldris.bramblebridge`, producing Exploration. The matching affordance record presents
bounded party and GM opportunities without claiming eligibility, success, objective progress, or
player action. SQLite remains authority and the identical manifest must pass dry run before commit.

The current host does not expose the accepted campaign-session operations and does not register the
location-visit mechanic. This slice therefore refuses to fabricate an active session or a visit.

The live `dnd2024-main` state space is also bound to an older application activation that does not
include `dnd2024.game.core.campaign.current-scene` or
`dnd2024.game.core.campaign.scene-affordances`. The current upgrade operation deliberately accepts
only empty state spaces. Applying this manifest therefore requires a separately confirmed,
backup-first non-empty application-state migration; bypassing the component registry is forbidden.

## Acceptance

- both referenced entities exist and the two components are absent before the write;
- exact dry run succeeds, followed by the byte-identical commit;
- component readback matches the reviewed values;
- Current View resolves Bramblebridge as Exploration and shows only perspective-appropriate items;
- a minimal receipt records the boundary and retained platform gates.

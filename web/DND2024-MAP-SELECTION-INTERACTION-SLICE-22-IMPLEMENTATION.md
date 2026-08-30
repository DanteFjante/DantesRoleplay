# D&D 2024 map slice 22 implementation — non-sticky detail and click-away selection

Status: **accepted**
Owner/roadmap: `web/WEB-INTERFACE-ROADMAP.md`, Feature 5
Dependency tree/leaf: `web/DND2024-WORLD-TAB-COMPLETION-DEPENDENCY-TREE.md`, map presentation
Ruleset alignment: **ruleset-neutral**
Source ID and locator: **not applicable**; this is presentation interaction only
Outcome: keep the selected-place card in normal page flow and let a pointer press on empty map space clear the selected marker
Exclusions: map coordinates, marker eligibility, map hierarchy, audience policy, data owners, keyboard navigation changes, assets, and gameplay rules
Allowed files/areas: `MapCanvas.tsx`, the selected-place CSS rule, focused presentation tests, this implementation document and receipt, and the republished local page bundle
Stop point: the selected-place card no longer follows scrolling, marker selection still works, empty-map click clears it, focused/full web tests pass, and the behavior is verified on the live page

## Confirmed decisions

- The user's request confirms both interaction changes.
- “Stuck to the page” means normal document flow rather than viewport-sticky positioning.
- A marker click remains a selection action and stops propagation; any other click inside the map canvas clears the selection.
- List-mode selection behavior is unchanged because there is no map background in that mode.

## D&D 5e 2024 alignment

No D&D rule, formula, state transition, authorization decision, or catalog content changes.

## External implementation reference

No Foundry dnd5e reference is relevant to this repository-specific layout and pointer interaction.

## Prerequisite evidence

- The accepted map-atlas correction receipt verifies the React map is the live canonical surface.
- `MapCanvas.tsx` owns marker buttons and map background interaction.
- `.world-map-selection` currently owns the unwanted sticky positioning.

## Runtime artifacts

No permanent ID, schema, route, migration, or live World record is added.

## Authoritative state and closed input

Selection remains the existing parent-owned `selectedFeatureId`. The canvas may request the existing empty-string selection; it supplies no map data or identity.

## Behavior, result, and typed effects

The selected-place panel uses static layout. Clicking a marker stops propagation and selects its exact feature. Clicking elsewhere within the map canvas requests an empty selection. No persisted effect is produced.

## Failure, replay, and rollback contract

Unknown selections continue to normalize through the existing owner. Repeated background clicks are idempotent. Rollback is activation of the prior immutable page revision.

## Implementation sequence

1. Add the canvas clear handler and marker propagation guard.
2. Remove sticky positioning from the selected-place panel.
3. Add focused source-contract tests and run the full web suite.
4. Export, build, publish, and verify the live interaction.

## Acceptance matrix

| Case | Expected result |
| --- | --- |
| Scroll | Selected-place panel moves with normal page content |
| Marker click | Exact marker becomes selected and detail remains visible |
| Empty-map click | Selection clears and instructional empty detail appears |
| Marker bubbling | Marker click does not immediately clear itself |
| List mode | Existing selection behavior remains unchanged |
| Audience | DM and Player use the same presentation interaction over their own projected markers |

## Verification commands

- `npm test`
- `npm run build:server`
- Live browser selection, scroll, and click-away verification

## Completion receipt and exit gate

Record test, publication, rollback, and live interaction evidence in `web/evidence/dnd2024/DND2024-MAP-SELECTION-INTERACTION-SLICE-22-RECEIPT.md`, then stop.

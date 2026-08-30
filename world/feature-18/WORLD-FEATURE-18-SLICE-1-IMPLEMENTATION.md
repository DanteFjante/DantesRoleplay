# World Feature 18 Slice 1 implementation — canonical multi-plane display anchors

Status: **implementation complete; feature acceptance pending 2026-08-30**
Owner/roadmap: `WORLD_AND_LORE_PLAN.md`, W18
Dependency tree/leaf: `WORLD-FEATURE-18-SCOPED-MAP-PLANES-DEPENDENCY-PLAN.md`, B1–B3
Ruleset alignment: **ruleset-neutral**
Source ID and locator: **not applicable**; this slice defines no D&D rule
Outcome: let one existing display-anchor owner place direct child locations on active World,
Region, and settlement planes with uniqueness scoped to each plane.
Exclusions: live Thalorien changes, new entities/IDs/schemas, map visuals or asset bytes, web code,
audience policy, topology changes, chronology, directories, portraits, NPC profiles, geometry,
distance, routes, travel, discovery, and tactical maps.
Allowed files/areas: World spatial/read procedures, focused World map contract tests, W18 planning
documents, roadmap row, completion receipt, and the parent World-tab completion tree.
Stop point: stop after fresh catalog import and focused tests prove the generalized contract and the
existing Region-plane behavior remains compatible.

## Confirmed decisions

- The user's 2026-08-30 World-tab request confirms canonical coordinates at World and City scope.
- `game.core.world.map.anchor` remains the only placement owner; its `x`/`y` schema is unchanged.
- Active roots, Regions, and settlements are map planes. Sites and interiors are not.
- The direct container is the plane; no plane/map/owner ID is stored in the anchor.
- Coordinate uniqueness is enforced among anchored direct child locations of one plane.

## D&D 5e 2024 alignment

No D&D rule, formula, eligibility, timing, outcome, or vocabulary is introduced. The contract is
generic World presentation metadata.

## External implementation reference

No Foundry dnd5e implementation is relevant because this slice changes only generic setting-map
placement semantics.

## Prerequisite evidence

- W1 and `procedure.game.core.world.location` own roots, location kinds, containment, and slots.
- W9 receipts prove the closed normalized anchor schema, Region-plane placement, trusted-GM layout,
  topology isolation, and read-only behavior.
- The accepted map-media receipts prove map visuals and audience variants are separate owners.

## Runtime artifacts

- Revised `procedure.game.core.world.spatial` plane scope and per-plane uniqueness language.
- Revised `procedure.game.core.world.read` map-layout recipe for any valid active plane.
- Focused tests for valid/invalid plane kinds, topology-required child slots, per-plane uniqueness,
  schema compatibility, fresh import, and unchanged W9 fixture behavior.
- No new component, schema, mechanic, event, query kind, migration, or live record.

## Authoritative state and closed input

An anchor remains exactly `{x, y}` with integer values from 0 through 1,000. Its owner is one active
location directly contained by the selected plane. The plane is an active World root or an active
location whose kind is Region or settlement. Existing topology determines the required child slot:
Region children use `region`; all other location kinds use `location`.

## Behavior, result, and typed effects

The direct container supplies the only plane identity. Equal coordinates on different planes are
valid; equal coordinates for two anchored direct children on the same plane are invalid. A trusted-GM
layout read is complete only when every active direct child selected for display has one valid,
unique anchor. This contract produces no effects and changes no topology or travel state.

## Failure, replay, and rollback contract

Reject anchors on roots, inactive children, children outside the selected direct container,
wrong-slot children, children of site/interior planes, malformed/out-of-range coordinates, and
duplicates within one plane. Missing or invalid placement yields no partial layout. Reads are
side-effect free; repeated fresh imports are deterministic. Catalog failure changes no live state.

## Implementation sequence

1. Generalize the spatial and read procedure language without changing the anchor schema.
2. Replace the narrow plane-scope assertion with focused multi-plane cases and add per-plane
   uniqueness coverage.
3. Run focused W9/W18/map-visual tests and disposable catalog validation.
4. Record the receipt and stop before live Thalorien or website changes.

## Acceptance matrix

| Case | Expected result |
| --- | --- |
| Region child below active root in `region` | valid World-plane anchor |
| place below active Region in required slot | valid Region-plane anchor |
| site/interior below active settlement in `location` | valid City-plane anchor |
| same coordinates on different planes | valid |
| duplicate coordinates on one plane | invalid |
| inactive child/plane, wrong slot, site/interior plane, root anchor | invalid |
| malformed or extra anchor fields | invalid under unchanged schema |
| fresh import and W9 fixture | existing Region behavior remains valid |

## Verification commands

- focused `CatalogWorldFeature9Tests|CatalogWorldMapVisualTests|CatalogWorldFeature18Tests`
- `roleplay validate catalog`
- full suite only at feature acceptance

## Completion receipt and exit gate

Write `WORLD-FEATURE-18-SLICE-1-RECEIPT.md`, update the dependency/roadmap status once, and stop
before live data, map assets, audience projections, chronology, media, NPC profiles, or web edits.

Implementation evidence is recorded in `WORLD-FEATURE-18-SLICE-1-RECEIPT.md`.

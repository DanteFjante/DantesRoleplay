# Feature 20 Slice 2 amendment — roster placement admission

Status: **Implemented and verified; acceptance evidence is in `FEATURE-20-SLICE-2-RECEIPT.md`.**
Last updated: 2026-08-21

## Why this amendment exists

The attempted Slice 2 placement writer can read the encounter's contained participant identities,
but that projection does not expose each participant's Size and position component data. Its direct
collision check therefore cannot be accepted. Existing mechanic child fan-out already supports one
effect-free read per contained participant; the missing leaf is a closed participant tactical
snapshot reader and its declared fan-out into placement admission. This is not E6 dependent-data
composition: no child result becomes another child's input and no child proposes an effect.

## Target capability

Before a creature placement is recorded or corrected, the system proves from authoritative roster
snapshots that the Size-derived footprint is in bounds, avoids blocked terrain, and does not overlap
any other placed participant in the same encounter.

## Ownership and boundary

- `dnd2024.creature-size` remains the sole Size authority; footprint units are derived, never
  stored.
- `dnd2024.encounter-position` remains creature-owned placement state; absence means unplaced.
- `dnd2024.encounter-space` remains encounter-owned bounded terrain state; encounter containment
  remains the authoritative roster.
- The new read mechanic owns only a frozen diagnostic projection of one participant's existing
  Size/position state. It creates no map, placement, movement, action, effect, or event.
- This slice does not add reach behavior, attack admission, path movement, difficult-terrain
  movement cost, cover, sight, or any generic database query facility.

## Dependency graph

```text
safe placement admission
├─ encounter containment roster                         [implemented: Features 11–12]
├─ Size and placement components                         [implemented: Slice 2]
├─ effect-free participant tactical snapshot             [implemented: this slice]
├─ one child snapshot per contained participant          [implemented fan-out capability]
└─ footprint/terrain/collision writer validation          [implemented: Slice 2]
```

## Slice 2A — participant tactical snapshot and collision admission

### Runtime artifacts

- New `mechanic.dnd2024.encounter-participant-tactical-state.read` in
  `ruleset.dnd2024.core.tactical.space`.
- Revision of `mechanic.dnd2024.encounter-position.write` requirements to declare one
  `forEachContentsOf: encounter` child binding to that reader.
- Revision of the Feature 20 focused tests. No new component, entity, event, subscription,
  migration, or public generic composition capability.

### Data and result contract

The reader accepts exactly `{}` and one `participant` role. It returns one closed diagnostic:

```text
{ test, participantId, sizePresent, sizeValid, size, positionPresent, positionValid, position }
```

`size` and `position` are either complete canonical values or `null`; missing/malformed/invalid
state is explicit and never defaulted. The reader has zero effects and no random call.

The placement writer requires exactly one child diagnostic for each encounter-contained participant,
with no duplicate/missing/foreign IDs. It uses the subject's valid Size and each other participant's
valid same-encounter position plus Size to derive rectangular half-square footprints. A malformed
or invalid other participant state rejects placement unchanged rather than treating it as absent.
An absent other position is unplaced and does not collide. The writer alone proposes exactly one
component add/set after all map, roster, bounds, blocked-cell, and collision checks succeed.

### Required algorithm

1. Validate encounter space and direct participant roster.
2. Parse and bijectively match child diagnostics to that roster.
3. Validate subject Size, requested anchor, and Size-derived in-bounds footprint.
4. Reject blocked-cell overlap.
5. For every other participant: reject invalid present Size/position; skip only a genuinely absent
   position; require its position to name this encounter; reject overlapping occupied rectangles.
6. Apply existing record/correct semantics and propose one placement component effect.

### Acceptance matrix

| Case | Exact assertion |
| --- | --- |
| Adjacent Medium creatures | Anchors `(0,0)` and `(4,0)` yield a 5-foot gap; placement succeeds and reach evidence is effect-free. |
| Collision | Correcting the second Medium creature to `(0,0)` rejects with the stored position byte-identical. |
| Size boundaries | Tiny through Gargantuan footprints use 1/2/4/6/8 half-square units; each exact map edge succeeds and one unit beyond rejects. |
| Terrain | Any footprint overlap with a blocked five-foot cell rejects; Difficult Terrain is admitted but has no cost in this slice. |
| Roster evidence | Missing, duplicate, foreign, malformed, or invalid child diagnostics reject before an effect; an unplaced participant does not collide. |
| Isolation | Placement/read/reach create no turn-budget, action, attack, HP, condition, movement, event, or audit-success state on rejection; read/reach have zero effects. |
| Replay | Identical roster/map/state/input/seed returns byte-identical diagnostics and one identical proposed placement effect. |

### Exit gate

Feature 20 Slice 2 may be accepted only after collision and all Size/boundary cases pass through
the child-reader path, catalog validation and focused tests pass, and a receipt records the
evidence. Remove or replace the unproven direct-contained-component check; do not proceed to
Slice 3 attacks or Slice 4 movement.

## Plan-change rule

Revise again if fan-out child diagnostics cannot be bijectively associated with contained
participants, if their projections expose uncommitted state, or if collision requires a generic
query/derived-value binding. Do not bypass a failure by accepting caller occupancy, copying Size
into position, or allowing overlapping placements.

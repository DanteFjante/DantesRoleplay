# Feature 20 dependency plan — tactical position and movement

Status: **Slices 1–4 verified. Slice 4 is governed by FEATURE-20-SLICE-4-MOVEMENT-PLAN.md and recorded in FEATURE-20-SLICE-4-MOVEMENT-RECEIPT.md.**
Last updated: 2026-08-21

## Execution rule

This plan governed the verified Slice 1 repository implementation recorded in
`FEATURE-20-SLICE-1-RECEIPT.md`. Later slices remain prospective under `AGENTS.md` and the Terra
planning guide: reread their current contracts, establish a clean catalog/database baseline,
implement one slice, validate the disposable catalog, record a receipt, and stop.

## Target capability

An encounter can place sized creatures on a bounded five-foot tactical grid, derive base movement and melee reach from authoritative state, and later move an active creature through a validated path while charging the correct movement allowance.

### Included

- A non-rendered square grid: five-foot squares and integer 2.5-foot anchor units for all SRD Size categories.
- Closed base normal/special Speed state, encounter terrain, placement, distance/reach readout, and normal voluntary path movement.
- Difficult-terrain cost, occupied-space admission, route/corner checks, and an atomic pre-departure boundary for Feature 19.
- Replacing Feature 12's temporary recorded movement maximum with Speed-derived remaining movement.

### Excluded

- Rendering, pathfinding, elevation/3D terrain, cover, ranged range, flanking, and sight. Feature 21 owns cover/ranged geometry; Feature 34 owns sight; both are required for Feature 19.
- Disengage, Dash, crawling/climbing/flying/swimming/burrowing restrictions, jumping, forced movement, teleportation, mounts, and spell/feature movement.
- Unarmed attacks, weapon properties such as Reach, equipment, damage, conditions, and death. Features 22, 25, 9, and 13–17 own them.
- Opportunity-attack choice/resolution. Feature 19 consumes this feature's movement boundary only after its own composition and visibility inputs exist.

## Official source basis

The registered source is `source.dnd2024.srd-5.2.1`: *System Reference Document 5.2.1* (Wizards of the Coast LLC, 2025-05-01, CC-BY-4.0), [Playing on a Grid and Movement and Position, PDF pp. 12–14](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf), and [Rules Glossary > Difficult Terrain, Size, Speed, PDF pp. 180 and 187](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf).

- Grid squares are five feet; adjacent diagonal/orthogonal squares cost one ordinary square, and grid range uses the shortest route between occupied spaces.
- A creature deducts each part of its movement from selected Speed and may split movement around its actions.
- Difficult Terrain costs one extra foot per foot and does not stack.
- Size determines combat space: Tiny 2.5 feet, Small/Medium 5, Large 10, Huge 15, Gargantuan 20.
- Base melee reach is five feet; a target must be in reach.

## Planning inventory and overlap result

| Inquiry | Evidence and decision |
| --- | --- |
| Size | Feature 23 Slice 6 verifies `dnd2024.creature-size` and its six values. It stores no dimension, position, Speed, or reach. Feature 20 derives footprint once. |
| Turn budget | Feature 12 now records only `movementRemainingFeet`; Slice 1 retired its temporary maximum scaffold. Encounter start/advance derive refresh from `dnd2024.speed.walkFeet`. |
| Movement spend | `mechanic.dnd2024.turn-budget.spend` is the sole normal spend path. It accepts caller feet and knows no path, terrain, or coordinates. |
| Encounter roster | Features 11–12 own initiative and the direct contained participant roster. Terrain must not be added as encounter contents because that violates this roster invariant. |
| Attack resolver | Feature 8's `mechanic.dnd2024.weapon-attack` is effect-free and has no range/reach. Feature 20 adds a precondition parent; it never copies attack arithmetic. |
| Weapon data | `dnd2024.weapon-profile` has no reach/range field. Feature 25 owns weapon properties, so this feature supports recorded base creature reach only. |
| Tactical state | Slice 1 adds base Speed only. Searches still find no D&D coordinates, occupancy, reach, terrain, path, or voluntary movement model. |
| Composition | Children can inherit/static/select caller top-level input and run before parent source. A path parent cannot bind its derived cost to the budget-spend child. |

## Recursive dependency analysis

~~~text
Feature 20: tactical position and movement
├─ SRD grid, Size, Speed, difficult terrain, reach basis       [implemented source basis]
├─ creature Size                                                [implemented: Feature 23 Slice 6]
├─ turn lifecycle and movement allowance                        [implemented: Features 11–12]
├─ atomic actions/events/audit                                  [implemented: kernel/E1]
├─ base Speed + Feature-12 scaffold retirement                  [implemented: Slice 1]
├─ encounter grid, terrain, sized placement, reach reader       [implemented: Slice 2]
├─ legal tactical melee parent                                  [implemented: Slice 3]
├─ voluntary path move + exact budget cost                      [implemented: Slice 4]
│  ├─ validated path cost                                      [implemented: Slice 4]
│  └─ parent-derived cost -> budget spender                     [implemented: E6]
└─ opportunity candidates                                       [blocked: Feature 19 + Features 21/34]
~~~

Slice 1 is accepted: `dnd2024.speed` is the base-Speed authority and Feature 12's duplicated
maximum is retired. Slice 2 is accepted: the bounded map, Size-derived collision-safe placement,
and effect-free base-reach evidence are recorded in `FEATURE-20-SLICE-2-RECEIPT.md`. Slice 4 is
accepted: closed voluntary paths derive the only budget input and aggregate the existing budget
spender with the position update atomically; see `FEATURE-20-SLICE-4-MOVEMENT-RECEIPT.md`.

## Dependency and ownership decisions

1. **Speed is persistent; budget is per turn.** Proposed `dnd2024.speed` owns base speeds. `dnd2024.turn-budget` owns only current remaining movement and refreshes it at a turn start; no second maximum persists.
2. **The encounter owns the map.** Proposed `dnd2024.encounter-space` carries bounded dimensions and canonical sparse blocked/difficult five-foot squares. It is not a terrain roster, world location, or procedural map.
3. **The creature owns its placement.** Proposed `dnd2024.encounter-position` names an encounter and integer anchor. Its footprint derives from `dnd2024.creature-size`: Tiny 1, Small/Medium 2, Large 4, Huge 6, Gargantuan 8 half-square units. Absence means unplaced, never origin.
4. **Reach is derived at attack time.** Proposed `dnd2024.melee-reach` supplies source-backed base creature reach. Grid distance comes from map/placements; weapon-specific Reach stays Feature 25.
5. **Path cost is derived.** The move input contains an ordered path, never `feet`, an encounter/actor id, terrain verdict, final coordinate, or effects. It derives cost and passes it to the one Feature-12 spend owner only through a confirmed platform binding.
6. **Visibility is not position.** Feature 21 supplies geometry and Feature 34 supplies “can see” for Feature 19. Being co-located on an unobstructed map is not a substitute.

## Confirmation boundary

| Decision | Required confirmation |
| --- | --- |
| Speed | Exact `dnd2024.speed` fields, writer, source attribution, and future class/species/condition revision path. |
| Budget migration | Revised `dnd2024.turn-budget` and Feature 11 start/advance refresh from Speed without a duplicated maximum. |
| Map/position | Bounds, canonical terrain ordering, 2.5-foot anchors, footprint/collision/end-space rules, and normal placement lifecycle. |
| Base reach | `dnd2024.melee-reach` state and relationship to later weapon/Unarmed Strike exceptions. |
| Derived binding | Platform decision permitting a parent-derived closed value into one named child while retaining deterministic seeds, rollback, limits, and frozen evidence. |
| Reaction handoff | Feature 19/21/34 agreement on pre-departure transition id, candidate order, geometry, and visibility ownership. |

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Base Speed and budget migration | Existing Feature 11/12 contracts re-read. | A turn refreshes remaining movement from valid base walk Speed; no duplicated maximum remains. |
| 2 | Map, terrain, sized placement, reach reader | Slice 1 and ids confirmed. | Safe placements and exact effect-free grid distance/base-reach evidence. |
| 3 | Tactical melee precondition parent | Slice 2 and Feature 8 re-read. | Legal base-reach melee attacks compose Feature 8; out-of-reach attempts fail before rolling. |
| 4 | Voluntary path movement | Slice 2 and derived-input composition verified. | **Verified** — one active creature moves and pays an exact derived cost atomically; see `FEATURE-20-SLICE-4-MOVEMENT-RECEIPT.md`. |
| 5 | Difficult terrain and occupied-space movement | Slice 4 and Feature 13 condition input where needed. | Cost/pass-through/end-space rules are exact with no special movement implied. |
| 6 | Feature 19 handoff | Slice 4, Feature 19 schema, Feature 21 geometry, Feature 34 sight, and event composition verified. | Ordered candidates occur before position finalises; Feature 20 spends no Reaction or attack. |

## Slice 1 — base Speed and budget migration

### Runtime artifacts

- New `procedure.mechanic.dnd2024.speed`, `dnd2024.speed` definition/schema, and `mechanic.dnd2024.speed.write` / `.read`.
- Revisions to `dnd2024.turn-budget`, its writer/read/spend mechanics, Feature 11 start/advance mechanics, and their governing contracts.
- Migrated Feature 10 fixtures.

### Data/input and state

`dnd2024.speed` has a closed canonical profile: `walkFeet` (5–1000, multiple of five), `burrowFeet`, `climbFeet`, `flyFeet`, and `swimFeet` (each 0–1000, multiple of five), then fixed `sourceRef`. Zero special Speed means absent. It stores no remaining movement, condition effect, terrain, position, reach, or history.

Its writer accepts only `record` or `correct` plus the five speeds. Record requires absence; correct requires complete valid existing state. The turn-budget migration leaves four availability Booleans plus `movementRemainingFeet`. Turn start/advance reads valid Speed and sets the newly active participant's remaining movement to walk Speed; the spend mechanic validates remaining against Speed but still does not decide a movement cost.

### Behavior, acceptance, and exit gate

The Speed writer proposes one add/set effect; migration revisions preserve Feature 11's turn transition and touch only the active budget. It uses no randomness and creates no map, position, terrain, or movement action.

| Case | Assertion |
| --- | --- |
| Valid profile | Record/correct produces one canonical source-backed component. |
| Boundaries | Walk 5/30/1000 and specials 0/5/1000 pass; zero walk, 1005, fractions, extra fields, null, wrong case, supplied provenance, and duplicate record fail unchanged. |
| Refresh | Start/advance set remaining to each active participant's walk Speed, leaving prior participant state unchanged. |
| Spend | Exact remaining succeeds; overspend, off-turn movement, missing/corrupt Speed/budget, and remaining above Speed fail with zero effects. |
| Differential/replay | Walk 25 versus 35 yields exactly those refresh values; equivalent runs are byte-identical. |
| Compatibility | Action/bonus/reaction/free interaction and Feature 11 roster/turn invariants remain exact. |
| Routing/cleanup | Speed phrases select only its writer/read; movement/attack/turn phrases do not. Fixtures return to baseline. |

Exit only when Speed is the sole source of refreshed movement allowance, the scaffold maximum is gone, catalog validation and focused/full repository tests pass, and a receipt records the evidence. Stop before map/placement work.

## Slice 2 — encounter space, placement, and reach reader

Create `dnd2024.encounter-space` and `dnd2024.encounter-position` with safe record/correct/read paths. An encounter space has bounded five-foot-square dimensions and sorted sparse blocked/difficult cells without duplicates/overlap. A placement names its encounter and anchor. It requires roster membership, valid Size-derived in-bounds footprint, map, and no overlap; it spends no movement.

An effect-free reach reader validates both placements share the encounter, computes shortest distance between adjacent occupied squares, and compares it with `dnd2024.melee-reach`. Prove Tiny through Gargantuan, boundaries, blocked/overlap/out-of-bounds, missing/corrupt map/Size/reach/placement, exact in/out reach, zero effects, replay, and cleanup. Stop before attacks/movement.

## Slice 3 — tactical melee precondition parent

The player-facing parent requires valid map/placements/reach and `kind: "melee"`, rejects out-of-reach before calling the Feature-8 attack child, then returns frozen Feature-8 evidence. It spends no Action, decides no equipment, and creates no damage/position change. Prove no D20 on precondition failure, reach boundaries, wrong weapon kind, unchanged state, and routing separation between tactical attack and diagnostic resolver.

## Slice 4 — voluntary path movement

The input is a closed ordered list of adjacent grid steps. It derives every entered square, blocked/corner result, and total cost; caller `feet` or a final coordinate is forbidden. After the platform confirms derived-cost child binding, it invokes `mechanic.dnd2024.turn-budget.spend` exactly once and changes position in the same root transaction. Any failure rolls back both. It exposes a pre-departure transition identity but creates no opportunity candidate or Reaction.

Prove zero/one/many step paths, exact/one-short remaining movement, split movement around a separate action, blocked/out-of-bounds/corner rejection, input closure, atomic rollback, exact deltas, replay, routing, and restoration. Stop before difficult terrain, pass-through, or reactions.

## Slice 5 — difficult terrain and occupied spaces

Extend only movement cost and pass-through. Difficult Terrain doubles each affected five-foot step once. Passing through is limited to the SRD ally, Incapacitated, Tiny, or two-size-difference cases; a voluntary move cannot finish in another creature's space. The condition input comes from Feature 13's effective state rather than copied lists. Test differential terrain cost, permitted/refused pass-through, mult-square footprints, and no partial move. Stop before special movement modes.

## Slice 6 — Feature 19 handoff

After Feature 19's trigger schema, Feature 21 geometry, Feature 34 sight, and event-composition contracts are verified, emit ordered per-reactor candidates while the mover is still at its origin. Feature 20 owns spatial eligibility/timing only: no Reaction spend, attack, Disengage, teleport, or forced-movement rules appear here. Verify ordering, visibility input, atomic rollback, and that no candidate becomes automatic attack behavior.

## Plan-quality audit

- One capability and explicit non-goals: yes.
- Concrete official source locators: yes.
- Size, budget, roster, attack, map, and composition ownership searched: yes.
- Missing leaves expanded: yes.
- One next slice: yes — Slice 5, after its permanent terrain and pass-through boundary is confirmed.
- Closed state/input, effects, atomicity, replay, routing, and cleanup are specified.
- No runtime artifact is created by this planning pass.

## Plan-change rule

Stop and revise if live contracts differ, the map model cannot represent the Size table, another owner supplies Speed/reach/visibility, or composition cannot bind a derived cost safely. Do not retain two movement maxima, infer an absent position as origin, accept caller distance/terrain results, or write a second budget-spend algorithm.

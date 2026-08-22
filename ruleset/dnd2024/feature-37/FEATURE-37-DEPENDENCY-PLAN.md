# Feature 37 dependency plan — D&D travel pace and elapsed-time integration

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Planned. The pace source and base-Speed prerequisites are verified; runtime remains blocked on generic on-foot route distance and the rest/duration owners.**
Last updated: 2026-08-21

## Execution rule

This is a repository planning artifact. Core world contracts remain authoritative for locations,
routes, conveyances, itineraries, containment, and the world clock; D&D ruleset artifacts may only
consume them through their existing governed actions. A future implementation pass selects one
unblocked slice, creates its reviewed catalog artifacts, runs focused tests and `roleplay validate
catalog`, and stops. It must not import the persistent database merely to develop the feature.

## Target capability

A D&D party can apply an official, source-cited travel-pace rule to a measured world journey, and
elapsed world time can complete only the D&D rests and persistent durations that have separately
declared their start state and expiry rule.

### Included after prerequisites are verified

- A derived D&D travel-pace result from authoritative creature Speed, a measured generic world
  route, and a source-cited D&D pace policy.
- Explicit integration of an accepted world-clock advance with completed D&D rest and duration
  owners.
- Read-only planning and atomic travel execution that continue to use the current world movement
  owner rather than creating a second location, route, clock, or itinerary state.

### Excluded

- Locations, maps, adjacency, generic routes, conveyances, teleport gates, itinerary search, route
  availability, containment location, and root-clock ownership; core world already owns these.
- Rebuilding creature Speed, tactical movement, difficult terrain, reach, or position; Feature 20
  owns them.
- Starting or resolving rests, spending Hit Dice, resource recovery, or rest interruption; Feature
  33 owns those rules.
- Spell/effect duration identity, ending effects, concentration, weather, random encounters,
  foraging, navigation, hex crawling, camping policy, or travel montage narration.
- A caller-selected duration, pace result, distance, route, clock value, or expiry outcome.

## Official source basis and planning blocker

| Needed rule | Evidence found | Planning decision |
| --- | --- | --- |
| Core world journey/time | `procedure.game.core.world.travel`, `.time`, and `.itinerary` are verified repository contracts. They move a traveller/conveyance over validated routes and atomically advance the one root clock. | Consume; do not duplicate. |
| Short/Long Rest time | SRD 5.2.1 describes hour-long Short Rests and 8-hour Long Rests in the introductory rules (PDF p. 16) and directs complete definitions to the Rules Glossary. | Feature 33 must author the complete rest lifecycle before Feature 37 may react to elapsed time. |
| D&D travel pace | The official [D&D Beyond Basic Rules (2024), Playing the Game > Travel Pace](https://www.dndbeyond.com/sources/dnd/br-2024/playing-the-game/) gives Fast, Normal, and Slow rates of 400/300/200 feet per minute (and corresponding hourly/daily rates), plus their check effects and mount rule. | **Source gate resolved.** Before a runtime mechanic cites it, register this authorized source with its stable locator under the reviewed source-registry contract. Do not substitute a generic route's fixed duration for the derived D&D pace result. |
| Persistent duration expiry | No approved persistent-effect identity/expiry owner exists. Feature 32 is planned to own spell duration/effect resolution; Feature 18's concentration plan records the same gap. | **Block.** Feature 37 cannot schedule or end effects before Feature 32 owns their lifecycle. |

The planning pass read the local SRD PDF, the core-world travel/time/itinerary contracts, the
Feature 33 roadmap boundary, and the Feature 18/32 dependency evidence. No runtime artifact,
database record, or game action was created.

## Verified existing dependencies

| Dependency | Evidence and boundary |
| --- | --- |
| World location and containment | `procedure.game.core.world.location` and `WorldStore.MoveAsync`; containment remains current location. |
| One-leg on-foot route travel | `mechanic.game.core.world.route.travel-on-foot` validates route direction/availability and atomically moves the traveller plus root clock. Its input never accepts time. |
| Ground, aerial, and teleport travel | Core travel owns these separately, including their duration derivation or zero-time portal behavior. D&D pace does not override them. |
| Root clock | `game.core.world.clock`, `procedure.game.core.world.time`; one authoritative minute/revision coordinate. |
| Itinerary planning | `query(kind: "journey-plan")` and `query(kind: "itinerary-plan")` are read-only, bounded, and must be revalidated leg by leg. |
| Event/subscription platform | E1 is verified, but a generic component replacement is not permission to create a D&D rest/effect scheduler. |
| Feature 20 movement/Speed | Slice 1 verified `dnd2024.speed`, its closed writer/reader, and turn-budget refresh from walk Speed. |
| Feature 33 rests | Planned. No rest episode, completion criteria, interruption rule, or recovery owner exists. |
| Feature 32 durations | Planned. No persistent D&D effect identity/expiry owner exists. |

## Recursive dependency analysis

```text
Feature 37: D&D pace and elapsed-time integration                         [blocked parent]
├─ core world route/itinerary/clock                                        [implemented]
├─ E1 event/subscription platform                                          [implemented]
├─ official pace source                                                    [resolved: D&D Beyond Basic Rules 2024]
├─ generic measured on-foot route distance                                 [missing core-world owner]
├─ D&D Speed authority                                                     [implemented: Feature 20 Slice 1]
├─ rest lifecycle/completion owner                                         [blocked: Feature 33]
└─ persistent duration identity and expiry owner                           [blocked: Feature 32]
    └─ concentration lifecycle                                             [planned: Feature 18]
```

No numbered implementation slice is a valid lowest leaf today. The next assignment is the
source-and-ownership ratification below, not runtime coding.

## Ownership decisions

1. **World topology and elapsed time stay generic.** A D&D pace consumer reads the existing
   route/clock; it does not add position, distance, duration, route, or clock copies to a creature
   or campaign.
2. **Distance belongs to the generic route owner.** If on-foot pace needs distance, the generic
   world route contract must add a reviewed measured-distance vocabulary itself. A `dnd2024`
   component on one route would make D&D the owner of a world fact and would give generic travel
   two incompatible duration sources.
3. **Speed belongs to Feature 20.** Feature 37 consumes a verified Speed result and may not revive
   Feature 12's temporary `movementMaximumFeet` as travel Speed.
4. **Rests and durations own their own transitions.** Feature 37 may supply an accepted elapsed
   clock boundary to their declared readers/handlers, but it may not grant healing/resources or
   delete/continue effects itself.
5. **Pace is derived, never caller supplied or stored as a mutable travel total.** A selected pace
   policy, Speed, route distance, and applicable world route produce the result. If the official
   source cannot be used, the result is campaign policy and must live outside the D&D SRD ruleset.

## Required decision gates

Before any implementation, all of the following must be resolved and recorded in this plan:

1. Register the official D&D Beyond Basic Rules (2024), *Playing the Game > Travel Pace*, under
   the reviewed source-registry contract before a runtime mechanic cites it. The source supplies
   Fast/Normal/Slow vocabulary, rate, checks, mounts, and its link to terrain constraints.
2. A core-world design decision for measured on-foot route distance. It must name the generic
   owner, schema migration/compatibility behavior for existing duration-only routes, source unit,
   and which journey action derives duration from it. Feature 37 cannot make that change alone.
3. Feature 33 acceptance of rest lifecycle and its exact completion/interruption seam.
4. Feature 32 acceptance of persistent duration identity and expiry seam.

## Prospective slice order after gates

| Order | Slice | Starts only when | Exit gate |
| ---: | --- | --- | --- |
| 0 | Source registry and owner ratification | The registered source, core route-distance decision, Feature 33, and Feature 32 have named owners; no runtime change | This plan has exact source locators, confirmed core-route compatibility decision, and no remaining guessed formula or owner. |
| 1 | D&D pace reader | Slice 0 and Feature 20 verified; core measured route available | A read-only mechanic derives pace/distance/duration from exact Speed and route facts; no caller time/distance/total; source, boundary, corrupt-state, replay, and routing tests pass. |
| 2 | Time-bound rest integration | Slice 1 and Feature 33 verified | A completed/rest-in-progress owner receives only validated elapsed-clock evidence; rest completion/rejection is atomic and Feature 37 owns no recovery effects. |
| 3 | Persistent duration integration | Slice 2 and Feature 32 verified | A duration owner receives validated elapsed-clock evidence and applies its own explicit expiry transition; no polling loop or parallel scheduler. |
| 4 | Cross-system acceptance | Slices 1–3 verified | One route journey demonstrates pace read plus only the appropriate rest/duration consumer transitions; generic world route/clock/itinerary tests remain green; full suite passes. |

## Slice 0 — source and owner ratification

### Required artifacts

Planning-only revisions to this plan, `ROADMAP.md`, and the relevant owning core-world/Feature
20/33/32 plans. It creates no component, mechanic, event, subscription, fixture, or schema.

### Required evidence

- A source-registry record for the official D&D Beyond Basic Rules (2024), *Playing the Game >
  Travel Pace*, with exact locator.
- A written core-world route-distance compatibility decision, including treatment of existing
  duration-only routes and all generic travel modes.
- Accepted Feature 20, 33, and 32 contracts at the consumer seams named above.

### Exit gate

Every later artifact id, formula, input, effect owner, and test expectation is determined without
guessing. Until then, Feature 37 remains blocked and no implementation is authorized.

## Prospective implementation requirements

These requirements bind Slices 1–4 once Slice 0 is complete.

- **Closed inputs:** a pace reader accepts only a selected traveller and/or validated route roles;
  duration consumers never accept minutes, clock revision, route id, rest outcome, or effect id
  when those are derivable from authoritative state.
- **Result shape:** report source ids, source locator, selected pace vocabulary, exact distance and
  elapsed minutes as appropriate, plus a clear `complete`/`bounded` status. Do not return a cached
  journey total.
- **Effects:** read-only pace has zero effects. Route movement remains the core mechanic. A rest or
  duration transition proposes only the effects its own owner governs, in its owner's atomic
  action/chain.
- **Failure:** missing/corrupt Speed, route distance, pace policy, root clock, rest state, or
  duration state fails before effects. A generic duration-only route may remain valid core content
  while being ineligible for D&D pace derivation.
- **Routing:** administrative pace-policy maintenance uses no player-facing phrase. Player travel
  phrases continue to select the core-world travel owner; D&D readers must not capture `travel`,
  `go`, `move`, `journey`, or `rest` phrases owned elsewhere.
- **Acceptance matrix:** each implementation slice must prove source/formula boundaries,
  differential Speed/distance outcomes, closed input, missing/corrupt state, deterministic replay,
  zero or exact owner-specific effects, core-route compatibility, state integrity after refusal,
  and no duplicate clock/location ownership. Slice 4 additionally runs the full suite and
  `git diff --check`.

## Plan-quality audit

- Target, boundaries, core-world ownership, and explicit non-goals: **yes**.
- Existing dependency evidence: **yes**, from repository contracts and roadmap evidence.
- Exact official pace formula/source: **yes** — D&D Beyond Basic Rules (2024), *Playing the Game >
  Travel Pace*; registry admission remains a Slice 0 artifact.
- All missing leaves expanded: **yes** — source registry, route distance, rests, and durations.
- One lowest implementation slice: **none until Slice 0's remaining owner decisions**; this is intentional
  and follows the planning guide's blocker rule.
- Runtime artifacts created by this planning pass: **none**.

## Plan-change rule

Revise this plan before implementation if the chosen pace source changes, Feature 20 defines Speed
without a travel-reader seam, Feature 33/32 choose a non-clock completion model, or core world
adopts a measured-distance model that changes current route compatibility. Do not adapt those
choices inside a D&D mechanic.

---
id: procedure.game.core.world.itinerary
category: game.core.world.travel
name: Plan a mode-aware distant itinerary
governs: commit(kind: "system.interaction-execute") proposing a distant journey over stored travel modes
status: active
createdBy: "seed"
changeNote: "Seeded from bootstrap file."
---

## Description
Defines the trusted, read-only planner for a traveller's distant journey. It reads only the
already-governed on-foot, ground-conveyance, aerial-conveyance, and fixed-portal records. It does
not create a journey or move anyone; each returned leg must still be performed by its existing
one-leg action owner.

## Matches

## Instructions
1. Plan with `worldId`, `travellerId`, and `destinationLocationId`.
   Optionally include `groundConveyanceId` and/or `aerialConveyanceId` only for specifically
   selected, active conveyances that are directly co-located with the traveller at `presence`.
   Origin is always derived from the traveller's actual containment.
2. The closed result has status `ready`, `already-there`, `unreachable`, `blocked`, `too-long`,
   or `unavailable-resource`. A ready result has an opaque `itineraryFingerprint`, a summed
   `estimatedTotalMinutes`, and at most 64 ordered legs. A leg names its mode, endpoints,
   route-or-portal, selected conveyance when any, and estimated minutes.
3. The planner uses only active same-world records that independently satisfy their owning mode's
   read-only prerequisites. On-foot and ground legs require their existing canonical adjacency;
   aerial legs do not. Fixed portals are solely their direct containment origin and exact directed
   root/destination links. Portal legs cost zero minutes.
4. A selected conveyance remains part of the planner state. A ground or aerial leg may be used
   only where that selected conveyance is co-located; travelling with it carries it to that leg's
   destination. Walking or using a portal does not silently carry a conveyance along.
5. Choose least total estimated minutes. Break equal totals by fewer legs, then the ordered mode
   preference `portal`, `on-foot`, `ground`, `air`, then route-or-portal id and destination id.
   Closed on-foot routes remain structural evidence for `blocked`; malformed or irrelevant records
   never become an edge.
6. A plan is advice, not authorization. To execute exactly one current leg, advance it with the
   same request, exact fingerprint, and the leg's `nextLegIndex`. It re-reads the plan, rejects any changed fingerprint/index, invokes only the
   named Feature 8, 12, 13, or 15 action owner, then returns a freshly rebuilt itinerary. Do not
   use direct effects or a distant containment move as a shortcut.

## Constraints
- There is no `itinerary-plan` query kind and no `itinerary-advance` commit kind. An application
  supplies planning and leg execution as mechanics: resolve one with
  `query(kind: "system.interaction-plan")` and run it with
  `commit(kind: "system.interaction-execute")`.
- This query writes no containment, clock, component, relationship, event, reservation, or
  operation-derived world state.
- It never selects a conveyance, invents a route, reverses a directed edge, grants access,
  batches legs, changes time, or recognizes spell/item/network teleportation.
- `itinerary-advance` changes only the normal effects proposed by its one selected owner. A
  failed stale check or owner action changes nothing; it cannot execute a cached later leg.
- Missing, inactive, malformed, wrong-place, or wrong-mode selected conveyances produce
  `unavailable-resource`; a missing topology path is `unreachable`; a path whose on-foot route is
  currently closed is `blocked`.

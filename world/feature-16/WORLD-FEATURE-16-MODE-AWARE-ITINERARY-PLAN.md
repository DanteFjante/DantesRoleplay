# World Feature 16 dependency plan — mode-aware distant itinerary

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Slice 2 implemented; Feature 16 is ready for acceptance verification**  
Last updated: 2026-08-20

## Target capability

A trusted host can ask a marked traveller to reach one named, stored destination that is several
legs away. The planner considers only the explicitly authored, currently usable ways of travelling:
on foot, a co-located ground conveyance, a co-located aerial conveyance, and a fixed portal. It
returns one deterministic ordered itinerary, then executes **one leg at a time** through the
existing owner for that leg. Before every next leg it re-reads current state and re-plans.

It never treats a distant destination as an unchecked teleport. A later ration shortage,
encounter, closure, condition, unavailable conveyance, or new route block can stop the journey at
the actual current location before the next leg. This slice supplies the itinerary boundary and
recheck point; it does not invent those future blockers.

### Included

- A read-only itinerary planner over active, same-world on-foot routes, generic ground routes,
  generic aerial routes, and fixed portals.
- Explicit traveller, destination, and optional selected ground/aerial conveyance references.
- A deterministic result with ordered legs, estimated total minutes, and a terminal status.
- A one-leg execution coordinator that delegates the actual mutation to W8, W12, W13, or W15,
  re-reads state, and either returns the next itinerary or stops.
- Canonical ordering, bounded graph/result sizes, no-path, stale-state, unavailable-resource, and
  per-leg re-plan coverage.

### Excluded

- Batched or automatic completion of every leg; this feature may not bypass an intermediate
  location or its later checks.
- Route creation, free travel, routing around unauthored topology, maps, reservations,
  ownership/permission systems, passenger/cargo assignment, fuel, rations, weather, encounters,
  random tables, combat, notifications, player authorization, spell/item teleportation, or
  movable portal networks.
- Choosing a conveyance on the traveller's behalf. In this first slice a supplied conveyance must
  already be active and co-located; ownership and access rules belong to later character/item work.

## Source and contract basis

| Authority | Decision supplied |
| --- | --- |
| Feature 8 | On-foot route legality and one-leg movement owner. |
| [Feature 12 receipt](../feature-12/WORLD-FEATURE-12-IMPLEMENTATION-RECEIPT.md) | Generic ground-conveyance route and co-travel action owner. |
| [Feature 13 receipt](../feature-13/WORLD-FEATURE-13-IMPLEMENTATION-RECEIPT.md) | Generic aerial-conveyance route and co-travel action owner. |
| Feature 14 | Its on-foot-only itinerary result and per-leg execution pattern; W16 generalizes the selection boundary rather than overwriting W14. |
| Feature 15 | Fixed, explicit portal edge and portal action owner; a portal is never inferred from shared location or text. |
| World time/change/action contracts | Atomic leg mutations, root-clock behavior, replay protection, and read-before-write discipline. |

## Ownership and confirmation boundary

The user confirmed the permanent IDs, selected-conveyance input meaning, cost ordering, and existing
leg-owner invocation boundary on 2026-08-20. W16 may read topology and choose a next leg,
but it does **not** mutate containment or clocks directly. The corresponding mode owner remains
the only writer for that leg.

| Artifact | Proposed meaning |
| --- | --- |
| \`procedure.game.core.world.itinerary\` | Shared read-only planner and one-leg coordination surface. |
| \`mechanic.game.core.world.itinerary.plan\` | Deterministically returns a mode-aware itinerary without writing state. |
| \`mechanic.game.core.world.itinerary.advance-one-leg\` | Validates the next planned leg against fresh state, delegates exactly one mode action, then re-plans. |

No component, relationship, fixture, event type, or migration is proposed in this feature. The
planner derives graph edges exclusively from the verified mode contracts.

### Slice 1 implementation boundary

The existing JavaScript mechanic projection intentionally cannot enumerate arbitrary stored route
entities or invoke a child action after its effects commit: it receives only explicitly declared
role projections and child outputs. Slice 1 therefore implements the confirmed read-only planner
semantics as trusted host `query(kind: "itinerary-plan")`, alongside Feature 14's existing
planner. With the user's 2026-08-20 confirmation, Slice 2 uses
`commit(kind: "itinerary-advance")` as the action-facing coordinator: it validates the exact plan,
delegates the selected one-leg action to its existing owner, and only then performs the fresh read.
The procedure is the durable public contract; no sandboxed mechanic is misrepresented as having
topology or post-commit access it does not possess.

## Closed request and result contract

\`plan\` input is exactly:

~~~text
{
  travellerId: canonical entity id,
  destinationLocationId: canonical entity id,
  groundConveyanceId?: canonical entity id,
  aerialConveyanceId?: canonical entity id
}
~~~

\`advance-one-leg\` accepts the same request plus the exact returned itinerary revision/fingerprint
and next-leg index. It rejects a stale fingerprint instead of executing a guessed replacement.

The result is closed:

~~~text
{
  status: "ready" | "already-there" | "unreachable" | "blocked" | "too-long" | "unavailable-resource",
  itineraryFingerprint?: stable opaque value,
  estimatedTotalMinutes?: non-negative integer,
  legs?: [{
    index: non-negative integer,
    mode: "on-foot" | "ground" | "air" | "portal",
    fromLocationId: canonical entity id,
    toLocationId: canonical entity id,
    routeOrPortalId: canonical entity id,
    conveyanceId?: canonical entity id,
    estimatedMinutes: non-negative integer
  }]
}
~~~

\`ready\` has 1–64 legs; \`already-there\` has zero legs; all other statuses have no legs or estimate.
Portal legs estimate zero minutes. All other leg estimates come from their owning verified mode
contract. Missing/extra keys, wrong-world/inactive entities, invalid selected conveyance, an
unavailable selected conveyance, malformed source edge, more than 64 legs, or an invalid numeric
sum rejects or produces its stated non-success status without mutation.

## Planning and execution rules

1. Read the traveller's current active location and world. Validate the active destination is in
   that same world.
2. Construct edges only from active individual mode records in that world. Include ground/air
   edges only when the selected conveyance is co-located at the edge origin and satisfies its mode
   action's read-only preconditions. Include portal edges only when the portal owner says it is
   usable at the origin.
3. Find the least estimated-minute route. Tie-break by fewer legs, then the fixed mode order
   \`portal\`, \`on-foot\`, \`ground\`, \`air\`, then canonical route/portal ID. This makes equal options
   reviewable and replayable.
4. Return the complete ordered proposal; do not execute it from \`plan\`.
5. For \`advance-one-leg\`, re-read traveller, endpoint, route/portal, selected conveyance, and
   clock state; reject a changed fingerprint. Delegate exactly the next leg to its owner.
6. After a successful leg, re-read state and call \`plan\` again. A newly impossible continuation
   returns \`blocked\`/\`unreachable\` at that reached location; no later leg has been pre-authorized.

## Dependency order and slices

~~~text
World Feature 16: mode-aware distant itinerary
├─ W8 on-foot one-leg travel                                      [verified]
├─ W12 generic ground-conveyance travel                           [verified]
├─ W13 generic aerial-conveyance travel                          [verified]
├─ W15 fixed portal travel                                        [verified]
├─ confirmed itinerary procedure/mechanics and result semantics  [semantic boundary]
│  └─ Slice 1: read-only multimode planner                         [implemented]
└─ verified planner                                               [parent: Slice 1]
   └─ Slice 2: one-leg delegation and fresh-state re-planning
~~~

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Mode-aware planning | All four mode owners are verified and result semantics are confirmed. | A read-only request produces only legal ordered legs or a precise non-success status. |
| 2 | One-leg continuation | Slice 1 is verified. | Exactly one delegated leg mutates; the next route is rebuilt from fresh state. |

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Mixed-mode happy path | A route that needs on-foot, selected ground or air conveyance, and/or a fixed portal returns the ordered legs and correct summed estimate. |
| Far destination | A destination many locations away returns up to 64 individually executable legs, never one direct containment move. |
| Portal | A valid fixed portal appears only as its authored edge and contributes zero estimated minutes; no spell/item teleport is implied. |
| Selected conveyance | A valid co-located selected conveyance can supply only its own mode edges. Missing, archived, wrong-world, or elsewhere conveyance returns \`unavailable-resource\` or excludes those edges. |
| Per-leg block | After leg one, a changed route/clock/conveyance/location condition prevents leg two and leaves the traveller at the reached intermediate location. |
| Re-plan | A legal state change after a leg can produce a different next route; it never executes a cached later leg. |
| No path/bounds | Disconnected destination returns \`unreachable\`; oversized/cyclic graph work is bounded and returns \`too-long\`/rejects without a partial write. |
| Isolation | Planning writes no state. Advancement writes only the one delegated mode action's normal effects and does not create a composite bypass event. |
| Repository acceptance | Focused planner/coordinator tests, \`roleplay validate catalog\`, full suite, and \`git diff --check\` pass. |

## Completion boundary

Feature 16 is complete when a host can request a far destination using the traveller's explicitly
available travel modes, inspect the proposed legs, and advance only one freshly validated leg at a
time. Stop before any subsystem that decides encounters, provisions, access rights, free travel,
or non-fixed teleportation.

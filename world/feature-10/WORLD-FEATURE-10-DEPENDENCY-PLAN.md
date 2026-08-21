# World Feature 10 dependency plan — clock-driven route closure

Status: **Feature 10 verified**  
Last updated: 2026-08-20

## Target capability

One scheduled world condition temporarily closes the verified Feature 8 fixture route. The condition
has an authored source, one route scope, start/end minutes, descriptive visibility, and stored
current status. Whenever the root clock changes, a bounded Feature 10 reaction reconciles the
condition with the clock and keeps the route's explicit availability state in sync.

During the active interval, the on-foot journey mechanic rejects the closed route with zero effects.
Before its start and after its end, the same route is open and may be used normally. The condition
is deterministic state derived from the committed clock, not a scheduler, weather generator, or
free-form GM narration.

### Included

- One `game.core.world.condition` entity component for the single `route-closure` condition family.
- One `game.core.world.route.availability` component that the route journey mechanic consults.
- Condition-to-world and condition-to-route relationships with explicit source, scope, time, and
  status evidence.
- One fixed subscription/reaction listening to the existing root-clock replacement event.
- One scheduled route-closure fixture and Feature 8 journey revision that denies a closed route.
- Fresh-import, interval boundary, skipped interval, correction reconciliation, chain/rollback,
  journey-denial, and no-change coverage.

### Excluded

- Seasons, weather simulation, random hazards, severity rolls, stacked/overlapping conditions,
  conditions on locations, multiple route modes, party travel, pathfinding, terrain, detours,
  encounter generation, or a general condition authoring UI.
- A scheduler, polling loop, wall-clock use, real-time expiry, calendar dates, durations beyond
  start/end minute coordinates, background writes, or AI-selected consequences.
- A new semantic event type, MCP query kind, migration, notification, map behavior, player
  filtering, authorization, campaign/quest state, or a generic multi-condition framework.

## Source and contract basis

| Authority | Evidence | Decision supplied |
| --- | --- | --- |
| Repository workflow | `AGENTS.md`; `procedure.system.create-feature`; `procedure.system.verify` | Confirmation gates, catalog validation, focused/full evidence, and persistent-import boundary. |
| World clock | `procedure.game.core.world.time` and [Feature 5 plan](../feature-05/WORLD-FEATURE-05-DEPENDENCY-PLAN.md) | Root-owned bounded minute/revision state and structural clock-replacement evidence. W10 begins only after the final Feature 5 receipt verifies this contract. |
| Route journey | [Feature 8 plan](../feature-08/WORLD-FEATURE-08-DEPENDENCY-PLAN.md) | Named route scope, on-foot journey roles, route duration, atomic movement/time outcome, and the existing route owner. |
| Reactions/subscriptions | `procedure.event.react`; `procedure.subscription.create`; `procedure.event.chain-limits`; [Feature 6 plan](../feature-06/WORLD-FEATURE-06-DEPENDENCY-PLAN.md) | Accepted-event reaction semantics, fixed role binding, payload/entity filters, one execution per root chain, and full rollback on reaction failure. |
| Structural clock event | `world.component.replaced` | Existing payload provides world entity/definition and before/after clock records; no time-specific event is needed for this single condition. |
| Generic world model | `procedure.world.model`; `procedure.world.change` | Conditions, route availability, and scope are components/relationships; all writes remain explicit effects. |
| Route/map boundary | [Feature 9 plan](../feature-09/WORLD-FEATURE-09-DEPENDENCY-PLAN.md) | A closure may alter route availability but does not alter route anchors, map geometry, containment, or adjacency. |

## Ownership and confirmation boundary

Feature 10 adds the first temporary world-state family. It revises existing owners rather than
creating parallel systems:

- `procedure.game.core.world.condition` owns condition data, scope links, status reconciliation,
  and the single active/closed mapping.
- `procedure.game.core.world.travel` is revised so the Feature 8 journey mechanic requires a valid
  route-availability component and rejects `closed` state before proposing movement/time effects.
- `procedure.game.core.world.time` retains clock data and monotonic normal advance. Its
  administrative correction boundary is clarified: any accepted replacement of the root clock
  must permit the condition reaction to reconcile affected state from the resulting clock value.
- `procedure.game.core.world.spatial` and map consumers are unchanged; availability does not
  rewrite visual route records.

The user confirmed these permanent IDs and fixture values on 2026-08-20:

| Artifact | Proposed meaning |
| --- | --- |
| `game.core.world.condition` | Closed state for one scheduled `route-closure` with status, summary, authored source, descriptive visibility, and start/end minute coordinates. |
| `game.core.world.route.availability` | Closed current route eligibility: `open` or `closed`. It is updated only by this feature's condition reaction in the first slice. |
| `game.core.world.condition.in-world` | Directed empty-data link from the condition entity to exactly one active world root. |
| `game.core.world.condition.affects` | Directed empty-data link from the condition entity to exactly one active route entity. |
| `procedure.game.core.world.condition` | Governs the four artifacts above, lifecycle reconciliation, clock-correction policy, and route-availability mapping. |
| `mechanic.game.core.world.condition.sync-route-closure` | Reaction that reconciles the fixed condition and route availability from an accepted root-clock replacement. |
| `subscription.game.core.world.condition.sync-route-closure` | Active subscription binding the fixture condition/route to root-clock replacements. |
| `condition.feature-10.gate-market-closure` | The one reviewed fixture closure on the Feature 8 gate→market route. |

The confirmed fixture `startMinute` and `endMinute`, together with the existing Feature 8 duration,
are `60` and `180`: start is inclusive, end is exclusive, and
`0 ≤ startMinute < endMinute ≤ 1,000,000,000`.

## Closed condition and availability contracts

~~~text
game.core.world.condition
{
  kind: "route-closure",
  status: "scheduled" | "active" | "expired",
  summary: trimmed text, 1–1,000 Unicode scalar values,
  source: trimmed text, 1–500 Unicode scalar values,
  visibility: "public" | "party" | "gm",
  startMinute: integer, 0–1,000,000,000 inclusive,
  endMinute: integer, 1–1,000,000,000 inclusive
}

game.core.world.route.availability
{
  status: "open" | "closed"
}
~~~

Both records are closed. Missing, `null`, arrays/scalars, extra keys, invalid/whitespace text,
unknown kind/status/visibility, non-integers, overflow, or `startMinute >= endMinute` rejects.
The condition contains no clock ID, route ID, location ID, campaign/quest reference, duration,
history, stack count, severity, route mode, or caller-defined effect. Its directed relationships
supply the root and route scope.

The fixture begins at clock minute zero with condition `scheduled` and route availability `open`.
For the resulting clock minute `m`, expected state is:

| Clock range | Expected condition status | Expected route availability |
| --- | --- | --- |
| `m < startMinute` | `scheduled` | `open` |
| `startMinute ≤ m < endMinute` | `active` | `closed` |
| `m ≥ endMinute` | `expired` | `open` |

A normal monotonic clock advance therefore progresses scheduled → active → expired. If an
inspected administrative clock correction moves time backward, the reaction reconciles stored
status/availability back to the table. That correction is administrative evidence, not a claim
that time naturally reversed or that a previously expired condition produced a second narrative
event.

## Reaction and journey behavior

The subscription listens only to `world.component.replaced` where scalar payload values identify
the confirmed world-root entity and `game.core.world.clock`. It tracks that world root and binds
the fixture `condition` and `route` through fixed roles. It is active, ordered `0`, and has
`maxExecutionsPerChain: 1`.

The reaction declares the clock event component plus fixed condition/route requirements. It:

1. Validates the accepted event's entity/definition and the closed before/after clock data.
2. Validates the condition's one root scope and one route scope, and that they match the fixed
   roles and the root event.
3. Computes the expected condition/availability pair solely from the **after** clock minute.
4. Returns no effects if both fixed records already equal that pair.
5. Otherwise returns exactly two complete `component.set` effects in order: condition replacement,
   then route-availability replacement. It changes no clock, route metadata, location, containment,
   adjacency, map anchor, knowledge, faction, campaign, or quest state.

The reaction's derived replacements cannot re-enter its subscription because its filters track the
world root and clock definition only. A malformed binding, corrupt component, invalid scope, failed
effect, or chain-limit failure aborts the entire root transaction, including the clock advance or
journey that caused it.

Revise `mechanic.game.core.world.route.travel-on-foot` to require both `game.core.world.route` and
`game.core.world.route.availability` on role `route`. It rejects closed, missing, or malformed
availability before proposing either of its two normal journey effects. It never changes condition
state or availability itself.

## Dependency order and slices

~~~text
World Feature 10: clock-driven one-route closure
├─ W5 root clock, action, structural event, correction policy          [must be verified]
├─ W6 accepted-event subscription/reaction proof                       [must be verified]
├─ W8 named route and atomic on-foot journey                           [must be verified and played]
├─ W9 map layout (consumer only)                                       [not a technical dependency]
├─ confirmed condition/availability vocabulary and interval            [implemented]
│  └─ Slice 1: condition/availability state, scope, fixture, tests    [verified]
└─ verified condition foundation                                       [parent: Slice 1]
   └─ Slice 2: clock reaction and journey-closure integration          [verified]

Schedulers, multiple conditions, weather, locations, player filtering [excluded]
~~~

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Condition foundation | W5/W8 fixtures and one played W8 journey are verified; vocabulary and interval are confirmed. | **Verified:** fresh import has one valid scheduled route closure and matching open route availability; invalid data/scope leaves the established route/topology unchanged. See the [Slice 1 receipt](WORLD-FEATURE-10-SLICE-1-RECEIPT.md). |
| 2 | Clock reaction and route denial | Slice 1 and W6 reaction behavior are verified. | **Verified:** clock boundaries reconcile the condition/route atomically, and a journey through the active closure fails with no movement/time change. See the [implementation receipt](WORLD-FEATURE-10-IMPLEMENTATION-RECEIPT.md). |

## Slice 1 — condition foundation

| Artifact | Change |
| --- | --- |
| Component definitions/schemas | Add `game.core.world.condition` and `game.core.world.route.availability` with the exact closed data above. |
| Governing procedures | Add `procedure.game.core.world.condition`. Revise travel/time contracts only for availability eligibility and clock-correction/reconciliation boundaries. |
| Fixture | Add the condition entity, its active condition component, root/route scope links, and initial `open` availability to the confirmed Feature 8 route. |
| Focused tests | Add `CatalogWorldFeature10Tests` or the nearest world catalog test owner for fresh import, closed input, link conventions, initial state, and isolation. |

### Slice 1 acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Fresh import | One route-closure condition has exactly one root link and one route link; initial status is scheduled and the scoped route has exactly open availability. |
| Closed data | Invalid condition/availability JSON, bad text/status/kind/minutes, or non-object data rejects. |
| Interval | Zero start is legal; end is exclusive; equal/reversed/overflow minutes reject. |
| Scope | Missing/duplicate/reversed/self/nonempty/cross-world/non-route links reject. A route may not carry availability without the fixture condition in this bounded slice. |
| Isolation | Condition creation changes no route metadata, location/topology, traveller containment, root clock, map anchor, knowledge, faction, campaign, or quest data. |
| Repository | Focused tests and `roleplay validate catalog` pass; no persistent import occurs. |

## Slice 2 — clock reaction and route denial

Add the reaction mechanic/subscription and revise the Feature 8 journey mechanic/procedure. The
fixture subscription binds exactly the confirmed condition and route IDs; it must not search, pick,
or update arbitrary conditions.

### Slice 2 acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Before start | Clock advance ending below start leaves condition scheduled and route open; the reaction returns no effects. |
| Start boundary | An advance ending exactly at start changes condition to active and route to closed in the same root transaction. |
| Interior advance | Any ending minute within `[start, end)` retains active/closed without duplicate replacements. |
| End boundary | An advance ending exactly at end changes condition to expired and route to open in the same root transaction. |
| Skipped interval | An advance from before start to at/after end yields expired/open directly; no invented intermediate event occurs. |
| Administrative correction | A committed inspected clock correction to any valid minute reconciles condition/availability to the table; it does not modify the underlying start/end evidence. |
| Journey denial | While availability is closed, the Feature 8 route journey returns zero effects: traveller containment and root clock stay byte-identical. |
| Journey recovery | After expiry/open reconciliation, the valid on-foot journey again moves the traveller and advances time atomically. |
| Invalid reaction state | Corrupt condition/availability/scope/fixed binding or invalid reaction effect rolls back the source clock action/journey and produces no partial derived state. |
| Chain safety | One matching clock event executes this subscription once; its two derived component events cannot re-route it. Non-clock/non-root events do nothing. |
| Feature isolation | Existing Feature 2 adjacent movement remains route/time/availability-free; no map, audience, campaign, quest, notification, or scheduler behavior is added. |
| Repository acceptance | Focused catalog/action/reaction tests, `roleplay validate catalog`, full suite, and `git diff --check` pass. Run a protocol walk only if the public MCP/dependency registration surface changes. |

## Completion boundary

Feature 10 is complete when the single reviewed closure is consistently derived from root-clock
state, blocks its one scoped journey only while active, and proves atomic reconcile/rollback/no-op
behavior. Stop before adding a second condition, condition stacking, a new weather family,
location effects, automatic content generation, or any background process.

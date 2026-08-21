# World Feature 12 historical proposal — horse-cart conveyance journey

Status: **Superseded by the [generic ground-conveyance revision](WORLD-FEATURE-12-GROUND-CONVEYANCE-PLAN.md); do not implement**  
Last updated: 2026-08-20

> This retained proposal used horse-cart-specific route timing. The revised authoritative plan
> places distance on the route and speed on the generic ground conveyance so different settings
> can use sleds, cars, rovers, and similar vehicles without new mechanics.

## Target capability

One world-owned horse cart and its marked driver can take one named, active, one-way cart route
between two stored adjacent locations. The cart and driver must be co-located at the origin. A
successful action moves both to the destination and advances the root clock by the route's fixed
horse-cart duration in one transaction.

This is a first conveyance proof, not an inventory, ownership, passenger, cargo, vehicle physics,
or animal-riding system. The cart is one persistent world entity whose containment gives its
location; the driver is one existing traveller. A separate cart-only route avoids silently changing
Feature 8's deliberately single-mode on-foot route schema.

### Included

- One `game.core.world.conveyance` component with the sole initial kind `horse-cart`.
- One fixture horse-cart entity located with the existing fixture traveller.
- One closed conveyance-route component and three explicit world/from/to links for a distinct
  `horse-cart` route.
- One deterministic driver-and-cart journey mechanic that moves cart, driver, and root clock
  atomically.
- Fresh-import, co-location, route-mode, duration, clock, rollback, replay, and no-change tests.

### Excluded

- Individual horse entities, harnesses, animal welfare/health, creature AI, ownership, hiring,
  driving proficiency, inventory/cargo, passengers, seat capacity, party movement, mounts,
  riding, vehicle damage, speed/encumbrance, roads/terrain, routes with several modes, or
  cart-to-foot conversion.
- Browser controls, map interaction, player authorization, route discovery, conditions, random
  encounters, notifications, migrations, new event types/subscriptions, or a new MCP kind.
- Changing Feature 2 movement, Feature 8 on-foot route data, Item/Inventory ownership, or
  Character Creation equipment grants. Those owners may later connect a player-owned conveyance.

## Source and contract basis

| Authority | Evidence | Decision supplied |
| --- | --- | --- |
| Feature workflow | `AGENTS.md`; `procedure.system.create-feature`; `procedure.system.verify` | Repository confirmation and validation/acceptance requirements. |
| Traveller movement | `procedure.game.core.world.travel` and [Feature 2 receipt](../feature-02/WORLD-FEATURE-02-SLICE-2-RECEIPT.md) | Traveller eligibility and current location are derived from containment. |
| On-foot journey | [Feature 8 plan](../feature-08/WORLD-FEATURE-08-DEPENDENCY-PLAN.md) | World-scoped directed route convention, fixed duration, root-clock handoff, and atomic journey shape. |
| World clock | `procedure.game.core.world.time` and [Feature 5 plan](../feature-05/WORLD-FEATURE-05-DEPENDENCY-PLAN.md) | Same root-clock bounds, calendar identity, action audit, and structural evidence. |
| Generic world model | `procedure.world.model`; `procedure.world.change` | The cart is an entity/component; co-location and arrival use containment, not copied location fields. |
| Item/character boundaries | `ITEMS_AND_INVENTORY_PLAN.md`, Slices 1–6; `CHARACTER_CREATION_PLAN.md`, Slices 1–6 | This first world fixture does not claim an item instance, possession, starting equipment, or character ownership model. |

## Ownership and confirmation boundary

Revise `procedure.game.core.world.travel` to govern conveyance-route authoring and this cart
journey alongside the existing adjacent move and on-foot route journey. No parallel transport
procedure is created.

Confirm these permanent IDs and exact fixture prose/duration before implementation:

| Artifact | Proposed meaning |
| --- | --- |
| `game.core.world.conveyance` | Closed world state for a mobile conveyance. This first feature permits exactly active/archived `horse-cart` state and descriptive metadata. |
| `conveyance.feature-12.horse-cart` | The first world-owned horse-cart entity; it is not an item, actor, character, mount, or route. |
| `game.core.world.conveyance-route` | Closed directed ground-conveyance journey metadata: lifecycle, summary, descriptive visibility, sole `horse-cart` mode, and fixed duration minutes. |
| `game.core.world.conveyance-route.in-world` | Directed empty-data link from conveyance route to exactly one active world root. |
| `game.core.world.conveyance-route.from` | Directed empty-data link from conveyance route to exactly one active origin location. |
| `game.core.world.conveyance-route.to` | Directed empty-data link from conveyance route to exactly one active destination location. |
| `conveyance-route.feature-12.gate-to-market-horse-cart` | A distinct active directed cart route from gate to market, declaring `horse-cart` mode and its reviewed duration. |
| `mechanic.game.core.world.conveyance-route.travel-horse-cart` | Active deterministic action that co-moves one active driver and one active horse cart over the matching conveyance route while advancing the root clock. |

The existing `game.core.world.route` component is not widened in this slice: it remains Feature
8's closed `on-foot` contract. `game.core.world.conveyance-route` deliberately repeats only the
reviewed route metadata shape with its own `horse-cart` mode and three empty-data endpoint links.
A road that supports both walking and carts is represented by separate authored on-foot and
conveyance-route records during this proof. A later common multi-mode route capability needs a
dedicated schema-migration/compatibility plan; it is not smuggled into Feature 8.

## Closed conveyance and action contracts

~~~text
game.core.world.conveyance
{
  kind: "horse-cart",
  status: "active" | "archived",
  summary: trimmed text, 1–1,000 Unicode scalar values,
  visibility: "public" | "party" | "gm"
}

game.core.world.conveyance-route
{
  status: "active" | "archived",
  summary: trimmed text, 1–1,000 Unicode scalar values,
  visibility: "public" | "party" | "gm",
  mode: "horse-cart",
  durationMinutes: integer, 1–1,440 inclusive
}
~~~

Both records are closed. Missing, `null`, arrays/scalars, unknown keys, invalid/empty/untrimmed
text, unknown kind/status/visibility/mode, invalid duration, or archived conveyance/route rejects.
Neither stores an owner, driver, horse, passenger, capacity, cargo, current location, speed,
health, condition, damage, item ID, character ID, or clock field.

The cart is directly contained at an active origin location in slot `presence`, as is the driver.
Their co-location is derived from containment and is rechecked by the mechanic; neither component
stores it.

The mechanism declares exactly six roles:

| Role | Required projection | Purpose |
| --- | --- | --- |
| `driver` | `game.core.world.traveller` | Active traveller co-located at the origin. |
| `conveyance` | `game.core.world.conveyance` | Active horse cart co-located at the origin. |
| `origin` | `game.core.world.location` with relationships | Current co-location and adjacency evidence. |
| `destination` | `game.core.world.location` | Directed cart-route destination. |
| `conveyanceRoute` | `game.core.world.conveyance-route` with relationships | Active cart-route data and its exact in-world/from/to evidence. |
| `world` | `game.core.world.root` and `game.core.world.clock` | Route scope and clock replacement. |

Input is exactly `{}`. A caller cannot supply a cart kind, driver/cargo/passenger list, route mode,
duration, origin/destination result, clock value, containment slot, or effect list.

After validating roles, closed state, cart/driver co-location, active sibling locations, canonical
adjacency, matching world-scoped conveyance-route links, and clock overflow, the action returns
exactly:

1. `containment.move` conveyance → destination / `presence`;
2. `containment.move` driver → destination / `presence`;
3. complete `component.set` of the same root clock with
   `currentMinute + route.durationMinutes` and `revision + 1`.

No one effect is optional. A rejected or invalid later effect rolls back all three changes and all
success evidence. The result uses existing containment/component structural events and action
audit; there is no transport-specific event.

## Dependency order and slices

~~~text
World Feature 12: one horse-cart co-travel journey
├─ W1/W2 containment, adjacency, traveller eligibility                 [verified]
├─ W5 root clock                                                        [must be verified]
├─ W8 route entity and on-foot journey                                  [must be verified]
├─ Item/character ownership model                                       [not required; explicitly excluded]
├─ confirmed conveyance/conveyance-route vocabulary and duration        [semantic boundary]
│  └─ Slice 1: conveyance/route components, links, fixture, convention tests
└─ verified cart foundation                                             [parent: Slice 1]
   └─ Slice 2: atomic driver/cart/clock journey action

Mounts, cargo, passengers, multi-mode routes, and flight [excluded]
~~~

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Horse-cart foundation | W5/W8 are verified; conveyance/conveyance-route IDs and duration are confirmed. | Fresh import proves one active cart and one matching conveyance route; invalid cart/route state leaves existing world routes unchanged. |
| 2 | Cart journey | Slice 1 is verified. | One legal action moves exactly cart + driver + clock atomically; stale/co-location/mode/clock failures leave all three unchanged. |

## Slice 1 — horse-cart foundation

| Artifact | Change |
| --- | --- |
| Component definitions/schemas | Add `game.core.world.conveyance` and `game.core.world.conveyance-route` with their exact closed contracts and three empty-data route links. |
| Governing procedure | Revise `procedure.game.core.world.travel` to record/correct a conveyance and distinct conveyance route without changing Feature 2/8 meaning. |
| Fixture | Add one cart entity at the fixture gate and one cart-only gate→market conveyance route with its three canonical links. |
| Focused test | Add `CatalogWorldFeature12Tests` or the nearest world catalog owner for import/readback and invalid conveyance/cart-route variants. |

### Slice 1 acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Fresh import | One active cart is at gate/`presence`; one active cart route has `horse-cart` mode, valid scope/endpoints, and reviewed duration. |
| Closed state | Invalid component/route data or archived cart/route rejects. |
| Route separation | The cart route is distinct from the Feature 8 on-foot route; neither record's mode/duration is rewritten. |
| Placement | Cart in a root/region/route/faction/cart container, absent origin, or non-`presence` fixture placement rejects. |
| Topology | Wrong/missing/reversed/duplicate endpoints or non-adjacent route rejects. |
| Isolation | Existing traveller, clock, on-foot route, location topology, conditions, factions, knowledge, items, and characters remain unchanged. |

## Slice 2 — deterministic horse-cart journey

### Slice 2 acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Happy path | Co-located active driver/cart at gate use the matching cart route to reach market; exactly three effects move cart, move driver, then advance clock. |
| Co-location | Cart elsewhere, driver elsewhere, wrong containment slot, or cart/driver identity collision rejects with no move/time change. |
| Route mode | On-foot route, unknown/archived/malformed cart route, wrong endpoints/world, or invalid adjacency rejects. |
| Clock | Missing/corrupt/overflow root clock rejects; a success preserves calendar identity and increments revision once. |
| Replay | Repeating the successful request with old origin rejects; cart, driver, and clock do not advance twice. |
| Rollback/evidence | Any invalid effect rolls back both containment moves, clock, events, and success audit. A success has three normal structural events under one root action. |
| Boundaries | No passenger, cargo, horse, inventory, ownership, mount, combat, condition, or map state is created or changed. |
| Repository acceptance | Focused action tests, `roleplay validate catalog`, full suite, and `git diff --check` pass. |

## Completion boundary

Feature 12 is complete when one reviewed horse cart and driver complete one atomic cart journey and
all invalid/mode/co-location/replay/rollback cases preserve world state. Stop before modelling a
horse separately, adding a rider/passenger/cargo, combining travel modes on a route, or linking a
cart to inventory/character ownership.

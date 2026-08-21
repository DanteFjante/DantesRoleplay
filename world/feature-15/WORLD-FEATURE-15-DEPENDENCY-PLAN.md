# World Feature 15 dependency plan — fixed teleport portals

Status: **Feature 15 verified**  
Last updated: 2026-08-20

## Target capability

One marked traveller standing at one active fixed world portal can cross to that portal's one
explicit destination location in a single atomic action. The traveller's containment changes from
the portal location to the destination; the world clock does not change.

This is magical point-to-point relocation, not a journey. It does not traverse intermediate
locations, consume rations, trigger roadside bandits, use roads/routes/adjacency, or claim that a
prior itinerary authorizes it. The portal's active state and exact destination are the whole first
authorization rule.

### Included

- One world-owned fixed portal entity directly contained at its active origin location.
- One closed `game.core.world.teleport-gate` component and explicit world/destination links.
- One deterministic traveller-only teleport action with exact empty input and no clock effect.
- One one-way fixture portal plus fresh-import, scope, placement, lifecycle, destination, rollback,
  replay, and unchanged-clock coverage.

### Excluded

- Teleport spells, scrolls, items, mana/charges, character abilities, ownership, keys, payments,
  portal networks, random destinations, recall/home locations, targeting a creature, companions,
  cargo, cross-world travel, or player authorization.
- Routes, adjacency, travel time, map lines, pathfinding, journey plans, rations, encounters,
  combat, weather, generic conditions, scheduling, events, subscriptions, migrations, or a new
  MCP kind.

## Source and contract basis

| Authority | Evidence | Decision supplied |
| --- | --- | --- |
| Repository workflow | `AGENTS.md`; `procedure.system.verify` | Confirmation, validation, and acceptance boundaries. |
| Traveller movement | `procedure.game.core.world.travel` and [Feature 2 receipt](../feature-02/WORLD-FEATURE-02-SLICE-2-RECEIPT.md) | Traveller eligibility and actual location remain containment-derived. |
| World clock | `procedure.game.core.world.time` and Feature 5 receipt | Teleportation has no clock effect; root-clock state must remain byte-identical. |
| World structures/actions | `procedure.world.model`; `procedure.world.change`; `procedure.mechanic.write`; `procedure.action.run` | Closed data, explicit roles, deterministic effects, rollback, and audit evidence. |
| Other travel boundaries | Features 8, 12, 13, and 14 plans | A portal is not a route, flight, or itinerary. Its instant semantics must not widen those contracts. |
| Character/item boundary | Character Creation and Items plans | Spell/item teleports belong to later ability or item ownership features. |

## Ownership and confirmation boundary

Revise `procedure.game.core.world.travel` to govern fixed portal authoring and instant relocation
alongside existing movement forms. No general magic, item, or character procedure is created.

Confirm these permanent IDs and exact fixture endpoints before implementation:

| Artifact | Proposed meaning |
| --- | --- |
| `game.core.world.teleport-gate` | Closed world state for one fixed portal with `active`, `disabled`, or `archived` lifecycle and descriptive metadata. |
| `game.core.world.teleport-gate.in-world` | Directed empty-data link from portal entity to exactly one active world root. |
| `game.core.world.teleport-gate.to` | Directed empty-data link from portal entity to exactly one active destination location. The portal's direct `presence` containment is its origin. |
| `teleport-gate.feature-15.gate-to-observatory` | One reviewed portal contained at gate and targeting observatory. It is one-way; return travel needs another portal. |
| `mechanic.game.core.world.teleport-gate.teleport` | Active deterministic action moving one eligible traveller from portal origin to its exact destination with no time change. |

## Closed portal and action contracts

~~~text
game.core.world.teleport-gate
{
  kind: "fixed-portal",
  status: "active" | "disabled" | "archived",
  summary: trimmed text, 1–1,000 Unicode scalar values,
  visibility: "public" | "party" | "gm"
}
~~~

The record is closed. Missing, `null`, arrays/scalars, extra keys, unknown kind/status/visibility,
invalid text, or disabled/archived state rejects. It stores no current location, origin/destination
ID, traveller, owner, access key, charge, spell, item, duration, clock, route, or availability.

The portal is directly contained at one active location in `presence`; that containment is its
origin. It has exactly one empty-data `in-world` link and one empty-data `to` link. The
destination must be another active location in that same world; it may be non-adjacent.
Missing/duplicate/reversed/nonempty/cross-world/self/inactive links or portal placement reject.

The mechanism declares exactly five roles:

| Role | Required projection | Purpose |
| --- | --- | --- |
| `traveller` | `game.core.world.traveller` | Active traveller directly co-located with the portal at origin/`presence`. |
| `portal` | `game.core.world.teleport-gate` with relationships and containment | Active fixed portal, origin containment, and exact world/destination links. |
| `origin` | `game.core.world.location` | Claimed portal location and traveller co-location evidence. |
| `destination` | `game.core.world.location` | Exact linked arrival location. |
| `world` | `game.core.world.root` and `game.core.world.clock` | Shared portal scope and unchanged-clock proof. |

Input is exactly `{}`. A caller supplies no destination, spell, item, charge, path, duration,
clock value, containment slot, or effects.

After closed-state, containment, endpoint, world-scope, and unchanged-clock validation, success
returns exactly one effect:

1. `containment.move` traveller → destination / `presence`.

No component replacement is returned. The root clock, route state, portal, relationships, and
unrelated entities remain unchanged. Existing containment events and action audit record the move;
no teleport-specific event is introduced.

## Dependency order and slices

~~~text
World Feature 15: one fixed instant portal
├─ W1 containment and active locations                                 [verified]
├─ W2 traveller eligibility and atomic containment move                [verified]
├─ W5 root clock/no-time baseline                                       [verified]
├─ confirmed portal vocabulary and fixture endpoints                    [implemented]
│  └─ Slice 1: portal component, links, fixture, convention tests      [verified]
└─ verified portal foundation                                           [parent: Slice 1]
   └─ Slice 2: atomic instant teleport action                         [implementation verified]

Spells/items, parties/cargo, portal networks, itinerary integration, and encounters [excluded]
~~~

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Portal foundation | W2/W5 are verified; portal IDs and fixture endpoints are confirmed. | **Verified:** fresh import proves one active portal at its origin and one exact same-world destination; malformed data leaves established world state unchanged. See the [Slice 1 receipt](WORLD-FEATURE-15-SLICE-1-RECEIPT.md). |
| 2 | Instant relocation | Slice 1 is verified. | **Implementation verified:** one valid action moves exactly one traveller to the exact destination; portal, root clock, and all other state remain unchanged. |

## Slice 1 — fixed portal foundation

| Artifact | Change |
| --- | --- |
| Component definition/schema | Add `game.core.world.teleport-gate` with the exact closed fixed-portal contract. |
| Relationship definitions | Add the two exact empty-data portal links. |
| Governing procedure | Revise `procedure.game.core.world.travel` for portal placement, scope, lifecycle, direction, and no-time semantics. |
| Fixture | Add one active portal at gate/`presence` with exact world and observatory destination links. |
| Focused test | Add `CatalogWorldFeature15Tests` or nearest world catalog owner for import/readback and invalid portal conventions. |

### Slice 1 acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Fresh import | One active fixed portal is at gate/`presence` and has one valid same-world observatory destination. |
| Closed state | Invalid component data, disabled/archived status, or unknown fields reject. |
| Origin/target truth | Missing, duplicate, malformed, cross-world, self, or inactive portal placement/link rejects. |
| Direction | The gate portal only permits gate → observatory; reverse travel requires another explicit portal. |
| Isolation | Existing routes, map, clock, traveller, factions, knowledge, conditions, items, characters, cart, and dragon state remain unchanged. |

## Slice 2 — deterministic instant teleport

### Slice 2 acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Happy path | An active traveller co-located with the active gate portal reaches observatory through exactly one containment move. |
| No travel inference | Adjacent locations, roads, routes, cart/flight routes, or an unlinked destination do not authorize teleportation. |
| Lifecycle/co-location | Disabled/archived/malformed portal, traveller elsewhere, wrong slot, stale origin, wrong destination, or identity collision rejects without effects. |
| Clock | Before/after root-clock bytes are identical on success and failure. No duration is calculated or consumed. |
| Replay/rollback | Reusing the old origin rejects; a failed effect rolls back containment, structural events, and success audit. |
| Boundary | No spell, item, cost, key, passenger, cargo, encounter, rations, combat, route, map, or itinerary behavior is added. |
| Repository acceptance | Focused tests, `roleplay validate catalog`, full suite, and `git diff --check` pass. |

## Completion boundary

Feature 15 is complete when one reviewed fixed portal instantly moves one co-located traveller to
one explicit destination with no clock change and no route inference. Stop before spell/item
teleportation, portal networks, carrying others, cross-world jumps, or using portals in a multi-leg
journey planner.

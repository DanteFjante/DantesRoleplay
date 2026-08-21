# World Feature 2 dependency plan — governed adjacent movement

Status: **Feature 2 verified**
Last updated: 2026-08-20

## Execution rule

This repository plan follows `AGENTS.md`,
[`procedure.system.create-feature`](../../catalog/procedures/system/procedure.system.create-feature.md),
and the quality structure in
[`TERRA-FEATURE-PLANNING-GUIDE.md`](../../ruleset/dnd2024/TERRA-FEATURE-PLANNING-GUIDE.md).
Each slice is independently implemented, catalog-validated in a fresh disposable database,
focused-tested, and stopped at its exit gate. A persistent catalog import belongs only to later
integration play or release after deliberate drift reconciliation.

## Target capability

A marked traveller at one active fixture location can take one stored adjacent connection to
another active sibling location. The action atomically changes only that traveller's containment
and returns a deterministic, inspectable result.

### Included

- Generic opt-in relationship projection for mechanics that declare it.
- Shared-game traveller marker and one fixture traveller.
- One deterministic adjacent-movement mechanic and governing procedure.
- Gate → market and market → observatory movement, readback, replay, and no-change coverage.

### Excluded

- Distance, time, routes, terrain, travel modes, random encounters, clocks, maps, pathfinding,
  party movement, visibility enforcement, authorization, discovery, lore, quests, and campaigns.
- Moving a location, region, item, faction, or arbitrary unmarked entity.
- New MCP verb/kind, event type, subscription, migration, or world-specific C# helper.

Current location remains derived from containment. Feature 1 remains the sole owner of world
topology and adjacency. A future routes/travel feature owns distance, time, and travel modes.

## Source and contract basis

Adjacent setting movement is an authored product rule, not an SRD calculation. Its authority is
the stored topology and these contracts:

| Authority | Evidence | Decision supplied |
| --- | --- | --- |
| Feature workflow | `procedure.system.create-feature`; `AGENTS.md` | Repository mode, one coherent slice, verification and review boundaries. |
| World model/change | `procedure.world.model`; `procedure.world.change` | Component, containment, relationship ownership and atomic `containment.move`. |
| Action/mechanic authoring | `procedure.action.run`; `procedure.mechanic.write` | Declared projection, action routing, replay, proposed effects. |
| Verified topology | [Feature 1 plan](../feature-01/WORLD-FEATURE-01-DEPENDENCY-PLAN.md) and [receipt](../feature-01/WORLD-FEATURE-01-RECEIPT.md) | Active location data, fixture IDs, canonical adjacency and no duplicated location field. |
| Projection inventory | `DantesRoleplay.DataAccess/ProjectionResolver.cs`; `ProjectionResolverTests`; [Slice 1 receipt](WORLD-FEATURE-02-SLICE-1-RECEIPT.md) | Containment is projected, and declared roles can opt into frozen relationship records. |

## Verified dependencies and ownership decisions

| Dependency | Status/evidence | Required behavior |
| --- | --- | --- |
| World root, locations, adjacency | Implemented: Feature 1 receipt and `CatalogWorldFeature1Tests` | Five-entity topology with active location components and two canonical edges. |
| Containment mutation/event | Implemented: `EffectApplier`, `world.containment.moved` | One accepted move is atomic and emits the existing structural event. |
| Action runner/replay | Implemented: `ActionRunner`, Feature 10 vertical replay | Frozen projection, version/seed audit, atomic effects. |
| Containment projection | Implemented: `ProjectionResolverTests` | Roles include derived `containerId` and `containerSlot`. |
| Relationship projection | Implemented: [Slice 1 receipt](WORLD-FEATURE-02-SLICE-1-RECEIPT.md) | Opting-in mechanics receive frozen, canonical relationship records needed to verify adjacency. |

1. **Current location is containment**, never a `currentLocation` field or action input.
2. **Adjacency is `game.core.world.location.connected-to` with `{}`**, never a caller claim or
   copied connection list.
3. **The generic projection is a prerequisite.** A JavaScript mechanic cannot inspect a stored
   relationship today; a world-specific query helper would violate generic ownership.
4. **Traveller eligibility is `game.core.world.traveller`.** Its closed data is exactly
   `{ "status": "active" }`. It marks use of this feature only; it is not a character sheet,
   speed, party model, or second current-location store.
5. **The caller provides traveller/origin/destination as roles.** The mechanic proves the
   traveller is in origin and origin is connected to destination from the frozen projection.
6. **Movement writes one containment effect only.** No clock, route, connection, component,
   quest, or new event is written.

## Recursive dependency analysis

```text
World Feature 2: governed adjacent movement
├─ Feature 1 topology and canonical adjacency                         [implemented]
├─ containment effect, structural event, action/replay                [implemented]
├─ declared generic relationship projection                            [implemented: Slice 1]
│  ├─ includeRelationships requirement field                           [implemented]
│  ├─ frozen canonical relationship records                            [implemented]
│  └─ parser/resolver/sandbox regression coverage                      [implemented]
└─ traveller marker and adjacent-move mechanic                         [implemented: Slice 2]
   ├─ traveller component/schema and governed procedure                [implemented]
   ├─ fixture traveller in gate                                        [implemented]
   ├─ deterministic movement mechanic                                  [implemented]
   └─ fresh-import/action/replay/no-change coverage                    [implemented]

Routes, time, party movement, maps, lore, campaigns                    [excluded]
```

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Declared generic relationship projection | New projection vocabulary confirmed by implementation request. | **Verified:** an opting-in mechanic receives only stable relationships touching its declared role; a non-opting mechanic receives none. See the Slice 1 receipt. |
| 2 | Traveller marker and adjacent movement | Slice 1 is verified. | **Verified:** fresh-import action coverage proves two stored-edge moves, rejection/replay protection, and one structural event per accepted move. See the [Slice 2 receipt](WORLD-FEATURE-02-SLICE-2-RECEIPT.md). |

## Slice 1 — declared generic relationship projection

### Runtime artifacts

| Artifact | Change |
| --- | --- |
| `RoleRequirement` | Add opt-in `includeRelationships: boolean`, default `false`. |
| `EntityProjection` | Add optional `relationships` only for opted-in roles. Each record has `fromEntityId`, `toEntityId`, `kind`, and raw JSON-object `data`. |
| `ProjectionResolver` | Batch-load non-deleted relationships touching opted-in entities and order them by kind, from ID, then to ID. |
| Generic projection contract/tests | Revise the mechanic projection contract and add focused resolver/sandbox coverage. |

No world component, mechanic, event, migration, or MCP surface belongs in this generic slice.

### Contract and behavior

`includeRelationships` defaults to false; only a JSON Boolean is valid. Null, string, number,
array, or object values reject mechanic requirements consistently at write and run time. For an
opted-in role, `ctx.roles.<role>.relationships` is an explicit empty list when no edge touches the
role and otherwise contains every incoming/outgoing edge that touches it. The projection never
materialises the opposite endpoint's name, components, containment, or other relationships.

The list is a frozen read-only snapshot. A reverse edge remains distinct generic data if stored;
the generic projection never decides symmetry, endpoint validity, or game meaning.

### Slice 1 acceptance matrix

| Test class | Input/setup | Exact expected result |
| --- | --- | --- |
| Opt-in | Role has one incoming and one outgoing relationship | Exactly both records are projected. |
| Isolation | Same role without opt-in | `relationships` is absent from the role/sandbox. |
| Differential | Two roles, only one opts in | Only opted-in role receives relationship data. |
| Empty and ordering | No edges; then unordered kinds/endpoints | Explicit empty list; otherwise canonical kind/from/to order. |
| Closed requirement | Default, true, false, null, scalar, collection | Default/Boolean exact; invalid shapes reject without world change. |
| State integrity | Invalid roles/input/requirements | No projection effects or mutation. |
| Repository | Focused tests, full suite, catalog validation if catalog contract changes, diff check | All pass. |

### Slice 1 exit gate

Verified: the parser, resolver, sandbox projection, contract, and tests agree on opt-in, absence,
empty, ordering, and isolation semantics. See
[the Slice 1 receipt](WORLD-FEATURE-02-SLICE-1-RECEIPT.md). Stop before adding traveller state or
movement.

## Slice 2 — traveller marker and adjacent movement

### Runtime artifacts

| Artifact | ID/path | Delivered change |
| --- | --- | --- |
| Traveller component | `game.core.world.traveller` definition/schema | New closed data: exactly `status: "active"`. |
| Governing procedure | `procedure.game.core.world.travel` | New shared-game recording/correction and adjacent-move contract. |
| Movement mechanic | `mechanic.game.core.world.location.move`; `game.core.world.travel` category | New active deterministic shared-game mechanic. |
| Fixture traveller | `traveller.feature-02.fixture` | New entity, contained by gate in `presence`, with active traveller state. |
| Focused regression | `DantesRoleplay.Tests/CatalogWorldFeature2Tests.cs` | Fresh-import/action/replay/no-change coverage. |

Feature 10's encounter actor, Feature 1 relationship kind, location/root schemas, route/time,
event type, subscription, migration, and MCP surface remain unchanged.

### Data/input contract and required state

The mechanic has exactly these required roles:

| Role | Required state | Purpose |
| --- | --- | --- |
| `traveller` | `game.core.world.traveller` | Must have exact active state. |
| `origin` | `game.core.world.location`, `includeRelationships: true` | Claimed current location and only connection evidence. |
| `destination` | `game.core.world.location` | Claimed adjacent location. |

Input is exactly `{}`. Extra keys, `null`, arrays, scalars, malformed JSON, and whitespace-only
input reject. Origin, destination, connection, result, slot, distance, time, route, and effects
are never caller-provided values.

Before proposing an effect, the mechanic proves all of the following: traveller data is exact and
active; origin/destination data is valid and active; traveller is directly in origin at `presence`;
origin/destination are direct sibling locations in the same container at `location`; their IDs are
distinct; origin's frozen relationships contain exactly a valid empty-data connection between the
two IDs. Either stored orientation is accepted as the Feature 1 undirected convention; a self,
reverse-duplicate, non-location, or nonempty-data edge is corrupt and rejects.

### Resolution, result, and effects

The rule validates closed input and roles, validates component/topology state, then returns exactly
one `containment.move` effect moving traveller to destination in slot `presence`. Its structured
data reports test name, traveller/origin/destination IDs, prior/current slots, and adjacency kind.
It makes no random call and writes no derived route/time data. The existing
`world.containment.moved` structural event occurs once on success; no new semantic event exists.

### Slice 2 acceptance matrix

| Test class | Input/setup | Exact expected result/state assertion |
| --- | --- | --- |
| Happy path | Fresh traveller at gate, destination market | Selected move mechanic applies one effect; only traveller containment becomes market/presence. |
| Second edge | Traveller at market, destination observatory | One accepted move; either stored adjacency orientation works. |
| Routing | “move to the market”, “travel from gate to market”, unrelated administration | Only movement phrases select this mechanic. |
| Closed roles/input | Missing/unknown/duplicate roles, traveller equals place, extra or invalid input | Rejected with zero effects and exact state bytes unchanged. |
| Missing/corrupt state | Missing/inactive/malformed traveller/location, wrong traveller container/slot | Deterministic rejection, no effects. |
| Topology | Disconnected endpoints, differing parent, missing/nonempty/self/reverse-duplicate/non-location edge | Rejected; caller claims never override stored graph. |
| Replay/determinism | Two fresh imports same seed/input/roles; repeat after accepted move | Fresh results/effects equal; stale repeat rejects unchanged. |
| State isolation/event | Accepted and rejected moves | Components/relationships never change; success emits existing structural event once. |
| Cleanup/repository | Disposable copies/databases, focused/full/catalog/diff gates | All temporary state removed and checks pass. |

### Slice 2 exit gate

**Verified.** A fresh import contains every new artifact; real action-runner coverage proves both
stored fixture edges, rejection/replay/readback assertions, one existing structural event per
accepted move, and no unrelated state change. Catalog validation, the full suite, and diff check
pass. See the [Slice 2 receipt](WORLD-FEATURE-02-SLICE-2-RECEIPT.md). Routes, time, party movement,
lore, and campaign work remain excluded.

## Plan-quality audit

1. One adjacent-movement outcome with explicit non-goals: yes.
2. Product-authored rule basis and exact governing contracts: yes.
3. Existing topology/movement/projection owners searched and read: yes.
4. Every implemented dependency has code, contract, test, or receipt evidence: yes.
5. Relationship projection, traveller state, mechanic, and verification are leaves: yes.
6. State, derived values, input, effects, and later consequences have one owner: yes.
7. Each slice has its own usable contract/exit gate: yes.
8. Each implementation slice stopped at its separate verified exit gate: yes.
9. Closed/missing/null/empty semantics are explicit: yes.
10. Ordering, effects, result, and deterministic behavior are testable: yes.
11. Acceptance covers positive, invalid, missing/corrupt, routing, replay, state, event, cleanup: yes.
12. Repository validation is distinct from persistent import: yes.
13. Temporary fixtures and existing feature preservation are explicit: yes.
14. Exit gates are all-or-nothing: yes.
15. No executable runtime source or commit payload is copied here: yes.
16. Implementation evidence is recorded separately from this plan: yes.

## Plan-change rule

Revise before implementation if a generic relationship-projection owner already exists, projection
cannot preserve frozen deterministic replay, the reviewer changes `includeRelationships`, traveller
state, or the `presence` slot, Feature 1 adjacency semantics change, or a consumer requires time,
distance, group, or authorization behavior. Create a new feature plan rather than widening this
rule.

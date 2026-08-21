# World Feature 2 — Slice 2 receipt

Status: **Verified**
Completed: 2026-08-20

## Delivered

Slice 2 makes the Feature 1 fixture traversable. `game.core.world.traveller` is a closed active
marker, and `traveller.feature-02.fixture` begins at the fixture gate in `presence`. The active
`mechanic.game.core.world.location.move` accepts only empty input and the declared
traveller/origin/destination roles. It proves that the traveller is at the claimed origin, that
the locations are sibling active locations, and that exactly one valid stored adjacency connects
them in the frozen relationship projection. It then proposes precisely one `containment.move` to
the destination `presence` slot.

The accompanying travel procedure records the ownership boundary. No route, time, distance,
party, map, lore, quest, campaign state, or new event type was added.

`ActionRunner` now allocates the root operation ID before applying an accepted effect and passes it
to the effect applier as well as the operation log. Consequently, the existing
`world.containment.moved` event is correctly correlated to the action that caused it.

## Evidence

| Check | Result |
| --- | --- |
| Focused Feature 2 suite | `dotnet test DantesRoleplay.slnx --no-restore --filter FullyQualifiedName~CatalogWorldFeature2Tests` — **4/4 passed**. It imports the full catalog, moves gate → market → observatory in two fresh sessions, asserts deterministic outputs/effects, reads one structural event for each action, and covers disconnected, corrupt, stale, and inactive-traveller rejection. |
| Catalog validation | `roleplay validate catalog` — **103 records valid**; five non-blocking near-duplicate warnings. No live data touched. |
| Full regression suite | `dotnet test DantesRoleplay.slnx --no-restore` — **386/386 passed**. |
| Whitespace validation | `git diff --check` passed; Git reported only working-copy line-ending conversion warnings. |

## Boundary and handoff

Feature 2 is complete. This is one-hop movement over the stored Feature 1 graph, not route or
journey simulation. The next world capability should be planned as a separate feature, beginning
with the existing World Feature 3 faction and recurring-motive confirmation boundary.

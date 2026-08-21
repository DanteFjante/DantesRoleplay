# World Feature 7 — trusted-GM read handoff

**Audience:** Future website/API implementation  
**Authority:** [World Feature 7 dependency plan](WORLD-FEATURE-07-DEPENDENCY-PLAN.md) and
[`procedure.game.core.world.read`](../../catalog/procedures/game/core/world/procedure.game.core.world.read.md)

The website may use only the four read-only `query(kind: "graph")` recipes below. They are
trusted-GM views: do not expose them to players until an authenticated audience policy exists.

| View | Root supplied by consumer | Component IDs | Containment / relationship depth | Edge kinds | Caps |
| --- | --- | --- | --- | --- | --- |
| World overview | Active world-root ID | `game.core.world.root`, `game.core.world.location` | 2 / 1 | `game.core.world.location.connected-to` | 100 nodes / 100 edges |
| Location detail | One location ID | `game.core.world.location` | 1 / 1 | `game.core.world.location.connected-to` | 50 nodes / 50 edges |
| Faction detail | One faction ID | `game.core.world.faction`, `game.core.world.motive` | 0 / 1 | member, controls, allied-with, opposed-to faction links | 40 nodes / 50 edges |
| Knowledge detail | Active world-root ID | fact, rumour, secret, clue | 0 / 2 | in-world, about, clue-supports knowledge links | 100 nodes / 150 edges |

Every response has `rootId`, ordered `nodes`, ordered `edges`, and `truncated`. A node contains
its ID/name, selected components, and direct containment identity/slot. An edge contains its
source, target, kind, and authored object data. Treat a non-null `truncated` as an incomplete view;
the caller must not silently present it as complete.

No response contains map geometry, routes, distances, terrain, player-discovery state, or
authorization decisions. Use containment and canonical adjacency solely as topology preparation.

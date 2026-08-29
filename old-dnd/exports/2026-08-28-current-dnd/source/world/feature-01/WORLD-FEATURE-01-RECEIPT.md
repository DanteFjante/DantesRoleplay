# World Feature 1 — Slice 1 receipt

Status: **Verified**
Completed: 2026-08-20

## Delivered

Slice 1 establishes a catalog-owned, persistent shared-game world topology:

- `game.core.world.root` and `game.core.world.location` closed component contracts.
- `procedure.game.core.world.location`, which governs direct authored topology changes.
- One fixture world, one contained region, three contained locations, and two canonically ordered
  `game.core.world.location.connected-to` relationships.
- Fresh-import/readback and negative-contract coverage in `CatalogWorldFeature1Tests`.

No movement, routes, maps, lore, campaign, quest, event, mechanic, MCP-surface, migration, or
persistent-database import was added.

## Evidence

| Check | Result |
| --- | --- |
| Focused regression | `dotnet test DantesRoleplay.slnx --no-restore --filter FullyQualifiedName~CatalogWorldFeature1Tests` — **2/2 passed**. |
| Catalog validation | `roleplay validate catalog` — **99 records valid**; two existing lexical near-duplicate warnings (`procedure.event.guard` and `procedure.system.verify`), neither introduced by this slice. No live data touched. |
| Full regression suite | `dotnet test DantesRoleplay.slnx --no-restore` — **374/374 passed**. |
| Whitespace validation | `git diff --check` — passed; Git reported only line-ending conversion warnings for working-copy files. |

The focused test imports the repository catalog into fresh disposable databases, asserts the exact
component data, containment graph, canonical adjacency, a clean repeated import plan, and rejection
of closed-data and invalid-topology conventions. It also confirms that the existing Feature 10 hero
does not gain `game.core.world.*` data.

## Handoff

World Feature 1 Slice 1 is complete. The next world capability is **World Feature 2: governed actor
movement**. It needs a separate dependency plan and handoff; this receipt does not authorise it.

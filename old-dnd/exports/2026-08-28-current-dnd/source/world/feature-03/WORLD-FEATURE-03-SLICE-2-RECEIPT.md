# World Feature 3 — Slice 2 receipt

Status: **Verified**
Completed: 2026-08-20

## Delivered

Slice 2 adds `mechanic.game.core.world.faction.agenda`, the sole deterministic path to advance an
active faction agenda from `ready` to `advanced`. It accepts exactly one `faction` role and input
`{}`, validates the complete confirmed faction component, and returns exactly one complete
`component.set` effect. Every faction field except `agenda.state` is preserved; motives,
relationships, locations, and all other world state remain unchanged.

The existing `world.component.replaced` structural event and action audit record success. No
semantic faction event, subscription, notification, campaign, quest, clock, reactive advance, or
territorial behavior was added.

## Evidence

| Check | Result |
| --- | --- |
| Focused Feature 3 suite | `dotnet test DantesRoleplay.slnx --no-restore --filter FullyQualifiedName~CatalogWorldFeature3Tests` — **4/4 passed**. It covers fresh-session determinism, one replacement event, closed input, inactive/malformed/unknown/already-advanced state rejection, stale replay, and unrelated-state preservation. |
| Catalog validation | `roleplay validate catalog` — **110 records valid**; six non-blocking near-duplicate warnings. No live data touched. |
| Full regression suite | `dotnet test DantesRoleplay.slnx --no-restore` — **390/390 passed**. |
| Whitespace validation | `git diff --check` passed; Git reported only working-copy line-ending conversion warnings. |

## Boundary and handoff

Feature 3 is complete. It records one faction's state and advances its one initial agenda once; it
does not simulate factions. The next story-first candidate is the separately planned World Feature
4 knowledge, rumours, secrets, and clues capability.

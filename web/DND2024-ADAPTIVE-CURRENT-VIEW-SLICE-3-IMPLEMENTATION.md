# DND2024 adaptive Current View — Slice 3 implementation

Status: **source implementation complete; acceptance pending**
Ruleset alignment: **dnd2024-compatible read projection**
Owner/roadmap: `web/WEB-INTERFACE-ROADMAP.md`, Feature 5
Parent: `web/DND2024-WEB-INFORMATION-HUB-DEPENDENCY-TREE.md`, Current View Leaf 5

## Boundary

Project exact known ways onward into the read-only Current View. A displayed choice is one existing
active `dnd2024.game.core.world.route` whose availability is exactly `open`, whose exact `.from`
relationship is the current-scene location, whose exact `.to` relationship resolves to an active
same-projection location, and whose `.in-world` relationship matches the selected World.

For a Player view, both the route subject and destination subject must occur in already-admitted,
non-familiar authorized knowledge. Route descriptive visibility is not authorization. Player detail
comes from the admitted route knowledge entry, not from GM-authored route metadata. A DM view may
use the validated route summary after the route identity has been admitted by the same notebook
projection.

This slice does not calculate reachability, infer a reverse route, execute travel, expose closed or
malformed routes, create an available-action system, or change game state.

## Existing owners

- `procedure.game.core.world.travel` owns route shape, direction, scope, availability, and movement.
- `procedure.game.core.world.knowledge` and the authorized knowledge notebook own player admission.
- `game.core.campaign.current-scene`, runtime-qualified as
  `dnd2024.game.core.campaign.current-scene`, owns the current situation selector.
- The DND2024 connected-envelope adapter owns browser projection only.

No new permanent IDs, component schemas, relationship meanings, endpoints, migrations, catalog
records, live database writes, or namespace rules are introduced.

## Allowed files

- `src/system/web-interface/dnd2024/src/server/game-server-context.js`
- `src/system/web-interface/dnd2024/src/server/connected-hub-envelope.ts`
- `src/system/web-interface/dnd2024/src/data/hub-types.ts`
- focused DND2024 web tests for those adapters and Current View presentation
- this implementation document, its completion receipt, the parent dependency tree, and roadmap

## Closed output

The connected campaign envelope may carry `knownRoutes`, each with exact route/origin/destination
IDs, projected destination name, safe detail, on-foot mode, and authored duration. The hub adapter
attaches only routes whose origin equals the resolved current location to that location's existing
`routes` presentation field.

Any missing, duplicated, malformed, unavailable, cross-world, non-current-origin, unknown-target,
closed, archived, or unauthorized input omits that route. Partial route records never render.

## Verification

1. Focused adapter tests prove exact namespace IDs, relationship direction/scope, open availability,
   active destination validation, player knowledge admission, safe detail selection, and fail-closed
   cases.
2. Connected-envelope and Current View tests prove only the exact current location receives the
   projected routes.
3. Run the full DND2024 web suite and production server build.
4. Run catalog validation only if catalog files change; none are planned.

## Confirmation and stop

The user's 2026-08-30 instruction to continue implementing the Current tab confirms this separate
player-known route read projection. It does not confirm travel execution, available-action
semantics, another scene selector, activation/deployment, or final feature acceptance.

Stop after the read-only known-route projection and its evidence. The next Current View boundary is
blocked until an authored scene-affordance/action owner exists or travel execution is separately
requested and confirmed.

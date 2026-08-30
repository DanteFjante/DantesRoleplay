# DND2024 adaptive Current View — Slice 3 receipt

Date: 2026-08-30
Status: **source implementation complete; feature acceptance pending**
Ruleset alignment: **dnd2024-compatible read projection**

## Delivered boundary

The connected Current View can now show exact known ways onward during Exploration. The server
adapter admits only an active `dnd2024.game.core.world.route` with exact DND2024-qualified scope,
origin, destination, open availability, active destination state, and on-foot duration.

Player output additionally requires already-admitted non-familiar knowledge for both the route and
destination. Its route detail is the admitted knowledge text; GM-only route summary text is not
copied into Player output. The hub attaches the result only to the exact current Exploration
location. Conversation, Combat, missing knowledge, malformed relationships, unknown destinations,
and unavailable state render no route choices.

No route, component, relationship, schema, endpoint, migration, database state, or namespace was
created. All existing runtime identifiers retain the `dnd2024.` prefix.

## Evidence

- Focused Current View/route tests: **16/16 passed**.
- Production server build: **passed**, 1,622 modules transformed.
- Full DND2024 web run: **120/131 passed**. The 11 failures are the concurrently edited World
  chronology boundary: old fixtures omit its newly required envelope member, two knowledge-history
  assertions are not yet migrated, and two game-server expectations do not include its added
  request/output. The route tests pass in that same run; no route failure is hidden.
- Follow-up full run after the chronology boundary settled: **133/133 passed**, including every
  known-route and Current View test.
- Catalog validation was not rerun because this slice changes no catalog artifact.

## Deliberate exclusions

Travel execution, reverse-route inference, general pathfinding, map-derived reachability, declared
scene actions, conversation choices, combat actions, live activation, deployment, and final feature
acceptance remain outside this slice. An authored scene-affordance/action owner still does not exist.

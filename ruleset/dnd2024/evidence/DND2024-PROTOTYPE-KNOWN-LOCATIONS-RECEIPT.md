# D&D 2024 prototype known-locations receipt

Status: accepted
Implementation document: `DND2024-PROTOTYPE-KNOWN-LOCATIONS-IMPLEMENTATION.md`
Ruleset alignment: ruleset-neutral

## Delivered boundary

The existing generic authorized knowledge notebook now produces an identity-free directory of
known active locations. A location is included only when an already-authorized, non-familiar
knowledge record explicitly concerns an entity with the binding's active-location component/status.
The result contains the entity's display name and only the notebook entries already emitted to the
actor. The existing generic knowledge endpoint serializes the directory, and the connected
prototype renders the names without adding a detail or navigation endpoint.

The adapter fails closed for malformed location data. It does not infer places from prose and does
not accept browser-selected location, actor, world, campaign, or knowledge IDs.

## Evidence

- `dotnet build DantesRoleplay.MCPServer/DantesRoleplay.MCPServer.csproj --no-restore` with an
  isolated output path — passed: zero warnings/errors.
- Focused `KnowledgeCoreTests` — passed: 16 tests, including the active-location/familiarity/no-ID
  boundary.
- `node --test test/game-server-context.test.js` — passed: 6 tests, including malformed-location
  fail-closed coverage.
- `npm test` — passed: 122 tests.
- `npm run build` — passed: Vinext production build.
- `git diff --check` — passed.

The combined focused server filter also exercises unrelated existing web-page source checks; 12 of
those existing checks currently fail in this dirty checkout and are outside this projection. The
focused knowledge suite and the isolated server build passed.

## Deliberate exclusions

No D&D-specific C# logic, new route, component/schema/catalog record, live database write, location
description, component payload, ID, containment, route, map, faction, browser filtering, fixture
fallback, action path, or remote gateway was added. The active local server was not restarted.

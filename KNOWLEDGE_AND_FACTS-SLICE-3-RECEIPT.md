# Knowledge and facts — Slice 3 receipt

Completed: 2026-08-21

## Delivered

- `game.core.world.knowledge.validity`, an optional closed companion component whose minutes use
  the existing scoped world-root clock.
- `IKnowledgeTimelineCoordinator`, registered in host DI. It owns validity recording, canonical
  contradiction links, adjacent-interval supersession, and bounded trusted as-of projections.
- Existing Feature 4 records stay atemporal; they were not silently assigned a starting minute.
- Fixture history for a market toll at minute 119/120 and a concurrent contested observatory claim
  pair. Neither fixture changes actor knowledge state.
- Focused tests for historical/current projection, supersession, canonical contradiction replay,
  and rejection of future validity or reverse supersession.

## Verification

- `dotnet build DantesRoleplay.DataAccess/DantesRoleplay.DataAccess.csproj --no-restore` passed
  with zero warnings and zero errors.
- The focused test assembly compiled successfully with one pre-existing xUnit analyzer warning in
  `KnowledgeAcquisitionCoordinatorTests`. The local test runner then aborted before discovery
  because its copied x64 `testhost.exe` could not resolve the installed runtime; this is a local
  test-host deployment issue, not a test assertion failure.
- `roleplay validate catalog` passed with the added records and touched no live data. Its only
  output beyond success was the repository's existing near-duplicate warnings.

## Explicit exclusions retained

No world-clock duplication in knowledge data, real-world timestamps, scheduled future truth,
acquisition timestamps, player/public querying, MCP surface, vector retrieval, or generic temporal
engine was added.
